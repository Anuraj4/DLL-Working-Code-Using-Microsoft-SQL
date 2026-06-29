using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using Serilog;
using Serilog.Context;
using Edi.Generator835.Services;
using Edi.Generator835.Services.Interfaces;
using Edi.Generator835.Configuration;
using Edi.Generator835.Context;
using Edi.Generator835.Generators;
using Edi.Generator835.Models;
using Edi.Generator835.Rules;
using Edi.Generator835.Readers;
using EdiFabric.Core.Model.Edi;
using EdiFabric.Core.Model.Edi.X12;
using EdiFabric.Framework.Readers;
using EdiFabric.Framework.Writers;
using EdiFabric.Templates.Hipaa5010;
using Xalta.Edi.BalancingValidation;
using Xalta.Edi.BalancingValidation.Validators;
using Xalta.Edi.CodeCrossWalk.Interfaces;
using Xalta.Edi.CodeCrossWalk.Providers;
using Xalta.Edi.CodeCrossWalk.Services;
using Xalta.Edi.BalancingValidation.Core;
using Newtonsoft.Json;

namespace Edi.Generator835.Pipeline
{
    public class Edi835Pipeline : IEdi835Pipeline
    {
        private readonly IMappingProvider? _mappingProvider;
        private readonly IDataNormalizer _normalizer;
        private readonly IPatientResponsibilityFixer _prFixer;

        public Edi835Pipeline(IMappingProvider mappingProvider, IDataNormalizer normalizer, IPatientResponsibilityFixer prFixer)
        {
            _mappingProvider = mappingProvider;
            _normalizer = normalizer;
            _prFixer = prFixer;
        }

        public Edi835Pipeline(IMappingProvider mappingProvider)
            : this(mappingProvider, new DataNormalizationService(), new PatientResponsibilityFixerService())
        {
        }

        public Edi835Pipeline()
        {
            _normalizer = new DataNormalizationService();
            _prFixer = new PatientResponsibilityFixerService();
        }

        public async Task<PipelineResult> Execute(string inputExcelPath, string outputDirectory, MappingConfiguration mappings, GenerationContext context, bool enableAppsmith = false)
        {
            var result = new PipelineResult { InputFilePath = inputExcelPath };
            string sourceBaseName = Path.GetFileNameWithoutExtension(inputExcelPath);
            string safeBaseName = sourceBaseName.Length > 20 ? sourceBaseName.Substring(0, 20) : sourceBaseName;
            string logFileName = $"{safeBaseName}_{DateTime.Now:yyyyMMdd_HHmmss}";

            using (LogContext.PushProperty("LogFileName", logFileName))
            {
                // Note: Logging is initialized externally by the Orchestrator or A360Generator.
                // Do NOT call LoggingProvider.Initialize here to avoid redirecting/disrupting the shared logger.
                Log.Information("Starting EDI 835 Generation Pipeline for {InputFile}", Path.GetFileName(inputExcelPath));

                var timer = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    // 1. Convert CSV if necessary
                    if (Path.GetExtension(inputExcelPath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Information("Input is CSV. Converting to temporary Excel file using 'csv_mapper' config...");
                        var mapper = new CsvToExcelMapper(mappings);
                        string tempExcel = Path.Combine(outputDirectory, $"temp_converted_{DateTime.Now:HHmmss}.xlsx");
                        mapper.Convert(inputExcelPath, tempExcel);
                        inputExcelPath = tempExcel; // Switch specific path for this execution
                        Log.Information("CSV conversion complete. Using intermediate file: {TempPath}", tempExcel);
                    }

                    // 2. Setup Rules Engine
                    Log.Information("Step 2: Setting up rules engine via RuleRegistry...");
                    var ruleEngine = RuleRegistry.CreateEngine(mappings);

                    // 2.1 Setup CrossWalk Service (Master Mappings)
                    ICodeCrossWalkService? crossWalkService = null;
                    string crossWalkPath = Path.Combine(Path.GetDirectoryName(inputExcelPath) ?? "", "CARC_GAGC_Mapping_Master.xlsx");
                    if (File.Exists(crossWalkPath))
                    {
                        // Multi-sheet mode enabled to handle different sheets as tables
                        var crossWalkProvider = new ExcelCodeCrossWalkProvider(crossWalkPath, isMultiSheet: true);
                        crossWalkService = new CodeCrossWalkService(crossWalkProvider);
                    }

                    // 3. Read Excel Data
                    var reader = new EobExcelReader();
                    Edi835DataModel model = reader.ReadEobData(inputExcelPath, mappings);

                    // 3.1 Normalize Data (Centralized Enterprise Cleaning)
                    Log.Information("Step 3.1: Normalizing extracted data model...");
                    _normalizer.Normalize(model, mappings);
                    _normalizer.SynchronizeContext(model);

                    // Resolve Currency Code from Mapping if symbol was detected
                    if (!string.IsNullOrEmpty(model.Header.CurrencySymbol))
                    {
                        model.Header.CurrencyCode = ResolveCurrencyCode(model.Header.CurrencySymbol, mappings);
                    }

                    // 3.1.5 Fix Patient Responsibility based on breakdown
                    Log.Information("Step 3.1.5: Fixing patient responsibility amounts...");
                    _prFixer.FixPatientResponsibility(model);

                    // 3.1.6 Detect Swapped Allowed/Adjustment Amounts
                    Log.Information("Step 3.1.6: Detecting swapped Allowed/Adjustment amounts...");
                    var swapDetector = new Edi.Generator835.Services.SwapDetectionService();
                    // var swappedLines = swapDetector.DetectSwappedLines(model);



                    // 3.2 Run Math Balancing on all service lines (before generator)
                    Log.Information("Step 3.2: Running math balancing on service lines...");
                    var balancingRule = ruleEngine.Rules.OfType<MathBalancingRule>().FirstOrDefault() ?? new MathBalancingRule();
                    foreach (var claim in model.Claims)
                    {
                        // 3.2.1 Handle claim-level sequestration detection and distribution
                        balancingRule.PreProcessClaimSequestration(claim);

                        foreach (var line in claim.ServiceLines)
                        {
                            var key = (claim.ClaimIdPayer ?? "", line.ServiceLineId ?? "");
                            bool performGapResolution = true;
                            // if (swappedLines.Contains(key))
                            // {
                            //     Log.Warning("[SWAP-DETECT] Running MathBalancingRule in Mapping-only mode for Claim {ClaimId}, Service Line {ServiceLineId} due to detected swap.",
                            //         claim.ClaimIdPayer, line.ServiceLineId);
                            //     performGapResolution = false;
                            // }

                            var balanced = balancingRule.BalanceServiceLine(line, claim, model.Header, mappings, performGapResolution);
                            line.Adjustments.Clear();
                            foreach (var adj in balanced) line.Adjustments.Add(adj);
                        }
                    }
                    _normalizer.SynchronizeContext(model);
                    Log.Information("Math balancing complete.");

                    // 3.3 Write canonical model back to Excel (inspectable audit trail)
                    Log.Information("Step 3.3: Writing canonical model back to Excel...");
                    try
                    {
                        var canonicalWriter = new CanonicalExcelWriter();
                        canonicalWriter.WriteBack(inputExcelPath, model);
                        Log.Information("Canonical Excel written successfully.");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to write canonical Excel (non-fatal). Continuing with EDI generation.");
                    }

                    // 4. EDI Generation (reads clean, balanced data)
                    Log.Information("Step 4: Generating EDI 835 structure...");
                    var generator = new Edi835Generator(ruleEngine, context, mappings, crossWalkService);
                    TS835 transaction = generator.Generate(model);
                    Log.Information("EDI structure generated successfully.");

                    // 5. Serialize to File
                    string controlNum = context.ControlNumbers.NextTransactionControlNumber();
                    // 4. Persistence
                    Log.Information("Step 4: Writing EDI file to disk...");
                    //string fileName = $"835_{sourceBaseName}_{model.Header.CheckOrEftNumber}_{DateTime.Now:yyyyMMddHHmmss}.edi";
                    // string fileName = $"{sourceBaseName.Replace("_Mapped", "").Split('_')[1]}.edi";

                    string baseName = sourceBaseName.Replace("_Mapped", "");
                    string[] parts = baseName.Split('_');
                    string fileName = parts.Length > 1 ? $"{parts[1]}.edi" : $"{baseName}.edi";
                    string outputPath = Path.Combine(outputDirectory, fileName);

                    WriteEdiFile(outputPath, transaction, model.Header, controlNum, mappings);
                    result.OutputFilePath = outputPath;

                    // 5.1 Post-processing: Remove BOM from generated file (tested logic from script)
                    try
                    {
                        Log.Information("Step 5.1: Removing UTF-8 BOM from generated EDI file...");
                        RemoveBom(outputPath);
                        Log.Information("BOM removed successfully.");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to remove BOM from generated file (non-fatal). Continuing with validation.");
                    }

                    // Verify Output
                    var fi = new FileInfo(outputPath);
                    Log.Information("EDI file saved to {OutputPath}. Size: {Size} bytes", outputPath, fi.Length);
                    if (fi.Length == 0)
                    {
                        Log.Error("CRITICAL: Generated EDI file is 0 bytes!");
                    }

                    // 6. Validate Output
                    try
                    {
                        TS835? writtenTransaction = null;
                        using (var stream = File.OpenRead(outputPath))
                        {
                            var ediReader = new X12Reader(stream, "EdiFabric.Templates.Hipaa");
                            while (ediReader.Read())
                            {
                                if (ediReader.Item is TS835 ts)
                                {
                                    writtenTransaction = ts;
                                    break;
                                }
                            }
                        }

                        if (writtenTransaction != null)
                        {
                            var allErrors = new List<EdiSegmentError>();

                            // 6.3 SNIP Level 3 (Balancing)
                            var snip3 = EdiValidatorFactory.CreateSnip3();
                            var res3 = snip3.Validate(writtenTransaction);
                            if (!res3.IsValid) allErrors.AddRange(res3.Errors);

                            // 6.4 SNIP Level 4 (Balancing)
                            var snip4 = EdiValidatorFactory.CreateSnip4();
                            var res4 = snip4.Validate(writtenTransaction);
                            if (!res4.IsValid) allErrors.AddRange(res4.Errors);

                            if (allErrors.Count > 0)
                            {
                                var deduplicatedErrors = new List<EdiSegmentError>();

                                foreach (var group in allErrors.GroupBy(e => new { e.SegmentName, e.SegmentPosition }))
                                {
                                    var uniqueElements = group.SelectMany(g => g.ElementErrors).Distinct().ToList();

                                    // Modified: Even if uniqueElements is empty, we still add the segment error.
                                    // This ensures structural or balancing errors (which often have 0 element details) 
                                    // are still visible in the logs.
                                    var correctLoop = group.FirstOrDefault(g => g.Loop != "N/A" && g.Loop != "UNKNOWN")?.Loop ?? "N/A";
                                    deduplicatedErrors.Add(new EdiSegmentError(
                                        group.Key.SegmentName,
                                        group.Key.SegmentPosition,
                                        correctLoop,
                                        uniqueElements
                                    ));
                                }

                                result.ValidationResult = new EdiValidationResult(false, deduplicatedErrors, ValidatedAt: DateTime.UtcNow);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        var sysError = new EdiSegmentError("SYS", -1, "N/A", new List<EdiElementError> {
                         new EdiElementError("SYS", -1, "ERR", "System Error", "Exception", $"[Exception during validation] {ex.Message}", null, ex.StackTrace)
                        });
                        result.ValidationResult = new EdiValidationResult(false, new List<EdiSegmentError> { sysError }, ValidatedAt: DateTime.UtcNow);
                    }

                    result.Success = result.ValidationResult == null || result.ValidationResult.IsValid;

                    // 6.5 Store in Valid/Invalid folder
                    string subFolder = result.Success ? "Valid" : "Invalid";
                    string finalDir = Path.Combine(outputDirectory, subFolder);
                    if (!Directory.Exists(finalDir)) Directory.CreateDirectory(finalDir);

                    string finalPath = Path.Combine(finalDir, fileName);
                    if (File.Exists(outputPath))
                    {
                        if (File.Exists(finalPath)) File.Delete(finalPath);
                        File.Move(outputPath, finalPath);
                        result.OutputFilePath = finalPath;
                        Log.Information("Moved EDI file to {FinalPath} based on validation status.", finalPath);
                    }

                    if (result.ValidationResult != null && !result.ValidationResult.IsValid)
                    {
                        Log.Warning("Pipeline completed with {ErrorCount} validation segment error(s):", result.ValidationResult.Errors.Count);
                        foreach (var err in result.ValidationResult.Errors)
                        {
                            var msg = string.Join("; ", err.ElementErrors.Select(e => e.Message));
                            Log.Warning("  {ValidationError}", $"Segment {err.SegmentName}: {msg}");
                        }
                    }

                    // 7. A360 Bot Integration (Enterprise Grade Escalation)
                    if (enableAppsmith) // Using the same flag to trigger the bot
                    {
                        try
                        {
                            /* 
                            // Appsmith Code
                            var appsmithUrl = mappings.GetSetting("Appsmith_ApiUrl");
                            if (string.IsNullOrEmpty(appsmithUrl))
                            {
                                appsmithUrl = "https://devtest-2.appsmith.com/api/v1/workflows/trigger/698032eb417dd615e8d324b4?api-key=ea973685906228322c6ae6c5407d1ab22748ef86116b35201f57d5e079538d8280e0bdea1829f491f94c3ff1ce7ff4774764dd0fa8b2e7500e3cc30a66fc7edec8038e2b0a335dab3b89a72ba8de2d23157ee382ca8a66fd9ad3d3157c77659dbe7604fd8df95162c3c4732a716aafbd49090d2ff9f838317915302dc3979d3861f428d1896758e3b403366c5c843f8c91639d230ed4a8c6e55c53588ba02e422a028bb857cbfe79980c0f85bdef6f161607f2a22e08ee3feda1ef394dc72d124026a177536852757d49472744494f7d4874d47a5548354a0b5ea6d70ace5d24";
                            }
                            var appsmithKey = mappings.GetSetting("Appsmith_ApiKey"); // Optional if embedded in URL

                            if (!string.IsNullOrEmpty(appsmithUrl))
                            {
                                var appsmithService = new AppsmithService(appsmithUrl, appsmithKey ?? "");
                            */

                            // A360 configuration
                            var a360BaseUrl = mappings.GetSetting("A360_BaseUrl");
                            if (string.IsNullOrEmpty(a360BaseUrl))
                            {
                                a360BaseUrl = "https://community.cloud.automationanywhere.digital";
                            }
                            var a360Username = mappings.GetSetting("A360_Username") ?? "username";
                            var a360Password = mappings.GetSetting("A360_Password") ?? "password";
                            var botIdStr = mappings.GetSetting("A360_BotId") ?? "101152341";
                            var deviceIdStr = mappings.GetSetting("A360_DeploymentDeviceId") ?? "173";
                            var automationName = mappings.GetSetting("A360_AutomationName") ?? "835_Validator_Bot";
                            var timeoutStr = mappings.GetSetting("A360_TimeoutSeconds");
                            var botRunType = mappings.GetSetting("A360_BotRunType") ?? "A";
                            var runAsUserIdStr = mappings.GetSetting("A360_RunAsUserId");

                            int botId = int.TryParse(botIdStr, out var bid) ? bid : 101195483;
                            int deviceId = int.TryParse(deviceIdStr, out var did) ? did : 173;
                            int timeoutSeconds = int.TryParse(timeoutStr, out var ts) && ts > 0 ? ts : 120;
                            long runAsUserId = long.TryParse(runAsUserIdStr, out var ruid) ? ruid : 0;

                            if (!string.IsNullOrEmpty(a360BaseUrl))
                            {
                                var a360Service = new A360Service(a360BaseUrl, a360Username, a360Password, botId, deviceId, automationName, botRunType, runAsUserId, timeoutSeconds);

                                // Parse EDI file into objects for the payload as requested
                                var parsedEdiData = new List<IEdiItem>();
                                if (File.Exists(result.OutputFilePath))
                                {
                                    using (var stream = File.OpenRead(result.OutputFilePath))
                                    {
                                        using (var ediReader = new X12Reader(stream, "EdiFabric.Templates.Hipaa"))
                                        {
                                            while (ediReader.Read())
                                            {
                                                parsedEdiData.Add(ediReader.Item);
                                            }
                                        }
                                    }
                                }

                                //Console.WriteLine("parsed edi data: " + JsonConvert.SerializeObject(parsedEdiData) );



                                // Construct payload as Dictionary<string, object> with requested keys
                                var flattenedErrors = result.ValidationResult?.FlattenedErrors ?? new List<ValidationError>();
                                var errorStrings = flattenedErrors.Select(e => e.ToString()).Distinct().ToList();

                                // Log errors being sent to A360
                                if (errorStrings.Any())
                                {
                                    Console.WriteLine("\n[A360] Sending Validation Errors:");
                                    foreach (var errStr in errorStrings)
                                    {
                                        Console.WriteLine($"  - {errStr}");
                                    }
                                    Console.WriteLine();
                                }

                                var payload = new Dictionary<string, object>
                            {
                                { "payment_id", model.Header.PaymentId },
                                { "parsed_edi_data", parsedEdiData },
                                { "validation_errors", errorStrings }, // Keep structured errors if needed
                                // { "formatted_errors", flattenedErrors },      // Send as list of strings as requested
                                { "fileName", Path.GetFileName(result.OutputFilePath?.Replace(".edi", "") ?? "Unknown") },
                                { "timestamp", DateTime.UtcNow },
                                // { "success", result.Success },
                                { "is_valid_edi", result.Success },
                                {"a360_master_config_path", mappings.GetSetting("A360_MasterConfigFilePath")}
                            };

                                // await appsmithService.TriggerWorkflowAsync(payload);
                                await a360Service.TriggerBotAsync(payload);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log but do not fail pipeline if A360 fails
                            var appsmithErr = new EdiSegmentError("SYSTEM", 0, "A360", new List<EdiElementError> {
                                new EdiElementError("A360", -1, "ERR", "Escalation", "BotError", $"[Warning] A360 Escalation Failed: {ex.Message}", null, ex.StackTrace)
                            });
                            if (result.ValidationResult == null)
                                result.ValidationResult = new EdiValidationResult(true, new List<EdiSegmentError> { appsmithErr });
                            else
                                result.ValidationResult.Errors.Add(appsmithErr);
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.ToString();
                    Log.Error(ex, "Pipeline FAILED for {InputFile}: {Message}", Path.GetFileName(inputExcelPath), ex.Message);
                }
                finally
                {
                    timer.Stop();
                    result.ExecutionTime = timer.Elapsed;
                    Log.Information("Pipeline execution finished in {Elapsed}. Success: {Success}", result.ExecutionTime, result.Success);
                    // NOTE: Do NOT call Log.CloseAndFlush() here - the logger is shared across parallel threads.
                    // The Orchestrator or Runner is responsible for closing the logger.
                }

                return result;
            }
        }

        private string ResolveCurrencyCode(string symbol, MappingConfiguration mappings)
        {
            if (mappings.MappingTables.TryGetValue("currency_map", out var table))
            {
                if (table.TryGetValue(symbol, out var code))
                    return code;
            }

            return symbol switch
            {
                "$" => "USD",
                "€" => "EUR",
                "£" => "GBP",
                _ => "USD"
            };
        }

        private void WriteEdiFile(string path, TS835 transaction, HeaderData header, string controlNum, MappingConfiguration mappings)
        {
            char componentSeperator = mappings.GetSetting("ISA_ComponentElementSeparator", ":")[0];
            var isa = new ISA
            {
                AuthorizationInformationQualifier_1 = mappings.GetFixedDefault("ISA_AuthorizationInformationQualifier", "00"),
                AuthorizationInformation_2 = "".PadRight(10),
                SecurityInformationQualifier_3 = mappings.GetFixedDefault("ISA_SecurityInformationQualifier", "00"),
                SecurityInformation_4 = "".PadRight(10),
                SenderIDQualifier_5 = mappings.GetSetting("ISA_SenderIDQualifier", "ZZ"),
                InterchangeSenderID_6 = mappings.GetSetting("ISA06_InterchangeSenderID", header.PayerId.PadRight(15)),
                ReceiverIDQualifier_7 = mappings.GetSetting("ISA_ReceiverIDQualifier", "ZZ"),
                InterchangeReceiverID_8 = mappings.GetSetting("ISA08_Interchange_Receiver_ID", header.ProviderNpi.PadRight(15)),
                InterchangeDate_9 = DateTime.Now.ToString("yyMMdd"),
                InterchangeTime_10 = DateTime.Now.ToString("HHmm"),
                InterchangeControlStandardsIdentifier_11 = mappings.GetSetting("ISA_RepetitionSeparator", "^"),
                InterchangeControlVersionNumber_12 = mappings.GetFixedDefault("ISA_InterchangeControlVersionNumber", "00501").PadLeft(5, '0'), // ISA12 is 5 chars
                InterchangeControlNumber_13 = controlNum.PadLeft(9, '0'),
                AcknowledgementRequested_14 = mappings.GetSetting("ISA_AcknowledgmentRequested", "0"),
                UsageIndicator_15 = mappings.GetSetting("ISA_UsageIndicator", "P"),
                ComponentElementSeparator_16 = componentSeperator.ToString()
            };

            var gs = new GS
            {
                CodeIdentifyingInformationType_1 = mappings.GetFixedDefault("GS_FunctionalIdentifierCode", "HP"),
                SenderIDCode_2 = mappings.GetSetting("GS02_Application_Senders_Code", header.PayerId),
                ReceiverIDCode_3 = mappings.GetSetting("GS03_Application_Receiver_Code", header.ProviderNpi ?? header.ProviderTaxId),
                Date_4 = DateTime.Now.ToString("yyyyMMdd"),
                Time_5 = DateTime.Now.ToString("HHmm"),
                GroupControlNumber_6 = "1",
                TransactionTypeCode_7 = mappings.GetFixedDefault("GS_TransactionTypeCode", "X"), // X12 835
                VersionAndRelease_8 = mappings.GetFixedDefault("GS_VersionAndRelease", "005010X221A1")
            };

            var settings = new X12WriterSettings
            {
                AutoTrailers = true,
                //Separators = EdiFabric.Framework.Separators.X12,
                // Postfix = Environment.NewLine,
                Separators =
                {
                    ComponentDataElement = componentSeperator,
                }
            };

            using (var stream = File.Create(path))
            using (var writer = new X12Writer(stream, settings))
            {
                Log.Information("[DEBUG] WriteEdiFile: Writing ISA, GS, and TS835 with {LoopCount} loops.", transaction.Loop2000?.Count ?? 0);
                writer.Write(isa);
                writer.Write(gs);
                writer.Write(transaction);
            }
            Log.Information("WriteEdiFile completed and stream disposed for {Path}", path);
        }

        private void RemoveBom(string filePath)
        {
            if (!File.Exists(filePath)) return;

            // Read (auto-detects BOM if present)
            string content;
            using (var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                content = reader.ReadToEnd();
            }

            // Write WITHOUT BOM
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(filePath, content, utf8NoBom);
        }
    }
}

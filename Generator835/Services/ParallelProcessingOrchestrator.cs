using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Edi.Generator835.Configuration;
using Edi.Generator835.Context;
using Edi.Generator835.Pipeline;
using Edi.Generator835;

namespace Edi.Generator835.Services
{
    public class ParallelProcessingOrchestrator
    {
        private readonly CsvToExcelMapper _mapper;
        private readonly ExcelTemplateService _templateService;
        private readonly IEdi835Pipeline _ediPipeline;
        private readonly MappingConfiguration _mappings;

        public ParallelProcessingOrchestrator(
            CsvToExcelMapper mapper,
            ExcelTemplateService templateService,
            IEdi835Pipeline ediPipeline,
            MappingConfiguration mappings)
        {
            _mapper = mapper;
            _templateService = templateService;
            _ediPipeline = ediPipeline;
            _mappings = mappings;
        }

        public async Task<List<PipelineResult>> ProcessFolderAsync(string inputPath, bool enableParallelProcessing = true, int maxDegreeOfParallelism = -1, bool enableAppsmith = false)
        {
            var results = new List<PipelineResult>();
            List<string> inputFiles = new List<string>();
            bool isDirectExcelInput = false;

            if (File.Exists(inputPath))
            {
                inputFiles.Add(inputPath);
                if (inputPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || inputPath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
                {
                    isDirectExcelInput = true;
                }
            }
            else if (Directory.Exists(inputPath))
            {
                var excelFiles = Directory.GetFiles(inputPath, "*.xlsx", SearchOption.TopDirectoryOnly);
                if (excelFiles.Length > 0)
                {
                    inputFiles.AddRange(excelFiles);
                    isDirectExcelInput = true;
                }
                else
                {
                    inputFiles.AddRange(Directory.GetFiles(inputPath, "*.csv", SearchOption.TopDirectoryOnly));
                }
            }
            else
            {
                Log.Error("Input path not found: {Path}", inputPath);
                return results;
            }

            if (inputFiles.Count == 0)
            {
                Log.Warning("No input files found to process in {Path}", inputPath);
                return results;
            }

            Log.Information("Starting Enterprise Pipeline. Found {Count} input files. Input Type: {Type}",
                inputFiles.Count, isDirectExcelInput ? "Excel (Direct EDI Generation)" : "CSV (Requires Mapping)");
            Log.Information("Parallel Execution Enabled: {Enabled}", enableParallelProcessing);

            // 1. Create hierarchical batch folders
            var folders = _templateService.CreateOutputFolders(inputFiles.Count);

            // Re-configure logging to also write to this specific batch's log folder
            LoggingProvider.InitializeSafe(folders.LogsFolder);

            var generatedExcelFiles = new List<string>();

            if (!isDirectExcelInput)
            {
                // PHASE 1: CSV -> Mapped Excel
                Log.Information("=========================================");
                Log.Information("PHASE 1: Mapping CSVs to Excel Templates");
                Log.Information("=========================================");

                int mappedCount = 0;
                int totalToMap = inputFiles.Count;

                foreach (var csvFile in inputFiles)
                {
                    try
                    {
                        Log.Information("Mapping file: {FileName}", Path.GetFileName(csvFile));
                        string outputExcelPath = _templateService.PrepareOutputPath(folders.MappedFolder, csvFile);
                        _mapper.Convert(csvFile, outputExcelPath);
                        generatedExcelFiles.Add(outputExcelPath);
                        Log.Information("Successfully mapped: {FileName}", Path.GetFileName(csvFile));

                        mappedCount++;
                        Console.Error.Write($"\rPHASE 1: Mapping CSVs to Excel... [{mappedCount}/{totalToMap}] ");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error mapping file {FileName}: {Message}", Path.GetFileName(csvFile), ex.Message);
                    }
                }
                Console.Error.WriteLine();

                Log.Information("Phase 1 Complete. {Count} mapped Excel files generated.", generatedExcelFiles.Count);

                if (generatedExcelFiles.Count == 0)
                {
                    Log.Warning("No files mapped successfully. Aborting Phase 2.");
                    return results;
                }
            }
            else
            {
                Log.Information("=========================================");
                Log.Information("PHASE 1: SKIPPED (Direct Excel Input Detected)");
                Log.Information("=========================================");

                foreach (var file in inputFiles)
                {
                    try
                    {
                        string destFile = Path.Combine(folders.MappedFolder, Path.GetFileName(file));
                        File.Copy(file, destFile, true);
                        generatedExcelFiles.Add(destFile);
                        Log.Information("Copied input Excel to batch folder: {FileName}", Path.GetFileName(file));
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to copy input file {FileName}", Path.GetFileName(file));
                        generatedExcelFiles.Add(file);
                    }
                }
            }

            // PHASE 2: Mapped Excel -> EDI 835
            Log.Information("=========================================");
            Log.Information("PHASE 2: Generating EDI 835 Transactions");
            Log.Information("=========================================");

            int startControl = int.Parse(_mappings.GetSetting("Interchange_StartControlNumber", "1"));
            var controlProvider = new SequentialControlNumberProvider(startControl);
            var generationContext = new GenerationContext(controlProvider);

            int successCount = 0;
            int errorCount = 0;
            int processedCount = 0;
            int totalToProcess = generatedExcelFiles.Count;

            foreach (var excelFile in generatedExcelFiles)
            {
                try
                {
                    Log.Information("Executing pipeline for: {FileName}", Path.GetFileName(excelFile));

                    var result = await _ediPipeline.Execute(excelFile, folders.EdiFolder, _mappings, generationContext, enableAppsmith: enableAppsmith);
                    results.Add(result);

                    processedCount++;
                    Console.Error.Write($"\rPHASE 2: Generating EDI 835...  [{processedCount}/{totalToProcess}] ");

                    if (result.Success)
                    {
                        successCount++;
                        Log.Information("Successfully generated EDI file: {FileName}", Path.GetFileName(result.OutputFilePath));
                    }
                    else
                    {
                        errorCount++;
                        var errSegments = result.ValidationResult?.Errors.Select(e => e.SegmentName).Distinct() ?? new[] { "Unknown" };
                        Log.Error("Failed to generate EDI for {FileName}. Errors in: {Segments}", Path.GetFileName(excelFile), string.Join(", ", errSegments));
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Log.Error(ex, "Critical error executing pipeline for {FileName}: {Message}", Path.GetFileName(excelFile), ex.Message);
                }
            }

            Log.Information("=========================================");
            Log.Information("BATCH PROCESSING COMPLETE");
            Log.Information("Total processed: {Total}. Success: {Success}. Failed: {Failed}", generatedExcelFiles.Count, successCount, errorCount);
            Log.Information("Artifacts saved to: {RootFolder}", folders.RootFolder);

            LoggingProvider.Shutdown();
            return results;
        }
    }
}

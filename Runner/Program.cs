using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Edi.Generator835;
using Newtonsoft.Json;
using Xalta.Edi.BalancingValidation.Core;
using Edi.Generator835.Pipeline;

namespace Runner
{
    class Program
    {
        // Exit Codes for A360
        private const int EXIT_SUCCESS = 0;
        private const int EXIT_ERROR = 1;
        private const int EXIT_INVALID_ARGS = 2;

        static int Main(string[] args)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var parsed = ParseArgs(args);

                // --help
                if (parsed.ContainsKey("help"))
                {
                    PrintUsage();
                    return EXIT_SUCCESS;
                }

                // Resolve parameters (named flags or positional fallback)
                string inputPath = GetParam(parsed, new[] { "input", "i" }, positionalIndex: 0, args: args);
                string outputPath = GetParam(parsed, new[] { "output", "o" }, positionalIndex: 1, args: args);
                string configPath = GetParam(parsed, new[] { "config", "c" }, positionalIndex: 2, args: args);
                string templatePath = GetParam(parsed, new[] { "template", "t" }, positionalIndex: 3, args: args);
                bool parallel = GetBoolParam(parsed, new[] { "parallel", "p" }, defaultValue: true);
                bool uploadToApi = GetBoolParam(parsed, new[] { "upload", "u" }, defaultValue: false);

                // Validate required parameters
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(inputPath)) missing.Add("--input (-i)");
                if (string.IsNullOrWhiteSpace(outputPath)) missing.Add("--output (-o)");
                if (string.IsNullOrWhiteSpace(configPath)) missing.Add("--config (-c)");
                if (string.IsNullOrWhiteSpace(templatePath)) missing.Add("--template (-t)");

                if (missing.Count > 0)
                {
                    Console.Error.WriteLine($"ERROR: Missing required parameters: {string.Join(", ", missing)}");
                    Console.Error.WriteLine("Run with --help for usage information.");
                    PrintUsage();
                    return EXIT_INVALID_ARGS;
                }

                // Validate paths exist
                if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
                {
                    WriteResult("ERROR", $"Input path not found: {inputPath}", outputPath, 0, sw.Elapsed);
                    return EXIT_ERROR;
                }
                if (!File.Exists(configPath))
                {
                    WriteResult("ERROR", $"Config file not found: {configPath}", outputPath, 0, sw.Elapsed);
                    return EXIT_ERROR;
                }
                if (!File.Exists(templatePath))
                {
                    WriteResult("ERROR", $"Template file not found: {templatePath}", outputPath, 0, sw.Elapsed);
                    return EXIT_ERROR;
                }

                // Ensure output directory exists
                Directory.CreateDirectory(outputPath);

                // Banner
                Console.Error.WriteLine("==================================================");
                Console.Error.WriteLine("   A360 EOB TO EDI 835 PROCESSOR  (A360 Runner)");
                Console.Error.WriteLine("==================================================");
                Console.Error.WriteLine($"  Input:    {inputPath}");
                Console.Error.WriteLine($"  Output:   {outputPath}");
                Console.Error.WriteLine($"  Config:   {configPath}");
                Console.Error.WriteLine($"  Template: {templatePath}");
                Console.Error.WriteLine($"  Parallel: {parallel}");
                Console.Error.WriteLine($"  Appsmith: {uploadToApi}");
                Console.Error.WriteLine("--------------------------------------------------");

                // Initialize Logging
                Console.Error.WriteLine("Initializing logging and rules engine...");
                Edi.Generator835.Services.LoggingProvider.Initialize(Path.Combine(outputPath, "logs"));

                // Run the pipeline
                Console.Error.WriteLine("Loading configuration and preparing batches...");
                var generator = new EnterpriseGenerator(configPath, templatePath, outputPath);

                Console.Error.WriteLine("Starting processing pipeline...");
                var results = generator.RunAsync(inputPath, parallel, uploadToApi).GetAwaiter().GetResult();

                sw.Stop();

                // 10. Show rich validation results for each file
                Console.Error.WriteLine("--------------------------------------------------");
                Console.Error.WriteLine("FILE-WISE PROCESSING SUMMARY:");
                foreach (var res in results)
                {
                    string fileName = Path.GetFileName(res.InputFilePath);
                    string status = res.Success
                        ? (res.ValidationResult != null ? EdiValidationFormatter.ToSummary(res.ValidationResult) : "✅ SUCCESS")
                        : $"❌ FAILED ({res.ErrorMessage})";

                    Console.Error.WriteLine($"{fileName.PadRight(40)} : {status}");
                }

                // 11. Detailed reports for issues only
                var issues = results.Where(r => !r.Success || (r.ValidationResult != null && !r.ValidationResult.IsValid)).ToList();
                if (issues.Any())
                {
                    Console.Error.WriteLine("\nDETAILED ISSUE REPORTS:");
                    foreach (var res in issues)
                    {
                        Console.Error.WriteLine($"\n>>> {Path.GetFileName(res.InputFilePath)}");
                        if (res.ValidationResult != null)
                        {
                            Console.Error.WriteLine(EdiValidationFormatter.ToConsole(res.ValidationResult));
                        }
                        else
                        {
                            Console.Error.WriteLine($"  Error: {res.ErrorMessage}");
                        }
                    }
                }

                // Count output files for the final summary
                int filesProcessed = results.Count(r => r.Success);
                int filesWithErrors = results.Count(r => r.ValidationResult != null && !r.ValidationResult.IsValid);

                Console.Error.WriteLine("--------------------------------------------------");
                Console.Error.WriteLine($"Process completed in {sw.Elapsed.TotalSeconds:F1}s");
                Console.Error.WriteLine($"Output at: {outputPath}");

                // Write structured JSON result to stdout for A360 to capture
                WriteResult("SUCCESS", "Processing completed successfully.", outputPath, filesProcessed, sw.Elapsed);
                return EXIT_SUCCESS;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.Error.WriteLine($"CRITICAL ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                WriteResult("ERROR", ex.Message, "", 0, sw.Elapsed);
                return EXIT_ERROR;
            }
        }

        /// <summary>
        /// Write a JSON result to stdout so A360 can parse it.
        /// All human-readable messages go to stderr to keep stdout clean.
        /// </summary>
        private static void WriteResult(string status, string message, string outputPath, int filesProcessed, TimeSpan elapsed)
        {
            var result = new
            {
                status,
                message,
                outputPath,
                filesProcessed,
                elapsedSeconds = Math.Round(elapsed.TotalSeconds, 2),
                timestamp = DateTime.UtcNow.ToString("o")
            };
            Console.WriteLine(JsonConvert.SerializeObject(result));
        }

        /// <summary>
        /// Parse named arguments in the format --key value or -k value.
        /// Also handles --flag (boolean flags without a value).
        /// </summary>
        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith("--") || arg.StartsWith("-"))
                {
                    string key = arg.TrimStart('-').ToLowerInvariant();

                    if (key == "h" || key == "help")
                    {
                        dict["help"] = "true";
                        continue;
                    }

                    // Check if next arg is a value (not another flag)
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        dict[key] = args[i + 1];
                        i++; // skip the value
                    }
                    else
                    {
                        dict[key] = "true"; // Boolean flag
                    }
                }
            }
            return dict;
        }

        /// <summary>
        /// Get a parameter by named keys or fallback to positional index.
        /// </summary>
        private static string GetParam(Dictionary<string, string> parsed, string[] keys, int positionalIndex, string[] args)
        {
            foreach (var key in keys)
            {
                if (parsed.TryGetValue(key, out var val))
                    return val;
            }

            // Positional fallback: find non-flag arguments
            var positionalArgs = args.Where(a => !a.StartsWith("-")).ToArray();

            // But we need to skip values that are part of --key value pairs
            var positionals = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("-"))
                {
                    // Skip the value after a flag (if it exists and isn't a flag)
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        i++;
                    continue;
                }
                positionals.Add(args[i]);
            }

            if (positionalIndex < positionals.Count)
                return positionals[positionalIndex];

            return string.Empty;
        }

        private static bool GetBoolParam(Dictionary<string, string> parsed, string[] keys, bool defaultValue)
        {
            foreach (var key in keys)
            {
                if (parsed.TryGetValue(key, out var val))
                {
                    if (bool.TryParse(val, out var b)) return b;
                    return true; // --parallel (without value) means true
                }
            }
            return defaultValue;
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine(@"
XALTA EOB TO EDI 835 PROCESSOR - A360 Runner
=============================================

USAGE:
  Runner.exe --input <path> --output <path> --config <path> --template <path> [--parallel true|false]

REQUIRED PARAMETERS:
  --input,    -i    Input folder path (CSV/Excel EOB files)
  --output,   -o    Output directory for generated EDI files
  --config,   -c    Path to configuration Excel (.xlsx)
  --template, -t    Path to EOB template Excel (.xlsx)

OPTIONAL PARAMETERS:
  --parallel, -p    Enable parallel processing (default: true)
  --upload,   -u    Enable Appsmith API upload (default: false)
  --help,     -h    Show this help message

EXIT CODES:
  0  Success
  1  Processing error
  2  Invalid arguments

EXAMPLES:
  Runner.exe --input ""C:\data\input"" --output ""C:\data\output"" --config ""C:\config\mapping.xlsx"" --template ""C:\config\template.xlsx""
  Runner.exe -i ""C:\data\input"" -o ""C:\data\output"" -c ""C:\config\mapping.xlsx"" -t ""C:\config\template.xlsx"" -p false
  Runner.exe -i ""C:\data\input"" -o ""C:\data\output"" -c ""C:\config\mapping.xlsx"" -t ""C:\config\template.xlsx"" --upload true

OUTPUT (stdout):
  JSON result: {""status"":""SUCCESS"",""message"":""..."",""outputPath"":""..."",""filesProcessed"":N,""elapsedSeconds"":0.0}
  A360 can capture stdout to read the JSON result.
");
        }
    }
}

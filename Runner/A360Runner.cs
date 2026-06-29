using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace Runner
{
    public class A360Runner
    {
        /// <summary>
        /// Entry point for Automation Anywhere A360 DLL Run Function action.
        /// Uses SQL Server for configuration instead of Excel config file.
        /// </summary>
        public static string Execute(
            string inputPath,
            string outputPath,
            string sqlServerName,
            string sqlDatabaseName,
            string templatePath,
            string parallel = "true",
            string uploadToApi = "false")
        {
            var sw = Stopwatch.StartNew();

            try
            {
                bool parallelBool = true;
                bool.TryParse(parallel, out parallelBool);

                bool uploadBool = false;
                bool.TryParse(uploadToApi, out uploadBool);

                // Validation
                var missing = new List<string>();

                if (string.IsNullOrWhiteSpace(inputPath))
                    missing.Add(nameof(inputPath));

                if (string.IsNullOrWhiteSpace(outputPath))
                    missing.Add(nameof(outputPath));

                if (string.IsNullOrWhiteSpace(sqlServerName))
                    missing.Add(nameof(sqlServerName));

                if (string.IsNullOrWhiteSpace(sqlDatabaseName))
                    missing.Add(nameof(sqlDatabaseName));

                if (string.IsNullOrWhiteSpace(templatePath))
                    missing.Add(nameof(templatePath));

                if (missing.Count > 0)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        status = "ERROR",
                        message = $"Missing parameters: {string.Join(", ", missing)}",
                        outputPath = "",
                        filesProcessed = 0,
                        elapsedSeconds = 0
                    });
                }

                // Debug information
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "A360_Debug.txt"),
                    $@"InputPath: {inputPath}
                        OutputPath: {outputPath}
                        SqlServerName: {sqlServerName}
                        SqlDatabaseName: {sqlDatabaseName}
                        TemplatePath: {templatePath}
                        CurrentDirectory: {Environment.CurrentDirectory}
                        BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}"
                );

                // Path validation
                if (!Directory.Exists(inputPath) && !File.Exists(inputPath))
                {
                    throw new Exception($"Input path not found: {inputPath}");
                }

                if (!File.Exists(templatePath))
                {
                    throw new Exception($"Template file not found: {templatePath}");
                }

                Directory.CreateDirectory(outputPath);

                AppDomain domain = null;
                try
                {
                    string dllDir = GetDllDirectory();
                    AppDomainSetup setup = new AppDomainSetup
                    {
                        ApplicationBase = dllDir,
                        PrivateBinPath = dllDir,
                        ConfigurationFile = Path.Combine(dllDir, "Edi.Generator835.dll.config")
                    };

                    string domainName = "EdiGeneratorDomain_" + Guid.NewGuid().ToString("N");
                    domain = AppDomain.CreateDomain(domainName, null, setup);

                    domain.SetData("inputPath", inputPath);
                    domain.SetData("outputPath", outputPath);
                    domain.SetData("sqlServerName", sqlServerName);
                    domain.SetData("sqlDatabaseName", sqlDatabaseName);
                    domain.SetData("templatePath", templatePath);
                    domain.SetData("parallel", parallel);
                    domain.SetData("uploadToApi", uploadToApi);

                    domain.DoCallBack(new CrossAppDomainDelegate(IsolatedExecuteCallbackSql));

                    string result = (string)domain.GetData("result");
                    return result;
                }
                finally
                {
                    if (domain != null)
                    {
                        try
                        {
                            AppDomain.Unload(domain);
                        }
                        catch
                        {
                            // Ignore
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sw.Stop();

                return JsonConvert.SerializeObject(new
                {
                    status = "ERROR",
                    message = ex.Message,
                    details = ex.ToString(),
                    outputPath = "",
                    filesProcessed = 0,
                    elapsedSeconds = Math.Round(sw.Elapsed.TotalSeconds, 2),
                    timestamp = DateTime.UtcNow.ToString("o")
                });
            }
        }

        /// <summary>
        /// Entry point for Automation Anywhere A360 DLL Run Function action.
        /// Uses Excel config file (original behavior).
        /// </summary>
        public static string ExecuteWithConfig(
            string inputPath,
            string outputPath,
            string configPath,
            string templatePath,
            string parallel = "true",
            string uploadToApi = "false")
        {
            var sw = Stopwatch.StartNew();

            try
            {
                bool parallelBool = true;
                bool.TryParse(parallel, out parallelBool);

                bool uploadBool = false;
                bool.TryParse(uploadToApi, out uploadBool);

                // Validation
                var missing = new List<string>();

                if (string.IsNullOrWhiteSpace(inputPath))
                    missing.Add(nameof(inputPath));

                if (string.IsNullOrWhiteSpace(outputPath))
                    missing.Add(nameof(outputPath));

                if (string.IsNullOrWhiteSpace(configPath))
                    missing.Add(nameof(configPath));

                if (string.IsNullOrWhiteSpace(templatePath))
                    missing.Add(nameof(templatePath));

                if (missing.Count > 0)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        status = "ERROR",
                        message = $"Missing parameters: {string.Join(", ", missing)}",
                        outputPath = "",
                        filesProcessed = 0,
                        elapsedSeconds = 0
                    });
                }

                // Debug information
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "A360_Debug.txt"),
                    $@"InputPath: {inputPath}
                        OutputPath: {outputPath}
                        ConfigPath: {configPath}
                        TemplatePath: {templatePath}
                        CurrentDirectory: {Environment.CurrentDirectory}
                        BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}"
                );

                // Path validation
                if (!Directory.Exists(inputPath) && !File.Exists(inputPath))
                {
                    throw new Exception($"Input path not found: {inputPath}");
                }

                if (!File.Exists(configPath))
                {
                    throw new Exception($"Config file not found: {configPath}");
                }

                if (!File.Exists(templatePath))
                {
                    throw new Exception($"Template file not found: {templatePath}");
                }

                Directory.CreateDirectory(outputPath);

                AppDomain domain = null;
                try
                {
                    string dllDir = GetDllDirectory();
                    AppDomainSetup setup = new AppDomainSetup
                    {
                        ApplicationBase = dllDir,
                        PrivateBinPath = dllDir,
                        ConfigurationFile = Path.Combine(dllDir, "Edi.Generator835.dll.config")
                    };

                    string domainName = "EdiGeneratorDomain_" + Guid.NewGuid().ToString("N");
                    domain = AppDomain.CreateDomain(domainName, null, setup);

                    domain.SetData("inputPath", inputPath);
                    domain.SetData("outputPath", outputPath);
                    domain.SetData("configPath", configPath);
                    domain.SetData("templatePath", templatePath);
                    domain.SetData("parallel", parallel);
                    domain.SetData("uploadToApi", uploadToApi);

                    domain.DoCallBack(new CrossAppDomainDelegate(IsolatedExecuteCallback));

                    string result = (string)domain.GetData("result");
                    return result;
                }
                finally
                {
                    if (domain != null)
                    {
                        try
                        {
                            AppDomain.Unload(domain);
                        }
                        catch
                        {
                            // Ignore
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sw.Stop();

                return JsonConvert.SerializeObject(new
                {
                    status = "ERROR",
                    message = ex.Message,
                    details = ex.ToString(),
                    outputPath = "",
                    filesProcessed = 0,
                    elapsedSeconds = Math.Round(sw.Elapsed.TotalSeconds, 2),
                    timestamp = DateTime.UtcNow.ToString("o")
                });
            }
        }
        private static string FormatDirectResult(string status, string message, string outputPath, int filesProcessed, TimeSpan elapsed)
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
            return JsonConvert.SerializeObject(result);
        }
        private static bool IsValidDllPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            try
            {
                string name = Path.GetFileNameWithoutExtension(path);
                return string.Equals(name, typeof(A360Runner).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string GetDllPath()
        {
            try
            {
                string loc = typeof(A360Runner).Assembly.Location;
                if (IsValidDllPath(loc) && File.Exists(loc))
                {
                    return loc;
                }
            }
            catch { }

            try
            {
                string cb = typeof(A360Runner).Assembly.CodeBase;
                if (!string.IsNullOrEmpty(cb))
                {
                    string localPath = new Uri(cb).LocalPath;
                    if (IsValidDllPath(localPath) && File.Exists(localPath))
                        return localPath;
                }
            }
            catch { }

            return null;
        }

        private static string GetDllDirectory()
        {
            try
            {
                string loc = typeof(A360Runner).Assembly.Location;
                if (IsValidDllPath(loc))
                {
                    string dir = Path.GetDirectoryName(loc);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        return dir;
                }
            }
            catch { }

            try
            {
                string cb = typeof(A360Runner).Assembly.CodeBase;
                if (!string.IsNullOrEmpty(cb))
                {
                    string localPath = new Uri(cb).LocalPath;
                    if (IsValidDllPath(localPath))
                    {
                        string dir = Path.GetDirectoryName(localPath);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            return dir;
                    }
                }
            }
            catch { }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private static void IsolatedExecuteCallback()
        {
            try
            {
                string inputPath = (string)AppDomain.CurrentDomain.GetData("inputPath");
                string outputPath = (string)AppDomain.CurrentDomain.GetData("outputPath");
                string configPath = (string)AppDomain.CurrentDomain.GetData("configPath");
                string templatePath = (string)AppDomain.CurrentDomain.GetData("templatePath");
                string parallel = (string)AppDomain.CurrentDomain.GetData("parallel");
                string uploadToApi = (string)AppDomain.CurrentDomain.GetData("uploadToApi");

                var runner = new IsolatedRunner();
                string result = runner.ExecuteInternal(inputPath, outputPath, configPath, templatePath, parallel, uploadToApi);

                AppDomain.CurrentDomain.SetData("result", result);
            }
            catch (Exception ex)
            {
                AppDomain.CurrentDomain.SetData("result", JsonConvert.SerializeObject(new
                {
                    status = "ERROR",
                    message = "Callback execution failed: " + ex.Message,
                    details = ex.ToString(),
                    outputPath = "",
                    filesProcessed = 0,
                    elapsedSeconds = 0.0,
                    timestamp = DateTime.UtcNow.ToString("o")
                }));
            }
        }

        private static void IsolatedExecuteCallbackSql()
        {
            try
            {
                string inputPath = (string)AppDomain.CurrentDomain.GetData("inputPath");
                string outputPath = (string)AppDomain.CurrentDomain.GetData("outputPath");
                string sqlServerName = (string)AppDomain.CurrentDomain.GetData("sqlServerName");
                string sqlDatabaseName = (string)AppDomain.CurrentDomain.GetData("sqlDatabaseName");
                string templatePath = (string)AppDomain.CurrentDomain.GetData("templatePath");
                string parallel = (string)AppDomain.CurrentDomain.GetData("parallel");
                string uploadToApi = (string)AppDomain.CurrentDomain.GetData("uploadToApi");

                var runner = new IsolatedRunner();
                string result = runner.ExecuteInternalSql(inputPath, outputPath, sqlServerName, sqlDatabaseName, templatePath, parallel, uploadToApi);

                AppDomain.CurrentDomain.SetData("result", result);
            }
            catch (Exception ex)
            {
                AppDomain.CurrentDomain.SetData("result", JsonConvert.SerializeObject(new
                {
                    status = "ERROR",
                    message = "Callback execution failed: " + ex.Message,
                    details = ex.ToString(),
                    outputPath = "",
                    filesProcessed = 0,
                    elapsedSeconds = 0.0,
                    timestamp = DateTime.UtcNow.ToString("o")
                }));
            }
        }
    }

#if NET48
    /// <summary>
    /// Runner execution class that inherits from MarshalByRefObject so it can be invoked across AppDomain boundaries.
    /// </summary>
    public class IsolatedRunner : MarshalByRefObject
    {
        public string ExecuteInternal(
            string inputPath,
            string outputPath,
            string configPath,
            string templatePath,
            string parallel,
            string uploadToApi)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                // Parse parameters
                bool parallelBool = true;
                if (!string.IsNullOrWhiteSpace(parallel))
                {
                    bool.TryParse(parallel, out parallelBool);
                }

                bool uploadBool = false;
                if (!string.IsNullOrWhiteSpace(uploadToApi))
                {
                    bool.TryParse(uploadToApi, out uploadBool);
                }

                // Validate parameters
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(inputPath)) missing.Add("inputPath");
                if (string.IsNullOrWhiteSpace(outputPath)) missing.Add("outputPath");
                if (string.IsNullOrWhiteSpace(configPath)) missing.Add("configPath");
                if (string.IsNullOrWhiteSpace(templatePath)) missing.Add("templatePath");

                if (missing.Count > 0)
                {
                    return FormatResult("ERROR", $"Missing required parameters: {string.Join(", ", missing)}", "", 0, sw.Elapsed);
                }

                // Validate paths exist
                if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
                {
                    return FormatResult("ERROR", $"Input path not found: {inputPath}", outputPath, 0, sw.Elapsed);
                }
                if (!File.Exists(configPath))
                {
                    return FormatResult("ERROR", $"Config file not found: {configPath}", outputPath, 0, sw.Elapsed);
                }
                if (!File.Exists(templatePath))
                {
                    return FormatResult("ERROR", $"Template file not found: {templatePath}", outputPath, 0, sw.Elapsed);
                }

                // Ensure output directory exists
                Directory.CreateDirectory(outputPath);

                // Initialize Logging (which will load Serilog 4.2.0 from local directory now)
                Edi.Generator835.Services.LoggingProvider.InitializeSafe(Path.Combine(outputPath, "logs"));

                // Run the pipeline
                var generator = new Edi.Generator835.EnterpriseGenerator(configPath, templatePath, outputPath);
                var results = generator.RunAsync(inputPath, parallelBool, uploadBool).GetAwaiter().GetResult();

                sw.Stop();

                int filesProcessed = results.Count(r => r.Success);

                return FormatResult("SUCCESS", "Processing completed successfully.", outputPath, filesProcessed, sw.Elapsed);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return FormatResult("ERROR", $"CRITICAL ERROR: {ex.Message}. StackTrace: {ex.StackTrace}", "", 0, sw.Elapsed);
            }
        }

        /// <summary>
        /// Execute using SQL Server for configuration instead of Excel file.
        /// </summary>
        public string ExecuteInternalSql(
            string inputPath,
            string outputPath,
            string sqlServerName,
            string sqlDatabaseName,
            string templatePath,
            string parallel,
            string uploadToApi)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                // Parse parameters
                bool parallelBool = true;
                if (!string.IsNullOrWhiteSpace(parallel))
                {
                    bool.TryParse(parallel, out parallelBool);
                }

                bool uploadBool = false;
                if (!string.IsNullOrWhiteSpace(uploadToApi))
                {
                    bool.TryParse(uploadToApi, out uploadBool);
                }

                // Validate parameters
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(inputPath)) missing.Add("inputPath");
                if (string.IsNullOrWhiteSpace(outputPath)) missing.Add("outputPath");
                if (string.IsNullOrWhiteSpace(sqlServerName)) missing.Add("sqlServerName");
                if (string.IsNullOrWhiteSpace(sqlDatabaseName)) missing.Add("sqlDatabaseName");
                if (string.IsNullOrWhiteSpace(templatePath)) missing.Add("templatePath");

                if (missing.Count > 0)
                {
                    return FormatResult("ERROR", $"Missing required parameters: {string.Join(", ", missing)}", "", 0, sw.Elapsed);
                }

                // Validate paths exist
                if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
                {
                    return FormatResult("ERROR", $"Input path not found: {inputPath}", outputPath, 0, sw.Elapsed);
                }
                if (!File.Exists(templatePath))
                {
                    return FormatResult("ERROR", $"Template file not found: {templatePath}", outputPath, 0, sw.Elapsed);
                }

                // Ensure output directory exists
                Directory.CreateDirectory(outputPath);

                // Initialize Logging
                Edi.Generator835.Services.LoggingProvider.InitializeSafe(Path.Combine(outputPath, "logs"));

                // Run the pipeline with SQL configuration
                var generator = new Edi.Generator835.EnterpriseGenerator(sqlServerName, sqlDatabaseName, templatePath, outputPath);
                var results = generator.RunAsync(inputPath, parallelBool, uploadBool).GetAwaiter().GetResult();

                sw.Stop();

                int filesProcessed = results.Count(r => r.Success);

                return FormatResult("SUCCESS", "Processing completed successfully.", outputPath, filesProcessed, sw.Elapsed);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return FormatResult("ERROR", $"CRITICAL ERROR: {ex.Message}. StackTrace: {ex.StackTrace}", "", 0, sw.Elapsed);
            }
        }

        private string FormatResult(string status, string message, string outputPath, int filesProcessed, TimeSpan elapsed)
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
            return JsonConvert.SerializeObject(result);
        }
    }
#endif
}

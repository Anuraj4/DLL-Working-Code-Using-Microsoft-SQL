using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using EdiFabric.Core.Model.Edi;
using EdiFabric.Framework.Readers;
using EdiFabric.Templates.Hipaa5010;
using Xalta.Edi.BalancingValidation.Interfaces;
using Xalta.Edi.BalancingValidation.Core;

namespace Xalta.Edi.BalancingValidation.Integration
{
    /// <summary>
    /// Simplified entry point for Automation Anywhere A360.
    /// Methods here are static and return JSON strings for easy parsing in the bot.
    /// </summary>
    public class A360Validator
    {
        private static string _licenseKey = "c417cb9dd9d54297a55c032a74c87996"; // Default trial key

        public static void SetLicense(string key)
        {
            _licenseKey = key;
        }

        /// <summary>
        /// Validates an EDI file and returns a JSON string of results.
        /// </summary>
        /// <param name="filePath">Absolute path to the .edi file</param>
        /// <param name="snipLevel">1, 2, or 3</param>
        /// <returns>JSON string containing { isValid: bool, errors: [] }</returns>
        public static string ValidateFile(string filePath, int snipLevel)
        {
            var diagInfo = new List<string>();
            try
            {
                if (!File.Exists(filePath))
                {
                    return CreateJsonError("File not found: " + filePath);
                }

                // Ensure license is set before reading
                EdiFabric.SerialKey.Set(_licenseKey, true);

                var transactions = new List<TS835>();
                int totalItemsRead = 0;
                var foundTypes = new List<string>();

                // Diagnostics
                try
                {
                    var tsType = typeof(TS835);
                    diagInfo.Add($"EdiFabric-Version: {typeof(EdiFabric.Framework.Readers.X12Reader).Assembly.FullName}");
                    diagInfo.Add($"Templates-Assembly: {tsType.Assembly.FullName}");
                    diagInfo.Add($"TS835-Type: {tsType.FullName}");

                    foreach (var attr in tsType.GetCustomAttributes(true))
                    {
                        if (attr.GetType().Name.Contains("Message"))
                        {
                            var pSystem = attr.GetType().GetProperty("System")?.GetValue(attr)?.ToString();
                            var pVersion = attr.GetType().GetProperty("Version")?.GetValue(attr)?.ToString();
                            var pTag = attr.GetType().GetProperty("Tag")?.GetValue(attr)?.ToString();
                            diagInfo.Add($"TS835-Attr: Message({pSystem}, {pVersion}, {pTag})");
                        }
                    }
                }
                catch (Exception dex) { diagInfo.Add("DiagError: " + dex.Message); }

                using (var stream = File.OpenRead(filePath))
                {
                    // Use simple string-based template path for A360
                    var reader = new X12Reader(stream, "EdiFabric.Templates.Hipaa");
                    while (reader.Read())
                    {
                        totalItemsRead++;
                        var ediItem = reader.Item;
                        if (ediItem != null)
                        {
                            string typeName = ediItem.GetType().Name;
                            if (!foundTypes.Contains(typeName)) foundTypes.Add(typeName);

                            if (ediItem is TS835 ts835)
                            {
                                transactions.Add(ts835);
                            }
                            else if (typeName.Contains("ReaderErrorContext"))
                            {
                                // Extract the reader error if possible
                                try
                                {
                                    var prop = ediItem.GetType().GetProperty("Message");
                                    if (prop != null)
                                    {
                                        string? msg = prop.GetValue(ediItem, null)?.ToString();
                                        if (!string.IsNullOrEmpty(msg)) foundTypes.Add("Error: " + msg);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }

                if (transactions.Count == 0)
                {
                    var fileInfo = new FileInfo(filePath);
                    string types = string.Join(", ", foundTypes);
                    string diags = string.Join(" | ", diagInfo);

                    var availableTemplates = new List<string>();
                    try
                    {
                        foreach (var t in typeof(TS835).Assembly.GetTypes())
                        {
                            if (t.Name.Contains("835"))
                            {
                                var mattr = t.GetCustomAttributes(true).FirstOrDefault(a => a.ToString()?.Contains("Message") == true);
                                if (mattr != null)
                                {
                                    var pSystem = mattr.GetType().GetProperty("System")?.GetValue(mattr)?.ToString();
                                    var pVersion = mattr.GetType().GetProperty("Version")?.GetValue(mattr)?.ToString();
                                    var pTag = mattr.GetType().GetProperty("Tag")?.GetValue(mattr)?.ToString();
                                    availableTemplates.Add($"{t.FullName}[{pSystem}, {pVersion}, {pTag}]");
                                }
                            }
                        }
                    }
                    catch { }

                    return CreateJsonError($"Failed to parse EDI. Items: {totalItemsRead}. Types: {types}. Diags: {diags}. Available: {string.Join(", ", availableTemplates)}");
                }

                // 2. Build Validator
                IBalancingValidator<TS835> validator;
                switch (snipLevel)
                {
                    case 1: validator = EdiValidatorFactory.CreateSnip1(); break;
                    case 2: validator = EdiValidatorFactory.CreateSnip2(); break;
                    default: validator = EdiValidatorFactory.CreateSnip3(); break;
                }

                // 3. Validate each transaction and aggregate
                bool overallValid = true;
                var allErrors = new List<EdiSegmentError>();

                foreach (var transaction in transactions)
                {
                    var result = validator.Validate(transaction);
                    if (!result.IsValid) overallValid = false;
                    allErrors.AddRange(result.Errors);
                }

                // 4. Return as JSON
                return JsonConvert.SerializeObject(new
                {
                    isValid = overallValid,
                    snipLevelUsed = snipLevel,
                    transactionCount = transactions.Count,
                    fileName = Path.GetFileName(filePath),
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    diag = diagInfo,
                    errors = allErrors.SelectMany(seg =>
                        seg.ElementErrors.Select(elem => new
                        {
                            code = elem.ErrorType,
                            message = elem.Message,
                            segment = seg.SegmentName,
                            position = seg.SegmentPosition,
                            reference = elem.FieldKey,
                            context = $"{elem.BusinessName} = {elem.Value ?? "missing"}"
                        })
                    ).ToList()
                }, Formatting.Indented);
            }
            catch (Exception ex)
            {
                var diagStr = string.Join(" | ", diagInfo);
                return CreateJsonError("Internal Error: " + ex.Message + "\nDiagnostics: " + diagStr + "\nStack: " + ex.StackTrace);
            }
        }

        private static string CreateJsonError(string message)
        {
            return JsonConvert.SerializeObject(new
            {
                isValid = false,
                errors = new[] { new { code = "SYSTEM_ERR", message = message } }
            }, Formatting.Indented);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using ExcelDataReader;
using Serilog;

namespace Edi.Generator835.Configuration
{
    /// <summary>
    /// Loads configuration from a single Excel workbook using streaming (ExcelDataReader).
    /// Sheet names map to table names (lowercased).
    /// Optimized for memory: reads rows as a stream.
    /// </summary>
    public class ExcelMappingProvider : IMappingProvider
    {
        private static readonly HashSet<string> BlacklistedSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "column_name_to_amt01", "codes_description", "Metadata", "Index", "Master"
        };

        public MappingConfiguration LoadMappings(string configPath)
        {
            if (!File.Exists(configPath))
                throw new FileNotFoundException($"Config Excel file not found: {configPath}");

            var config = new MappingConfiguration();
            var store = new ConfigStore();

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            using (var stream = File.Open(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                // Process all sheets in the workbook
                do
                {
                    string sheetName = reader.Name;
                    string normalizedName = sheetName.ToLowerInvariant().Replace(" ", "_");

                    if (BlacklistedSheets.Contains(normalizedName)) continue;

                    var records = ReadSheetStream(reader);

                    if (records.Count > 0)
                    {
                        var configSheet = new ConfigSheet(sheetName, records);
                        store.AddSheet(configSheet);

                        // Route to existing MappingConfiguration buckets for backward compatibility
                        MappingLoader.LoadTable(config, sheetName, records);
                    }

                    Log.Information("Loaded config sheet: {SheetName} with {Count} records", sheetName, records.Count);

                } while (reader.NextResult());
            }

            // Optional: Store the new ConfigStore in MappingConfiguration if you add a property for it
            // config.Store = store; 

            return config;
        }

        private List<Dictionary<string, string>> ReadSheetStream(IExcelDataReader reader)
        {
            var records = new List<Dictionary<string, string>>();
            var headers = new List<string>();

            // Read headers from the first row
            if (reader.Read())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var header = reader.GetValue(i)?.ToString()?.Trim() ?? $"Column{i}";
                    headers.Add(header);
                }
            }

            if (headers.Count == 0 || headers.All(string.IsNullOrWhiteSpace)) return records;

            // Read data rows
            while (reader.Read())
            {
                var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool hasData = false;

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    if (i < headers.Count)
                    {
                        var value = reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;
                        record[headers[i]] = value;
                        if (!string.IsNullOrWhiteSpace(value))
                            hasData = true;
                    }
                }

                // Optimization: stop if we hit an empty row
                if (!hasData) break;

                records.Add(record);
            }

            return records;
        }
    }
}

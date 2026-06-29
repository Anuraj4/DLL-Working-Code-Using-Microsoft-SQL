using System;
using System.Collections.Generic;
using System.Linq;

namespace Edi.Generator835.Configuration
{
    /// <summary>
    /// Shared logic for routing parsed records into the correct MappingConfiguration buckets.
    /// Used by both CsvMappingProvider and ExcelMappingProvider.
    /// </summary>
    public static class MappingLoader
    {
        /// <summary>
        /// Route a table's records into the correct bucket based on its name.
        /// Always stores raw records to preserve order and full column access.
        /// </summary>
        public static void LoadTable(MappingConfiguration config, string tableName, List<Dictionary<string, string>> records)
        {
            if (records == null || records.Count == 0) return;

            string normalizedName = tableName.ToLowerInvariant().Replace(" ", "_");

            // Add to new ConfigStore
            var sheet = new ConfigSheet(normalizedName, records);
            config.Store.AddSheet(sheet);

            if (normalizedName == "common_settings")
            {
                LoadCommonSettings(config, records);
            }
            else if (normalizedName == "edi_settings" || normalizedName == "default_payment_settings")
            {
                LoadScopedSettings(config, records);
            }
            else if (normalizedName == "payer_registry")
            {
                LoadPayerRegistry(config, records);
            }
            else if (normalizedName == "835_default_code")
            {
                LoadFixedDefaults(config, records);
            }
            else if (normalizedName == "fallback_codes")
            {
                LoadScopedDefaults(config, records);
            }
            else if (normalizedName == "general")
            {
                LoadSheetMetadata(config, records);
            }
            else
            {
                LoadMappingTable(config, normalizedName, records);
            }

            // Always store the raw records and normalize common keys for downstream consumers
            foreach (var record in records)
            {
                if (record.TryGetValue("Payer ID", out var pId) && !record.ContainsKey("PayerID")) record["PayerID"] = pId;
            }
            config.RawMappingTables[normalizedName] = records;
        }

        /// <summary>
        /// Load metadata from the 'general' sheet (Sheet Name, Description, Technical / Business).
        /// </summary>
        public static void LoadSheetMetadata(MappingConfiguration config, List<Dictionary<string, string>> records)
        {
            foreach (var record in records)
            {
                if (record.TryGetValue("Sheet Name", out var name) && !string.IsNullOrWhiteSpace(name))
                {
                    // Always store specific rows for sheet indexing in RawMappingTables
                }
            }
        }

        /// <summary>
        /// Load global common settings from the 'common_settings' sheet.
        /// </summary>
        private static void LoadCommonSettings(MappingConfiguration config, List<Dictionary<string, string>> records)
        {
            foreach (var record in records)
            {
                if (record.TryGetValue("SettingName", out var name) &&
                    record.TryGetValue("Value", out var value) &&
                    !string.IsNullOrWhiteSpace(name))
                {
                    config.CommonSettings[name.Trim()] = value.Trim();
                }
            }
        }

        /// <summary>
        /// Load the payer registry metadata.
        /// </summary>
        private static void LoadPayerRegistry(MappingConfiguration config, List<Dictionary<string, string>> records)
        {
            config.PayerRegistry = records;
        }

        /// <summary>
        /// Load EDI settings which might be scoped by PayerID.
        /// </summary>
        private static void LoadScopedSettings(MappingConfiguration config, List<Dictionary<string, string>> records)
        {
            if (records.Count == 0) return;
            var originalHeaders = records[0].Keys.ToList();
            bool isMultiColumn = !originalHeaders.Any(h => h.Equals("SettingName", StringComparison.OrdinalIgnoreCase));

            foreach (var record in records)
            {
                if (isMultiColumn)
                {
                    // For multi-column sheets, sanitize headers (e.g., 'BPR01_Name' -> 'BPR01') and pad values
                    foreach (var header in originalHeaders)
                    {
                        if (record.TryGetValue(header, out var val))
                        {
                            string key = header;
                            // Clean verbose segment columns like BPR01_Transaction_Handling_Code
                            if (header.Contains("_") && !header.Equals("Payer ID") && !header.Equals("PayerID"))
                            {
                                if (header.StartsWith("BPR") || header.StartsWith("TRN") || header.StartsWith("CUR"))
                                {
                                    key = header.Split('_')[0];
                                }
                            }

                            string finalVal = PadIfRequired(key, val);
                            if (key != header)
                            {
                                record.Remove(header); // Remove verbose descriptive key
                                record[key] = finalVal; // Add compact key (e.g., "BPR01")
                            }
                            else
                            {
                                record[header] = finalVal; // Just pad the existing key
                            }
                        }
                    }
                }
                else if (record.TryGetValue("SettingName", out var name) && !string.IsNullOrWhiteSpace(name))
                {
                    // Existing key-value logic (with padding)
                    if (record.TryGetValue("Value", out var val))
                    {
                        record["Value"] = PadIfRequired(name, val);
                    }

                    if (!config.ScopedSettings.TryGetValue(name, out var list))
                    {
                        list = new List<Dictionary<string, string>>();
                        config.ScopedSettings[name] = list;
                    }
                    list.Add(record);
                }
            }
        }

        /// <summary>
        /// EDI qualifier fields and their expected minimum widths (for zero-padding).
        /// Excel strips leading zeros from numeric cells — this auto-corrects them.
        /// Add new fields here as needed.
        /// </summary>
        private static readonly Dictionary<string, int> EdiFieldMinWidths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // BPR segment qualifiers (2-digit codes)
            { "BPR06", 2 },  // DFI ID Number Qualifier (Originating)
            { "BPR12", 2 },  // DFI ID Number Qualifier (Receiving)
 
            // CLP segment qualifiers
            { "CLP08", 2 },  // Facility Code Qualifier

            // ISA/GS Qualifiers from edi_settings
            { "ISA_SenderIDQualifier", 2 },
            { "ISA_ReceiverIDQualifier", 2 },
            { "ISA_AuthorizationInformationQualifier", 2 },
            { "ISA_SecurityInformationQualifier", 2 },
            { "ISA_AcknowledgmentRequested", 1 },
            { "ISA_UsageIndicator", 1 },
            { "ISA_InterchangeControlVersionNumber", 5 },
            { "ISA01", 2 },
            { "ISA03", 2 },
            { "ISA05", 2 },
            { "ISA07", 2 }
        };

        /// <summary>
        /// Load default values (possibly payer-specific) from records.
        /// Handles both key-value and multi-column formats.
        /// </summary>
        private static void LoadScopedDefaults(MappingConfiguration config, List<Dictionary<string, string>> records)
        {
            if (records.Count == 0) return;

            var headers = records[0].Keys.ToList();

            // Check if this is a "Key-Value" sheet or a "Multi-Column" sheet
            bool isKeyValue = headers.Any(h => h.Equals("FieldName", StringComparison.OrdinalIgnoreCase) ||
                                               h.Equals("Code Name", StringComparison.OrdinalIgnoreCase) ||
                                               h.Equals("Field Name", StringComparison.OrdinalIgnoreCase));

            if (isKeyValue)
            {
                string nameCol = headers.FirstOrDefault(h => h.Equals("FieldName", StringComparison.OrdinalIgnoreCase) ||
                                                           h.Equals("Code Name", StringComparison.OrdinalIgnoreCase) ||
                                                           h.Equals("Field Name", StringComparison.OrdinalIgnoreCase) ||
                                                           h.Equals("Segment Element", StringComparison.OrdinalIgnoreCase)) ?? headers[0];

                foreach (var record in records)
                {
                    if (record.TryGetValue(nameCol, out var name) && !string.IsNullOrWhiteSpace(name))
                    {
                        // Clean values and pad if required
                        string valCol = record.ContainsKey("Fallback Code") ? "Fallback Code" :
                                       record.ContainsKey("DefaultValue") ? "DefaultValue" :
                                       record.ContainsKey("Value") ? "Value" : string.Empty;

                        if (!string.IsNullOrEmpty(valCol))
                        {
                            record[valCol] = PadIfRequired(name, record[valCol]);
                            // Ensure standard keys exist for consumer convenience
                            if (valCol != "DefaultValue" && !record.ContainsKey("DefaultValue"))
                                record["DefaultValue"] = record[valCol];
                        }

                        // Ensure PayerID is also available if it was "Payer ID"
                        if (record.TryGetValue("Payer ID", out var pId) && !record.ContainsKey("PayerID"))
                            record["PayerID"] = pId;

                        if (!config.ScopedDefaults.TryGetValue(name, out var list))
                        {
                            list = new List<Dictionary<string, string>>();
                            config.ScopedDefaults[name] = list;
                        }
                        list.Add(record);
                    }
                }
            }
            else
            {
                // Multi-column logic removed as it's now handled by LoadScopedSettings for default_payment_settings and edi_settings
                // But keep in case there are other generic multi-column sheets that need to populate ScopedDefaults
                foreach (var record in records)
                {
                    foreach (var header in headers)
                    {
                        if (header.Equals("Payer ID", StringComparison.OrdinalIgnoreCase) ||
                            header.Equals("PayerID", StringComparison.OrdinalIgnoreCase)) continue;

                        if (record.TryGetValue(header, out var value) && !string.IsNullOrWhiteSpace(value))
                        {
                            string key = header;
                            if (header.Contains("_"))
                            {
                                key = header.Split('_')[0];
                            }

                            if (!config.ScopedDefaults.TryGetValue(key, out var list))
                            {
                                list = new List<Dictionary<string, string>>();
                                config.ScopedDefaults[key] = list;
                            }

                            var syntheticRecord = new Dictionary<string, string>(record, StringComparer.OrdinalIgnoreCase);
                            syntheticRecord["FieldName"] = key;
                            syntheticRecord["DefaultValue"] = PadIfRequired(key, value);
                            syntheticRecord.Remove("Value");

                            list.Add(syntheticRecord);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Load fixed, non-payer-specific defaults from '835_default_code'.
        /// Uses 'Code Name' or 'Field Name' as the key.
        /// </summary>
        private static void LoadFixedDefaults(MappingConfiguration config, List<Dictionary<string, string>> records)
        {
            if (records == null || records.Count == 0) return;

            var headers = records[0].Keys.ToList();
            string nameCol = headers.FirstOrDefault(h => h.Equals("Code Name", StringComparison.OrdinalIgnoreCase) ||
                                                       h.Equals("Field Name", StringComparison.OrdinalIgnoreCase) ||
                                                       h.Equals("Segment Element", StringComparison.OrdinalIgnoreCase) ||
                                                       h.Equals("FieldName", StringComparison.OrdinalIgnoreCase)) ?? headers[0];

            string valCol = headers.FirstOrDefault(h => h.Equals("DefaultValue", StringComparison.OrdinalIgnoreCase) ||
                                                      h.Equals("Value", StringComparison.OrdinalIgnoreCase) ||
                                                      h.Equals("Default Value", StringComparison.OrdinalIgnoreCase)) ?? (headers.Count > 1 ? headers[1] : string.Empty);

            foreach (var record in records)
            {
                if (record.TryGetValue(nameCol, out var name) && !string.IsNullOrWhiteSpace(name))
                {
                    string finalValue = string.Empty;
                    if (!string.IsNullOrEmpty(valCol) && record.TryGetValue(valCol, out var val))
                    {
                        finalValue = val;
                    }

                    config.FixedDefaults[name.Trim()] = PadIfRequired(name, finalValue);
                }
            }
        }

        /// <summary>
        /// Pad a value with leading zeros if the field is a known EDI qualifier
        /// that requires a minimum width.
        /// </summary>
        private static string PadIfRequired(string fieldName, string value)
        {
            if (EdiFieldMinWidths.TryGetValue(fieldName, out int minWidth) &&
                !string.IsNullOrWhiteSpace(value) &&
                int.TryParse(value, out _))
            {
                return value.PadLeft(minWidth, '0');
            }
            return value;
        }

        /// <summary>
        /// Load a generic mapping table where first column = key, second column = value.
        /// This provides O(1) lookup for simple key-value mappings.
        /// For multi-column sheets, consumers should use RawMappingTables instead.
        /// </summary>
        public static void LoadMappingTable(MappingConfiguration config, string tableName, List<Dictionary<string, string>> records)
        {
            if (records.Count == 0) return;

            var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var headers = records[0].Keys.ToList();

            if (headers.Count < 2) return;

            string sourceCol = headers[0];
            string targetCol = headers[1];

            foreach (var record in records)
            {
                var sourceKey = record.ContainsKey(sourceCol) ? record[sourceCol] : string.Empty;
                var targetValue = record.ContainsKey(targetCol) ? record[targetCol] : string.Empty;

                if (!string.IsNullOrWhiteSpace(sourceKey) && !table.ContainsKey(sourceKey))
                {
                    table[sourceKey] = targetValue;
                }
            }

            config.MappingTables[tableName] = table;
        }
    }
}

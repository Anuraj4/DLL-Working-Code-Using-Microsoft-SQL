using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace Edi.Generator835.Configuration
{
    /// <summary>
    /// Holds all loaded mappings and settings from the Excel configuration.
    /// Provides O(1) lookup for code resolution.
    /// </summary>
    public class MappingConfiguration
    {
        /// <summary>
        /// New unified configuration storage.
        /// </summary>
        public ConfigStore Store { get; set; } = new ConfigStore();

        /// <summary>
        /// Global common settings (e.g., DateFormat, AppSmithUrl).
        /// Key = SettingName, Value = setting value string.
        /// </summary>
        public Dictionary<string, string> CommonSettings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// EDI settings (ISA/GS qualifiers) that can be payer-specific.
        /// Refactored to use Store.
        /// </summary>
        public Dictionary<string, List<Dictionary<string, string>>> ScopedSettings
        {
            get => GetLegacyRawTable("edi_settings");
            set
            {
                RawMappingTables["edi_settings"] = value.SelectMany(kvp => kvp.Value).ToList();
                SyncLegacyToStore("edi_settings");
            }
        }

        /// <summary>
        /// The complete list of payers from the 'payer_registry' sheet.
        /// </summary>
        public List<Dictionary<string, string>> PayerRegistry
        {
            get => EnsureSheet("payer_registry")?.AllRecords
                .Select(r => r is Dictionary<string, string> d ? d : r.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase))
                .ToList() ?? new List<Dictionary<string, string>>();
            set
            {
                RawMappingTables["payer_registry"] = value;
                SyncLegacyToStore("payer_registry");
            }
        }

        /// <summary>
        /// The currently matched payer from the registry.
        /// </summary>
        public Dictionary<string, string>? MatchedPayer { get; private set; }

        /// <summary>
        /// Generic mapping tables loaded from Excel sheets.
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> MappingTables { get; set; }
            = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Raw mapping records preserving the order from the Excel file.
        /// </summary>
        public Dictionary<string, List<Dictionary<string, string>>> RawMappingTables { get; set; }
            = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Default values for EDI fields.
        /// </summary>
        public Dictionary<string, List<Dictionary<string, string>>> ScopedDefaults { get; set; }
            = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Fixed default values for EDI fields from the '835_default_code' sheet.
        /// </summary>
        public Dictionary<string, string> FixedDefaults { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);


        private Dictionary<string, List<Dictionary<string, string>>> GetLegacyRawTable(string sheetName)
        {
            var sheet = Store.GetSheet(sheetName);
            if (sheet == null) return new Dictionary<string, List<Dictionary<string, string>>>();

            // This is a bit of a hack for backward compatibility if needed, 
            // but ideally we should move away from this specific structure.
            return new Dictionary<string, List<Dictionary<string, string>>>
            {
                {
                    sheetName,
                    sheet.AllRecords.Select(r => r.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)).ToList()
                }
            };
        }

        /// <summary>
        /// Identifies the best-matching payer from the registry based on word matching.
        /// </summary>
        public void MatchPayer(string payerName)
        {
            var registrySheet = EnsureSheet("payer_registry");
            var payers = registrySheet?.AllRecords ?? new List<IReadOnlyDictionary<string, string>>();

            if (string.IsNullOrWhiteSpace(payerName) || payers.Count == 0)
            {
                var fallback = payers.FirstOrDefault(r =>
                {
                    r.TryGetValue("Payer ID", out var id1);
                    r.TryGetValue("PayerID", out var id2);
                    var id = !string.IsNullOrEmpty(id1) ? id1 : id2;
                    return id?.Equals("Fallback", StringComparison.OrdinalIgnoreCase) == true;
                });
                MatchedPayer = fallback != null ? fallback.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase) : null;
                return;
            }

            string inputNormalized = NormalizeForMatch(payerName);
            var inputWords = new HashSet<string>(inputNormalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

            Dictionary<string, string>? bestMatch = null;
            double maxScore = 0;

            foreach (var payerRow in PayerRegistry)
            {
                string regName = GetPayerName(payerRow);
                if (string.IsNullOrWhiteSpace(regName) || regName.Equals("Fallback", StringComparison.OrdinalIgnoreCase)) continue;

                string regNormalized = NormalizeForMatch(regName);
                var regWords = regNormalized.Split(' ');

                double score = 0;

                // 1. Exact Normalized Match (Highest Priority)
                if (inputNormalized == regNormalized)
                {
                    score = 100;
                }
                else
                {
                    // 2. Word overlapping
                    int matchCount = regWords.Count(w => inputWords.Contains(w));
                    score += matchCount * 10;

                    // 3. Substring Containment (covers UnitedHealthcare vs United Health Care)
                    string regNoSpace = regNormalized.Replace(" ", "");
                    string inputNoSpace = inputNormalized.Replace(" ", "");

                    if (inputNoSpace.Contains(regNoSpace) || regNoSpace.Contains(inputNoSpace))
                    {
                        score += 20;
                    }

                    // 4. Bonus for starting with the same name
                    if (inputNormalized.StartsWith(regWords[0]) || regNormalized.StartsWith(inputWords.First()))
                    {
                        score += 5;
                    }
                }

                if (score > maxScore)
                {
                    maxScore = score;
                    bestMatch = payerRow.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
                }
            }

            // Apply a minimum threshold (at least some match)
            if (maxScore > 0)
            {
                MatchedPayer = bestMatch;
            }
            else
            {
                var fallback = payers.FirstOrDefault(r =>
                {
                    r.TryGetValue("Payer ID", out var id1);
                    r.TryGetValue("PayerID", out var id2);
                    var id = !string.IsNullOrEmpty(id1) ? id1 : id2;
                    return id?.Equals("Fallback", StringComparison.OrdinalIgnoreCase) == true;
                });
                MatchedPayer = fallback != null ? fallback.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase) : null;
            }

            if (MatchedPayer != null)
            {
                string matchedName = GetPayerName(MatchedPayer);
                string matchedId = GetPayerId(MatchedPayer);
                Log.Information("[PayerMatch] Input: '{InputName}' -> Matched: '{MatchedName}' (ID: {MatchedId}) with score {Score}",
                    payerName, matchedName, matchedId, maxScore);
            }
            else
            {
                Log.Warning("[PayerMatch] No match found for '{InputName}'. No fallback record available in registry.", payerName);
            }
        }

        private string NormalizeForMatch(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            // Lowercase and strip technical suffixes like "(UHC)" or ", Inc."
            string cleaned = text.ToLowerInvariant();
            cleaned = cleaned.Replace("(uhc)", "").Replace(", inc.", "").Replace("inc", "").Replace("corp", "");

            // Replace punctuation with spaces to ensure word separation
            char[] arr = cleaned.Select(c => (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)) ? c : ' ').ToArray();
            return new string(arr).Trim().Replace("  ", " ").Replace("  ", " ");
        }

        public string GetPayerName(Dictionary<string, string> row)
        {
            if (row.TryGetValue("Payer Name", out var n1)) return n1;
            if (row.TryGetValue("PayerName", out var n2)) return n2;
            return string.Empty;
        }

        /// <summary>
        /// Lookup a value in a generic mapping table. Returns null if not found.
        /// </summary>
        public string? LookupMapping(string tableName, string sourceValue)
        {
            var sheet = EnsureSheet(tableName);
            if (sheet != null) return sheet.GetValue(sourceValue);

            return MappingTables.TryGetValue(tableName, out var table) && table.TryGetValue(sourceValue, out var val) ? val : null;
        }




        /// <summary>
        /// Robust lookup for Adjustment Group (CAGC) and Reason (CARC) codes.
        /// Implements prioritized search: Payer-Specific (Exact ID + EOB Type) -> Global -> Secondary Fallback.
        /// </summary>
        public (string? CAGC, string? CARC) LookupAdjustment(string payerId, string eobType, string groupCode, string reasonCode)
        {
            const string TableName = "adjustment_group_mapping";
            var sheet = EnsureSheet(TableName);
            if (sheet == null) return (null, null);

            var payerIdStr = (payerId ?? "").Trim();
            var eobTypeStr = (eobType ?? "").Trim();

            // 1. Filter for PayerID and EOB Type matches (including fallbacks)
            var records = sheet.AllRecords;
            var candidates = records.Where(r =>
                (GetPayerId(r).Equals(payerIdStr, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(GetPayerId(r))) &&
                (!r.ContainsKey("EOB Type") || string.IsNullOrWhiteSpace(r["EOB Type"]) || r["EOB Type"].Equals(eobTypeStr, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            // 2. Search Strategy
            var result = SearchInSheetRecords(candidates, groupCode, reasonCode);

            // 3. Fallback to global if needed
            if (result.CAGC == null && result.CARC == null)
            {
                result = SearchInSheetRecords(records.ToList(), groupCode, reasonCode);
            }

            return result;
        }

        private (string? CAGC, string? CARC) SearchInSheetRecords(List<IReadOnlyDictionary<string, string>> records, string group, string reason)
        {
            if (records == null || records.Count == 0) return (null, null);

            group = (group ?? "").Trim();
            reason = (reason ?? "").Trim();
            string combined = (group + reason).Replace(" ", "").Replace("-", "");

            foreach (var r in records)
            {
                if (!r.TryGetValue("AdjustmentText", out var text) || string.IsNullOrEmpty(text)) continue;
                text = text.Trim();

                bool match = false;
                if (string.IsNullOrEmpty(reason) && text.Equals(group, StringComparison.OrdinalIgnoreCase)) match = true;
                else if (string.IsNullOrEmpty(group) && text.Equals(reason, StringComparison.OrdinalIgnoreCase)) match = true;
                else if (!string.IsNullOrEmpty(group) && !string.IsNullOrEmpty(reason))
                {
                    if (text.Equals(group, StringComparison.OrdinalIgnoreCase) || text.Equals(reason, StringComparison.OrdinalIgnoreCase)) match = true;
                    else if (text.Replace(" ", "").Replace("-", "").Equals(combined, StringComparison.OrdinalIgnoreCase)) match = true;
                }

                if (match)
                {
                    r.TryGetValue("CAGC", out var cagc);
                    r.TryGetValue("CARC", out var carc);
                    return (string.IsNullOrWhiteSpace(cagc) ? null : cagc, string.IsNullOrWhiteSpace(carc) ? null : carc);
                }
            }

            return (null, null);
        }

        /// <summary>
        /// Retrieves the default CAGC (GroupCode) for a given CARC (ReasonCode) from the 'carc_cagc_mapping' sheet.
        /// </summary>
        public string? LookupCagcByCarc(string carc)
        {
            if (string.IsNullOrWhiteSpace(carc)) return null;
            var sheet = EnsureSheet("carc_cagc_mapping");
            return sheet?.GetValue("ReasonCode", carc.Trim(), "GroupCode") ??
                   (MappingTables.TryGetValue("carc_cagc_mapping", out var table) && table.TryGetValue(carc, out var cagc) ? cagc : null);
        }

        private string CleanCagc(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            input = input!.Trim().ToUpperInvariant();

            // EDI 835 Group Codes are exactly 2 characters: CO, OA, PI, PR
            if (input == "CO" || input == "OA" || input == "PI" || input == "PR")
            {
                return input;
            }

            // Handle combined prefix cases (e.g. "CO45") by taking first 2 chars
            if (input.Length >= 2)
            {
                string firstTwo = input.Substring(0, 2);
                if (firstTwo == "CO" || firstTwo == "OA" || firstTwo == "PI" || firstTwo == "PR")
                {
                    return firstTwo;
                }
            }

            // If it's something like "NA", "None", or garbage, reject it so ResolveCagc falls back to lookup
            Log.Debug("[ResolveCagc] Rejecting invalid Group Code: '{Input}'", input);
            return string.Empty;
        }

        /// <summary>
        /// Resolves the correct CAGC (Group Code) for a given CARC (Reason Code) using the multi-tier hierarchy:
        /// 1. adjustment_group_mapping (Payer-specific or Global)
        /// 2. carc_cagc_mapping (Global template fallback)
        /// </summary>
        public string ResolveCagc(string payerId, string eobType, string carc, string? originalCagc = null)
        {
            if (string.IsNullOrWhiteSpace(carc)) return CleanCagc(originalCagc);

            // Tier 0: Absolute Priority - Trust the explicit Group Code from the input token (e.g. PR in PR243)
            // to ensure source liability is always preserved.
            string cleanedOriginal = CleanCagc(originalCagc);
            if (!string.IsNullOrEmpty(cleanedOriginal)) return cleanedOriginal;

            // Tier 1: adjustment_group_mapping (Payer/EOB specific)
            var (mappedCagc, _) = LookupAdjustment(payerId, eobType, string.Empty, carc);
            if (!string.IsNullOrEmpty(mappedCagc)) return CleanCagc(mappedCagc);

            // Tier 3: carc_cagc_mapping (Global template fallback)
            var templateCagc = LookupCagcByCarc(carc);
            if (!string.IsNullOrEmpty(templateCagc)) return CleanCagc(templateCagc);

            return string.Empty;
        }

        /// <summary>
        /// Get an EDI setting value. Returns defaultValue if not found.
        /// Prioritizes Payer-specific settings if available.
        /// </summary>
        public string GetSetting(string settingName, string defaultValue = "")
        {
            // 1. Check Payer-Specific Scoped Settings from 'edi_settings' table
            string val = GetScopedSetting("edi_settings", settingName);
            if (!string.IsNullOrEmpty(val)) return val;

            // 2. Check Global Common Settings
            if (CommonSettings.TryGetValue(settingName, out var globalVal))
                return globalVal;

            return defaultValue;
        }

        /// <summary>
        /// Retrieves a setting from the 'default_payment_settings' table using hierarchical lookup.
        /// </summary>
        public string GetPaymentSetting(string settingName, string defaultValue = "")
        {
            return GetScopedSetting("default_payment_settings", settingName, defaultValue);
        }

        /// <summary>
        /// Retrieves a setting from a specific table using hierarchical lookup (Matched Payer -> Fallback).
        /// </summary>
        public string GetScopedSetting(string tableName, string settingName, string defaultValue = "")
        {
            if (!RawMappingTables.TryGetValue(tableName, out var table)) return defaultValue;

            // Priority 1: Match currently active payer
            if (MatchedPayer != null)
            {
                string payerId = GetPayerId(MatchedPayer);
                if (!string.IsNullOrEmpty(payerId))
                {
                    var match = table.FirstOrDefault(r => GetPayerId(r).Equals(payerId, StringComparison.OrdinalIgnoreCase));
                    if (match != null && match.TryGetValue(settingName, out var val) && !string.IsNullOrEmpty(val))
                    {
                        return val;
                    }
                }
            }

            // Priority 2: Fallback row
            var fallbackMatch = table.FirstOrDefault(r => GetPayerId(r).Equals("Fallback", StringComparison.OrdinalIgnoreCase));
            if (fallbackMatch != null && fallbackMatch.TryGetValue(settingName, out var fval) && !string.IsNullOrEmpty(fval))
            {
                return fval;
            }

            return defaultValue;
        }

        public string GetPayerId(IReadOnlyDictionary<string, string> row)
        {
            if (row.TryGetValue("Payer ID", out var id1)) return id1;
            if (row.TryGetValue("PayerID", out var id2)) return id2;
            return string.Empty;
        }


        public string GetPayerTin(IReadOnlyDictionary<string, string> row)
        {
            if (row.TryGetValue("Payer EIN/TIN", out var t1)) return t1;
            if (row.TryGetValue("TIN", out var t2)) return t2;
            if (row.TryGetValue("EIN", out var t3)) return t3;
            return string.Empty;
        }

        /// <summary>
        /// Get a default value for a field. Returns fallback if not found.
        /// Prioritizes Payer-specific defaults if available.
        /// </summary>
        public string GetDefault(string fieldName, string fallback = "")
        {
            if (ScopedDefaults.TryGetValue(fieldName, out var records))
            {
                // Priority 1: Match currently active payer
                if (MatchedPayer != null)
                {
                    string payerId = GetPayerId(MatchedPayer);
                    if (!string.IsNullOrEmpty(payerId))
                    {
                        var payerRecord = records.FirstOrDefault(r => r.TryGetValue("PayerID", out var id) && id.Equals(payerId, StringComparison.OrdinalIgnoreCase));
                        if (payerRecord != null)
                        {
                            if (payerRecord.TryGetValue("DefaultValue", out var val) || payerRecord.TryGetValue("Value", out val))
                            {
                                return val ?? fallback;
                            }
                        }
                    }
                }

                // Priority 2: Match the "Fallback" payer
                var fallbackRecord = records.FirstOrDefault(r => r.TryGetValue("PayerID", out var id) && id.Equals("Fallback", StringComparison.OrdinalIgnoreCase));
                if (fallbackRecord != null)
                {
                    if (fallbackRecord.TryGetValue("DefaultValue", out var fval) || fallbackRecord.TryGetValue("Value", out fval))
                    {
                        return fval ?? fallback;
                    }
                }
            }

            return fallback;
        }

        /// <summary>
        /// Retrieves a fixed default value from the '835_default_code' system.
        /// </summary>
        public string GetFixedDefault(string codeName, string defaultValue = "")
        {
            var sheet = EnsureSheet("835_default_code");
            if (sheet != null)
            {
                // Try multiple possible column names for compatibility
                return sheet.GetValue("Code Name", codeName, "Value") ??
                       sheet.GetValue("Field Name", codeName, "Value") ??
                       sheet.GetValue("Code Name", codeName, "DefaultValue") ??
                       sheet.GetValue("Field Name", codeName, "DefaultValue") ??
                       defaultValue;
            }
            return FixedDefaults.TryGetValue(codeName, out var val) ? val : defaultValue;
        }

        /// <summary>
        /// Checks if a code is a known CARC.
        /// </summary>
        public bool IsCarc(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            var sheet = EnsureSheet("carc_codes_lookup");
            if (sheet != null) return sheet.GetValue("Code", code, "Description") != null;
            return MappingTables.TryGetValue("carc_codes_lookup", out var table) && table.ContainsKey(code);
        }

        /// <summary>
        /// Checks if a code is a known RARC.
        /// </summary>
        public bool IsRarc(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            var sheet = EnsureSheet("remark_codes_lookup");
            if (sheet != null) return sheet.GetValue("Code", code, "Description") != null;
            return MappingTables.TryGetValue("remark_codes_lookup", out var table) && table.ContainsKey(code);
        }

        /// <summary>
        /// Checks if a code is a known NCPDP Reject/Payment Code.
        /// </summary>
        public bool IsNcpdp(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            var sheet = EnsureSheet("ncpdp_codes_lookup");
            if (sheet != null) return sheet.GetValue("Code", code, "Description") != null;
            return MappingTables.TryGetValue("ncpdp_codes_lookup", out var table) && table.ContainsKey(code);
        }

        /// <summary>
        /// Ensures a sheet is available in the Store, falling back to RawMappingTables if needed (for unit tests).
        /// </summary>
        private IConfigSheet? EnsureSheet(string name)
        {
            var sheet = Store.GetSheet(name);
            if (sheet != null) return sheet;

            // Fallback for unit tests that assign directly to RawMappingTables
            if (RawMappingTables.TryGetValue(name, out var records))
            {
                var newSheet = new ConfigSheet(name, records);
                Store.AddSheet(newSheet);
                return newSheet;
            }

            return null;
        }

        private void SyncLegacyToStore(string name)
        {
            if (RawMappingTables.TryGetValue(name, out var records))
            {
                Store.AddSheet(new ConfigSheet(name, records));
            }
        }
    }
}

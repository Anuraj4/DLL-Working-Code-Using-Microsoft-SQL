using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Edi.Generator835.Configuration;
using Xalta.Edi.AddressParser.Interfaces;
using Serilog;

namespace Edi.Generator835.Services
{
    /// <summary>
    /// Maps CSV remittance data to a structured multi-sheet Excel workbook.
    /// Applies column-level mapping rules from configuration, aggregates claim totals,
    /// and optionally parses raw address strings into structured components.
    /// </summary>
    public class CsvToExcelMapper
    {
        // ─────────────────────────────────────────────
        // Constants
        // ─────────────────────────────────────────────

        private const string MapperTableName = "csv_mapper";

        private static class SheetNames
        {
            public const string PaymentHeader = "payment_header";
            public const string Claims = "claims";
            public const string ServiceLines = "service_lines";
            public const string Adjustments = "adjustments";
            public const string Plb = "plb";
        }

        private static class ColNames
        {
            // payment_header
            public const string PaymentID = "PaymentID";
            public const string CheckOrEFTNumber = "CheckOrEFTNumber";
            public const string PayerName = "PayerName";
            public const string PayerAddressLine1 = "PayerAddressLine1";
            public const string PayerCity = "PayerCity";
            public const string PayerState = "PayerState";
            public const string PayerZip = "PayerZip";
            public const string ProviderAddressLine1 = "ProviderAddressLine1";
            public const string ProviderCity = "ProviderCity";
            public const string ProviderState = "ProviderState";
            public const string ProviderZip = "ProviderZip";

            // claims
            public const string ClaimIDPayer = "ClaimID_Payer";
            public const string ClaimIDProvider = "ClaimID_Provider";
            public const string ClaimBilledAmount = "ClaimBilledAmount";
            public const string ClaimPaidAmount = "ClaimPaidAmount";
            public const string ClaimAllowedAmount = "ClaimAllowedAmount";
            public const string PatientResponsibilityAmt = "PatientResponsibilityAmount";

            // service_lines
            public const string ServiceLineID = "ServiceLine_ID";
            public const string CPTCode = "CPTCode";
            public const string LineBilledAmt = "LineBilledAmount";
            public const string LinePatientResponsibilityAmt = "LinePatientResponsibilityAmount";
            public const string Units = "Units";
            public const string LineRemarkCodes = "LineRemarkCodes";
            public const string LineExplanationCodes = "LineExplanationCodes";

            // adjustments
            public const string AdjustmentAmount = "AdjustmentAmount";
            public const string AdjustmentReasonCode = "AdjustmentReasonCode";
            public const string AdjustmentLevel = "AdjustmentLevel";
            public const string RemarkCode = "RemarkCode"; // Legacy/Alternate column name

            // plb
            public const string PLBAmount = "PLBAmount";
            public const string PLBReasonCode = "PLBReasonCode";

            // level values
            public const string LevelServiceLine = "SERVICE_LINE";
            public const string LevelClaim = "CLAIM";
        }

        // ─────────────────────────────────────────────
        // Fields
        // ─────────────────────────────────────────────

        private readonly MappingConfiguration _config;
        private readonly IAddressParser? _addressParser;

        // ─────────────────────────────────────────────
        // Constructor
        // ─────────────────────────────────────────────

        public CsvToExcelMapper(MappingConfiguration config, IAddressParser? addressParser = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _addressParser = addressParser;
        }

        // ─────────────────────────────────────────────
        // Public Entry Point
        // ─────────────────────────────────────────────

        /// <summary>
        /// Converts a CSV remittance file to a multi-sheet Excel workbook.
        /// Supports cancellation via <paramref name="cancellationToken"/>.
        /// </summary>
        public void Convert(
            string csvPath,
            string outputExcelPath,
            CancellationToken cancellationToken = default)
        {
            Log.Information(
                "Starting CSV → Excel conversion. Input: {Input}, Output: {Output}",
                csvPath, outputExcelPath);

            ValidateInputFile(csvPath);

            // 1. Load mapping rules and pre-build O(1) lookup index
            var mappingRules = GetMappingRules();
            var ruleIndex = BuildRuleIndex(mappingRules);

            if (mappingRules.Count == 0)
                Log.Warning("No mapping rules found for '{Table}' in configuration.", MapperTableName);
            else
                Log.Information("Loaded {Count} mapping rules.", mappingRules.Count);

            // 2. Read CSV data
            var csvData = ReadCsv(csvPath);
            Log.Information("Read {Count} records from CSV.", csvData.Count);

            if (csvData.Count > 0)
                Log.Debug("CSV columns: {Columns}", string.Join(", ", csvData[0].Keys));

            // 3. Build output rows in memory
            var paymentRows = new List<Dictionary<string, string>>();
            var claimRows = new List<Dictionary<string, string>>();
            var serviceRows = new List<Dictionary<string, string>>();
            var adjustmentRows = new List<Dictionary<string, string>>();
            var plbRows = new List<Dictionary<string, string>>();

            ProcessAllPayments(
                csvData, ruleIndex, cancellationToken,
                paymentRows, claimRows, serviceRows, adjustmentRows, plbRows);

            // 4. Write to workbook
            WriteWorkbook(
                outputExcelPath,
                paymentRows, claimRows, serviceRows, adjustmentRows, plbRows);

            Log.Information(
                "Conversion complete. Payments={P}, Claims={C}, ServiceLines={S}, Adjustments={A}, PLB={L}",
                paymentRows.Count, claimRows.Count, serviceRows.Count, adjustmentRows.Count, plbRows.Count);
        }

        // ─────────────────────────────────────────────
        // Step 1 – Validation
        // ─────────────────────────────────────────────

        private static void ValidateInputFile(string csvPath)
        {
            if (string.IsNullOrWhiteSpace(csvPath))
                throw new ArgumentException("CSV path must not be empty.", nameof(csvPath));

            if (!File.Exists(csvPath))
                throw new FileNotFoundException($"Input CSV file not found: {csvPath}", csvPath);
        }

        // ─────────────────────────────────────────────
        // Step 2 – Read CSV
        // ─────────────────────────────────────────────

        private static List<Dictionary<string, string>> ReadCsv(string csvPath)
        {
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null,
            };

            using var reader = new StreamReader(csvPath);
            using var csv = new CsvReader(reader, csvConfig);

            var result = new List<Dictionary<string, string>>();

            foreach (var record in csv.GetRecords<dynamic>())
            {
                var source = (IDictionary<string, object>)record;
                var row = new Dictionary<string, string>(source.Count, StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in source)
                    row[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;

                result.Add(row);
            }

            return result;
        }

        // ─────────────────────────────────────────────
        // Step 3 – Process All Payments
        // ─────────────────────────────────────────────

        private void ProcessAllPayments(
            List<Dictionary<string, string>> csvData,
            RuleIndex ruleIndex,
            CancellationToken cancellationToken,
            List<Dictionary<string, string>> paymentRows,
            List<Dictionary<string, string>> claimRows,
            List<Dictionary<string, string>> serviceRows,
            List<Dictionary<string, string>> adjustmentRows,
            List<Dictionary<string, string>> plbRows)
        {
            // Track which payment IDs have already produced a header row
            var processedPayments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Auto-incrementing service line ID for rows with no explicit ID
            int autoSvcId = 1;

            var groupedByPayment = csvData.GroupBy(row => ResolvePaymentId(row, ruleIndex)).ToList();
            Log.Information("Grouped data into {Count} payment(s).", groupedByPayment.Count);

            foreach (var paymentGroup in groupedByPayment)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string paymentId = paymentGroup.Key;

                // --- Payment Header ---
                string currentPayerId = string.Empty;
                string currentEobType = string.Empty;

                if (!processedPayments.Contains(paymentId))
                {
                    var firstRow = paymentGroup.First();
                    var pHeader = MapRowToSheet(firstRow, ruleIndex, SheetNames.PaymentHeader);
                    pHeader[ColNames.PaymentID] = paymentId;

                    currentPayerId = pHeader.TryGetValue("PayerID", out var pid) ? pid : string.Empty;
                    currentEobType = pHeader.TryGetValue("PayerEOBType", out var et) ? et : string.Empty;

                    ParseAndFillAddress(pHeader,
                        ColNames.PayerAddressLine1, ColNames.PayerCity,
                        ColNames.PayerState, ColNames.PayerZip);

                    ParseAndFillAddress(pHeader,
                        ColNames.ProviderAddressLine1, ColNames.ProviderCity,
                        ColNames.ProviderState, ColNames.ProviderZip);

                    paymentRows.Add(pHeader);
                    processedPayments.Add(paymentId);

                    Log.Debug("Payment header added for ID={PaymentId}", paymentId);
                }
                else
                {
                    // If already processed, we still need these for subsequent claims/adjustments
                    var pHeader = MapRowToSheet(paymentGroup.First(), ruleIndex, SheetNames.PaymentHeader);
                    currentPayerId = pHeader.TryGetValue("PayerID", out var pid) ? pid : string.Empty;
                    currentEobType = pHeader.TryGetValue("PayerEOBType", out var et) ? et : string.Empty;
                }

                // --- PLB Rows ---
                var seenPlbKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in paymentGroup)
                {
                    if (!HasPlbData(row, ruleIndex)) continue;

                    var plbRow = MapRowToSheet(row, ruleIndex, SheetNames.Plb);
                    plbRow[ColNames.PaymentID] = paymentId;

                    // Deduplicate PLB rows by amount+reason within a payment
                    string plbKey = $"{GetValue(row, ruleIndex, SheetNames.Plb, ColNames.PLBAmount)}|{GetValue(row, ruleIndex, SheetNames.Plb, ColNames.PLBReasonCode)}";
                    if (seenPlbKeys.Add(plbKey))
                        plbRows.Add(plbRow);
                }

                // --- Claims ---
                ProcessClaims(
                    _config, // Pass config to use ResolveCagc
                    paymentGroup, paymentId, ruleIndex,
                    currentPayerId, currentEobType,
                    claimRows, serviceRows, adjustmentRows,
                    ref autoSvcId);
            }
        }

        // ─────────────────────────────────────────────
        // Step 3a – Process Claims Within a Payment
        // ─────────────────────────────────────────────

        private static void ProcessClaims(
            MappingConfiguration mappings,
            IGrouping<string, Dictionary<string, string>> paymentGroup,
            string paymentId,
            RuleIndex ruleIndex,
            string payerId,
            string eobType,
            List<Dictionary<string, string>> claimRows,
            List<Dictionary<string, string>> serviceRows,
            List<Dictionary<string, string>> adjustmentRows,
            ref int autoSvcId)
        {
            var groupedByClaim = paymentGroup
                .GroupBy(row => ResolveClaimId(row, ruleIndex))
                .ToList();

            Log.Debug("Payment {PaymentId} → {Count} claim(s).", paymentId, groupedByClaim.Count);

            foreach (var claimGroup in groupedByClaim)
            {
                string claimId = claimGroup.Key;

                if (string.IsNullOrWhiteSpace(claimId))
                {
                    Log.Warning("Skipping rows with empty ClaimID in payment {PaymentId}.", paymentId);
                    continue;
                }

                // --- Aggregate claim-level totals across all service lines ---
                decimal totalBilled = 0m;
                decimal totalPaid = 0m;
                decimal totalAllowed = 0m;
                decimal totalResp = 0m;

                foreach (var row in claimGroup)
                {
                    totalBilled += ParseDecimal(GetValue(row, ruleIndex, SheetNames.Claims, ColNames.ClaimBilledAmount));
                    totalPaid += ParseDecimal(GetValue(row, ruleIndex, SheetNames.Claims, ColNames.ClaimPaidAmount));
                    totalAllowed += ParseDecimal(GetValue(row, ruleIndex, SheetNames.Claims, ColNames.ClaimAllowedAmount));
                    totalResp += ParseDecimal(GetValue(row, ruleIndex, SheetNames.Claims, ColNames.PatientResponsibilityAmt));
                }

                // --- Write aggregated claim row ---
                var firstClaimRow = claimGroup.First();
                var cRow = MapRowToSheet(firstClaimRow, ruleIndex, SheetNames.Claims);
                cRow[ColNames.PaymentID] = paymentId;
                cRow[ColNames.ClaimIDPayer] = claimId;
                cRow[ColNames.ClaimBilledAmount] = totalBilled.ToString("F2");
                cRow[ColNames.ClaimPaidAmount] = totalPaid.ToString("F2");
                cRow[ColNames.ClaimAllowedAmount] = totalAllowed.ToString("F2");
                cRow[ColNames.PatientResponsibilityAmt] = totalResp.ToString("F2");

                claimRows.Add(cRow);

                Log.Debug("Claim {ClaimId}: Billed={B:F2}, Paid={P:F2}", claimId, totalBilled, totalPaid);

                // --- Service Lines & Adjustments ---
                ProcessServiceLinesAndAdjustments(
                    mappings,
                    claimGroup, paymentId, claimId, ruleIndex,
                    payerId, eobType,
                    serviceRows, adjustmentRows, ref autoSvcId);

                Log.Debug("[CsvMapper] Claim {ClaimId}: Finished processing service lines.", claimId);
            }
        }

        // ─────────────────────────────────────────────
        // Step 3b – Service Lines & Adjustments
        // ─────────────────────────────────────────────

        private static void ProcessServiceLinesAndAdjustments(
            MappingConfiguration mappings,
            IGrouping<string, Dictionary<string, string>> claimGroup,
            string paymentId,
            string claimId,
            RuleIndex ruleIndex,
            string payerId,
            string eobType,
            List<Dictionary<string, string>> serviceRows,
            List<Dictionary<string, string>> adjustmentRows,
            ref int autoSvcId)
        {
            // Tracks previously added service lines for this claim to allow aggregation
            var svcMap = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            // Tracks the last seen service line ID so orphaned adjustments
            // (those on separate rows with no explicit ID) can still be linked.
            string lastServiceLineId = string.Empty;

            foreach (var row in claimGroup)
            {
                // ── Service Line ──────────────────────────────────────────
                string? currentServiceLineId = null;

                if (HasServiceData(row, ruleIndex))
                {
                    // Resolve ID: explicit mapping → direct column → auto-increment
                    string svcId = GetValue(row, ruleIndex, SheetNames.ServiceLines, ColNames.ServiceLineID);

                    if (string.IsNullOrWhiteSpace(svcId) &&
                        row.TryGetValue(ColNames.ServiceLineID, out var directId))
                        svcId = directId;

                    if (string.IsNullOrWhiteSpace(svcId))
                        svcId = (autoSvcId++).ToString();

                    currentServiceLineId = svcId;
                    lastServiceLineId = svcId;

                    if (!svcMap.TryGetValue(svcId, out var existingSvc))
                    {
                        var sRow = MapRowToSheet(row, ruleIndex, SheetNames.ServiceLines);
                        sRow[ColNames.ClaimIDPayer] = claimId;
                        sRow[ColNames.ServiceLineID] = svcId;
                        svcMap[svcId] = sRow;
                        serviceRows.Add(sRow);
                    }
                    else
                    {
                        // AGGREGATE: merge new non-blank fields into existing row
                        var newSvcData = MapRowToSheet(row, ruleIndex, SheetNames.ServiceLines);
                        foreach (var kvp in newSvcData)
                        {
                            if (string.IsNullOrWhiteSpace(kvp.Value)) continue;

                            if (kvp.Key.Equals(ColNames.LineRemarkCodes, StringComparison.OrdinalIgnoreCase) ||
                                kvp.Key.Equals(ColNames.LineExplanationCodes, StringComparison.OrdinalIgnoreCase))
                            {
                                // Append remark/explanation codes if they differ
                                if (string.IsNullOrWhiteSpace(existingSvc[kvp.Key]))
                                    existingSvc[kvp.Key] = kvp.Value;
                                else if (!existingSvc[kvp.Key].Contains(kvp.Value))
                                    existingSvc[kvp.Key] += ", " + kvp.Value;
                            }
                            else if (string.IsNullOrWhiteSpace(existingSvc[kvp.Key]))
                            {
                                // Fill other missing data (CPT, amounts etc. normally only on one row but just in case)
                                existingSvc[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }

                // ── Adjustment ────────────────────────────────────────────
                if (HasAdjustmentData(row, ruleIndex))
                {
                    var aRow = MapRowToSheet(row, ruleIndex, SheetNames.Adjustments);
                    aRow[ColNames.PaymentID] = paymentId;
                    aRow[ColNames.ClaimIDPayer] = claimId;

                    // Resolve adjustment's service line ID with clear precedence:
                    // 1. Explicitly mapped from CSV
                    // 2. Direct column on the row
                    // 3. Service line on same row (currentServiceLineId)
                    // 4. Last service line seen in this claim (lastServiceLineId)
                    string adjSvcId = GetValue(row, ruleIndex, SheetNames.Adjustments, ColNames.ServiceLineID);

                    if (string.IsNullOrWhiteSpace(adjSvcId) &&
                        row.TryGetValue(ColNames.ServiceLineID, out var aDirectId))
                        adjSvcId = aDirectId;

                    if (string.IsNullOrWhiteSpace(adjSvcId))
                        adjSvcId = currentServiceLineId ?? lastServiceLineId;

                    aRow[ColNames.ServiceLineID] = adjSvcId ?? string.Empty;

                    // Infer AdjustmentLevel if not already set
                    if (!aRow.TryGetValue(ColNames.AdjustmentLevel, out var lvl) ||
                        string.IsNullOrWhiteSpace(lvl))
                    {
                        string cptCode = GetValue(row, ruleIndex, SheetNames.ServiceLines, ColNames.CPTCode);
                        aRow[ColNames.AdjustmentLevel] = !string.IsNullOrWhiteSpace(adjSvcId) || !string.IsNullOrWhiteSpace(cptCode)
                            ? ColNames.LevelServiceLine
                            : ColNames.LevelClaim;
                    }

                    // --- CARC/CAGC RESOLUTION HIERARCHY ---
                    // Apply validation and correction during initial mapping
                    if (aRow.TryGetValue(ColNames.AdjustmentReasonCode, out var carc) && !string.IsNullOrEmpty(carc))
                    {
                        var rawCagc = aRow.TryGetValue("AdjustmentGroupCode", out var g) ? g : string.Empty;
                        var finalCagc = mappings.ResolveCagc(payerId, eobType, carc, rawCagc);

                        if (!string.IsNullOrEmpty(finalCagc))
                        {
                            aRow["AdjustmentGroupCode"] = finalCagc;
                        }
                    }

                    adjustmentRows.Add(aRow);
                }
            }
        }

        // ─────────────────────────────────────────────
        // Step 4 – Write Workbook
        // ─────────────────────────────────────────────


        private static void WriteWorkbook(
            string outputExcelPath,
            List<Dictionary<string, string>> paymentRows,
            List<Dictionary<string, string>> claimRows,
            List<Dictionary<string, string>> serviceRows,
            List<Dictionary<string, string>> adjustmentRows,
            List<Dictionary<string, string>> plbRows)
        {
            using var workbook = File.Exists(outputExcelPath)
                ? new XLWorkbook(outputExcelPath)
                : new XLWorkbook();

            WriteSheet(GetOrCreateSheet(workbook, SheetNames.PaymentHeader), paymentRows);
            WriteSheet(GetOrCreateSheet(workbook, SheetNames.Claims), claimRows);
            WriteSheet(GetOrCreateSheet(workbook, SheetNames.ServiceLines), serviceRows);
            WriteSheet(GetOrCreateSheet(workbook, SheetNames.Adjustments), adjustmentRows);
            WriteSheet(GetOrCreateSheet(workbook, SheetNames.Plb), plbRows);

            workbook.SaveAs(outputExcelPath);
            Log.Information("Workbook saved: {Path}", outputExcelPath);
        }

        // ─────────────────────────────────────────────
        // Mapping Helpers
        // ─────────────────────────────────────────────

        private List<MappingRule> GetMappingRules()
        {
            if (!_config.RawMappingTables.TryGetValue(MapperTableName, out var records))
                return new List<MappingRule>();

            var rules = new List<MappingRule>(records.Count);

            foreach (var r in records)
            {
                if (!r.TryGetValue("CsvColumn", out var csvCol) || string.IsNullOrWhiteSpace(csvCol)) continue;
                if (!r.TryGetValue("TargetSheet", out var sheet) || string.IsNullOrWhiteSpace(sheet)) continue;
                if (!r.TryGetValue("TargetColumn", out var targetCol) || string.IsNullOrWhiteSpace(targetCol)) continue;

                rules.Add(new MappingRule
                {
                    CsvColumn = csvCol,
                    TargetSheet = sheet,
                    TargetColumn = targetCol,
                    DataType = r.TryGetValue("DataType", out var dt) ? dt : "String",
                    DefaultValue = r.TryGetValue("DefaultValue", out var dv) ? dv : string.Empty,
                });
            }

            return rules;
        }

        /// <summary>
        /// Builds a dictionary keyed by "TargetSheet:TargetColumn" for O(1) rule lookup.
        /// </summary>
        private static RuleIndex BuildRuleIndex(List<MappingRule> rules)
        {
            var index = new RuleIndex(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in rules)
            {
                string key = MakeRuleKey(rule.TargetSheet, rule.TargetColumn);
                // First rule wins if duplicates exist (TryAdd not available in net48)
                if (!index.ContainsKey(key))
                    index[key] = rule;
            }

            return index;
        }

        private static string MakeRuleKey(string sheet, string column) =>
            $"{sheet}:{column}";

        /// <summary>
        /// Maps a CSV row to a target sheet dictionary using the pre-built rule index.
        /// </summary>
        private static Dictionary<string, string> MapRowToSheet(
            Dictionary<string, string> rowData,
            RuleIndex ruleIndex,
            string sheetName)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Iterate only the rules relevant to this sheet
            foreach (var kvp in ruleIndex)
            {
                if (!kvp.Key.StartsWith(sheetName + ":", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rule = kvp.Value;
                string val = rowData.TryGetValue(rule.CsvColumn, out var csv)
                    ? csv
                    : rule.DefaultValue;

                if (!string.IsNullOrWhiteSpace(val))
                    Log.Verbose("[CsvMapper] Map '{SourceCol}' -> '{TargetCol}': '{Value}'", rule.CsvColumn, rule.TargetColumn, val);

                bool isDecimal = rule.DataType.Equals("Decimal", StringComparison.OrdinalIgnoreCase);

                if (isDecimal)
                {
                    if (string.IsNullOrWhiteSpace(val))
                    {
                        result[rule.TargetColumn] = string.Empty;
                    }
                    else
                    {
                        result[rule.TargetColumn] = ParseDecimal(val).ToString("F2");
                    }
                }
                else
                {
                    result[rule.TargetColumn] = val;
                }
            }

            return result;
        }

        /// <summary>
        /// Fast O(1) single-value lookup using the pre-built rule index.
        /// </summary>
        private static string GetValue(
            Dictionary<string, string> row,
            RuleIndex ruleIndex,
            string sheet,
            string targetCol)
        {
            string key = MakeRuleKey(sheet, targetCol);

            if (!ruleIndex.TryGetValue(key, out var rule))
                return string.Empty;

            return row.TryGetValue(rule.CsvColumn, out var val)
                ? val
                : rule.DefaultValue;
        }

        // ─────────────────────────────────────────────
        // ID Resolution Helpers
        // ─────────────────────────────────────────────

        private static string ResolvePaymentId(Dictionary<string, string> row, RuleIndex ruleIndex)
        {
            string id = GetValue(row, ruleIndex, SheetNames.PaymentHeader, ColNames.CheckOrEFTNumber);

            if (string.IsNullOrWhiteSpace(id))
                id = GetValue(row, ruleIndex, SheetNames.PaymentHeader, ColNames.PaymentID);

            // Fallback: stable unique key per unidentifiable payment
            return string.IsNullOrWhiteSpace(id)
                ? "TEMP_PAY_" + Guid.NewGuid().ToString("N")
                : id;
        }

        private static string ResolveClaimId(Dictionary<string, string> row, RuleIndex ruleIndex)
        {
            string id = GetValue(row, ruleIndex, SheetNames.Claims, ColNames.ClaimIDPayer);

            if (string.IsNullOrWhiteSpace(id))
                id = GetValue(row, ruleIndex, SheetNames.Claims, ColNames.ClaimIDProvider);

            return id;
        }

        // ─────────────────────────────────────────────
        // Data-Presence Guards
        // ─────────────────────────────────────────────

        private static bool HasServiceData(Dictionary<string, string> row, RuleIndex ruleIndex) =>
            !string.IsNullOrWhiteSpace(GetValue(row, ruleIndex, SheetNames.ServiceLines, ColNames.CPTCode)) ||
            !string.IsNullOrWhiteSpace(GetValue(row, ruleIndex, SheetNames.ServiceLines, ColNames.LineBilledAmt)) ||
            !string.IsNullOrWhiteSpace(GetValue(row, ruleIndex, SheetNames.ServiceLines, ColNames.LinePatientResponsibilityAmt)) ||
            !string.IsNullOrWhiteSpace(GetValue(row, ruleIndex, SheetNames.ServiceLines, ColNames.Units)) ||
            !string.IsNullOrWhiteSpace(GetValue(row, ruleIndex, SheetNames.ServiceLines, ColNames.LineExplanationCodes)) ||
            !string.IsNullOrWhiteSpace(GetValue(row, ruleIndex, SheetNames.ServiceLines, ColNames.LineRemarkCodes));

        private static bool HasAdjustmentData(Dictionary<string, string> row, RuleIndex ruleIndex) =>
            !string.IsNullOrWhiteSpace(GetValue(row, ruleIndex, SheetNames.Adjustments, ColNames.AdjustmentAmount)) ||
            !string.IsNullOrWhiteSpace(GetValue(row, ruleIndex, SheetNames.Adjustments, ColNames.AdjustmentReasonCode));

        private static bool HasPlbData(Dictionary<string, string> row, RuleIndex ruleIndex) =>
            !string.IsNullOrWhiteSpace(GetValue(row, ruleIndex, SheetNames.Plb, ColNames.PLBAmount)) ||
            !string.IsNullOrWhiteSpace(GetValue(row, ruleIndex, SheetNames.Plb, ColNames.PLBReasonCode));

        // ─────────────────────────────────────────────
        // Address Parsing
        // ─────────────────────────────────────────────

        private void ParseAndFillAddress(
            Dictionary<string, string> rowDict,
            string addrKey,
            string cityKey,
            string stateKey,
            string zipKey)
        {
            if (_addressParser == null) return;

            if (!rowDict.TryGetValue(addrKey, out var rawAddress) ||
                string.IsNullOrWhiteSpace(rawAddress))
                return;

            bool cityMissing = !rowDict.TryGetValue(cityKey, out var c) || string.IsNullOrWhiteSpace(c);
            bool stateMissing = !rowDict.TryGetValue(stateKey, out var s) || string.IsNullOrWhiteSpace(s);
            bool zipMissing = !rowDict.TryGetValue(zipKey, out var z) || string.IsNullOrWhiteSpace(z);

            if (!cityMissing && !stateMissing && !zipMissing) return;

            try
            {
                var parsed = _addressParser.Parse(rawAddress);
                if (parsed == null) return;

                rowDict[addrKey] = parsed.AddressLine1 ?? rawAddress;
                if (cityMissing) rowDict[cityKey] = parsed.City ?? string.Empty;
                if (stateMissing) rowDict[stateKey] = parsed.State ?? string.Empty;
                if (zipMissing) rowDict[zipKey] = parsed.Zip ?? string.Empty;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Address parsing failed for '{Address}'.", rawAddress);
            }
        }

        // ─────────────────────────────────────────────
        // Excel Write Helpers
        // ─────────────────────────────────────────────

        private static IXLWorksheet GetOrCreateSheet(XLWorkbook workbook, string sheetName)
        {
            return workbook.Worksheets.TryGetWorksheet(sheetName, out var ws)
                ? ws
                : workbook.Worksheets.Add(sheetName);
        }

        private static void WriteSheet(
            IXLWorksheet sheet,
            List<Dictionary<string, string>> rows)
        {
            if (rows.Count == 0) return;

            // 1. Read or derive headers from the template's first row
            var headers = ReadOrDeriveHeaders(sheet, rows);

            // Pre-build a normalized key → header-index map for O(1) column lookup
            var normalizedIndex = new Dictionary<string, int>(headers.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
            {
                string norm = Normalize(headers[i]);
                // First header wins on collision (TryAdd not available in net48)
                if (!normalizedIndex.ContainsKey(norm))
                    normalizedIndex[norm] = i;
            }

            // 2. Clear existing data below headers to avoid mixed/phantom data
            int maxUsedCol = sheet.LastColumnUsed()?.ColumnNumber() ?? headers.Count;
            int maxUsedRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
            if (maxUsedRow >= 2)
            {
                sheet.Range(2, 1, maxUsedRow, maxUsedCol).Clear(XLClearOptions.Contents);
            }

            // 3. Write each data row starting at row 2
            int startRow = 2;
            for (int r = 0; r < rows.Count; r++)
            {
                var rowData = rows[r];
                int xlRow = startRow + r;

                foreach (var kvp in rowData)
                {
                    string normKey = Normalize(kvp.Key);

                    if (normalizedIndex.TryGetValue(normKey, out int colIdx))
                    {
                        sheet.Cell(xlRow, colIdx + 1).Value = kvp.Value;
                    }
                }
            }
        }

        private static List<string> ReadOrDeriveHeaders(
            IXLWorksheet sheet,
            List<Dictionary<string, string>> rows)
        {
            var headers = new List<string>();
            var firstRow = sheet.Row(1);
            var lastCell = firstRow.LastCellUsed();  // null-safe

            if (lastCell != null)
            {
                for (int i = 1; i <= lastCell.Address.ColumnNumber; i++)
                {
                    var h = firstRow.Cell(i).GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(h))
                        headers.Add(h);
                }
            }

            // No template headers — derive from data and write them
            if (headers.Count == 0)
            {
                headers = rows
                    .SelectMany(r => r.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < headers.Count; i++)
                    sheet.Cell(1, i + 1).Value = headers[i];
            }

            return headers;
        }

        // ─────────────────────────────────────────────
        // Utility Helpers
        // ─────────────────────────────────────────────

        private static decimal ParseDecimal(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return 0m;

            string trimmed = val.Trim();

            // Detect sign BEFORE stripping characters
            bool isNegative = trimmed.StartsWith("-")
                           || trimmed.EndsWith("-")
                           || (trimmed.StartsWith("(") && trimmed.EndsWith(")"));

            // Extract only digits and first decimal point
            // Handles: "- $ 51.46", "(51.46)", "-$1,234.56", "51.46-" etc.
            var digits = new System.Text.StringBuilder(trimmed.Length);
            bool hasDecimalPoint = false;

            foreach (char c in trimmed)
            {
                if (char.IsDigit(c))
                    digits.Append(c);
                else if (c == '.' && !hasDecimalPoint)
                {
                    digits.Append(c);
                    hasDecimalPoint = true;
                }
            }

            if (digits.Length == 0)
            {
                Log.Warning("ParseDecimal: no numeric content in '{Value}', returning 0.", val);
                return 0m;
            }

            if (!decimal.TryParse(digits.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
            {
                Log.Warning("ParseDecimal: could not parse '{Digits}' (from '{Value}'), returning 0.", digits, val);
                return 0m;
            }

            return isNegative ? -result : result;
        }

        /// <summary>
        /// Normalises a column name for fuzzy matching (lowercase, strip spaces and underscores).
        /// </summary>
        private static string Normalize(string s) =>
            s.Replace(" ", "").Replace("_", "").ToLowerInvariant();

        // ─────────────────────────────────────────────
        // Inner Types
        // ─────────────────────────────────────────────

        /// <summary>Type alias for the pre-built rule lookup dictionary.</summary>
        private sealed class RuleIndex : Dictionary<string, MappingRule>
        {
            public RuleIndex(IEqualityComparer<string> comparer) : base(comparer) { }
        }

        private sealed class MappingRule
        {
            public string CsvColumn { get; init; } = string.Empty;
            public string TargetSheet { get; init; } = string.Empty;
            public string TargetColumn { get; init; } = string.Empty;
            public string DataType { get; init; } = "String";
            public string DefaultValue { get; init; } = string.Empty;
        }
    }
}
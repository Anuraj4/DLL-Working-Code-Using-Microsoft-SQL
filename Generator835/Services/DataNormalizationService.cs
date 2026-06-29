using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Edi.Generator835.Configuration;
using Edi.Generator835.Models;
using Edi.Generator835.Services.Interfaces;
using Serilog;

namespace Edi.Generator835.Services
{
    public class DataNormalizationService : IDataNormalizer
    {
        private static readonly HashSet<string> _placeholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "null", "na", "n/a", "n.a.", "n.a", "none", "nan", "nil", "not applicable", "not available", "Not Available",
            "NA", "N/A", "N.A.", "N.A", "None", "NaN", "Nil", "Not Applicable", "N / A", "N /A", "N/ A", "Not", "not", "Available", "available"
        };

        public void Normalize(Edi835DataModel model, MappingConfiguration mappings)
        {
            Log.Information("[CAS-DEBUG] ── NORMALIZING NEW MODEL ── PayerId='{PayerId}', PaymentId='{PaymentId}', Claims={ClaimsCount}",
                model.Header?.PayerId, model.Header?.PaymentId, model.Claims?.Count ?? 0);
            Log.Information("Normalizing raw EOB data model...");

            if (model.Header is null || model.Claims is null)
            {
                Log.Error("Header or Claims is null");
                return;
            }

            // 1. Header Normalization
            NormalizeHeader(model.Header);

            // 2. Claims Normalization
            foreach (var claim in model.Claims)
            {
                NormalizeClaim(claim);

                var newClaimAdjs = new List<AdjustmentData>();
                foreach (var adj in claim.ClaimAdjustments)
                {
                    newClaimAdjs.AddRange(NormalizeComplexAdjustment(adj, model.Header.PayerId, model.Header.PayerEobType, mappings));
                }

                // ── [NEW] Deduplication Step for Claim Adjustments ──
                claim.ClaimAdjustments = newClaimAdjs
                    .GroupBy(a => new
                    {
                        Group = a.AdjustmentGroupCode?.ToUpperInvariant(),
                        Reason = a.AdjustmentReasonCode?.ToUpperInvariant()
                    })
                    .Select(group =>
                    {
                        var first = group.First();
                        first.AdjustmentAmount = group.Sum(x => x.AdjustmentAmount);
                        var mergedBucket = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var item in group)
                        {
                            foreach (var b in item.SpecialCodeBucket) mergedBucket.Add(b);
                        }
                        first.SpecialCodeBucket = mergedBucket;
                        return first;
                    })
                    .ToList();

                foreach (var line in claim.ServiceLines)
                {
                    NormalizeServiceLine(line, model.Header.PayerId, model.Header.PayerEobType, mappings);

                    var newLineAdjs = new List<AdjustmentData>();
                    foreach (var adj in line.Adjustments)
                    {
                        // Update: Pass Line Remark/Explanation codes to ensure bucketing considers all sources
                        newLineAdjs.AddRange(NormalizeComplexAdjustment(
                            adj, model.Header.PayerId, model.Header.PayerEobType, mappings,
                            line.LineRemarkCodes, line.LineExplanationCodes));
                    }

                    // ── [NEW] Deduplication Step ──
                    // Group by GroupCode and ReasonCode to prevent repeating codes.
                    // Sum the amounts and union the buckets.
                    line.Adjustments = newLineAdjs
                        .GroupBy(a => new
                        {
                            Group = a.AdjustmentGroupCode?.ToUpperInvariant(),
                            Reason = a.AdjustmentReasonCode?.ToUpperInvariant()
                        })
                        .Select(group =>
                        {
                            var first = group.First();
                            // Ensure the amount is the sum of all components
                            first.AdjustmentAmount = group.Sum(x => x.AdjustmentAmount);

                            // Merge all buckets found in the group
                            var mergedBucket = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var item in group)
                            {
                                foreach (var b in item.SpecialCodeBucket) mergedBucket.Add(b);
                            }
                            first.SpecialCodeBucket = mergedBucket;

                            return first;
                        })
                        .ToList();
                }
            }

            // 3. Provider Adjustments
            foreach (var plb in model.ProviderAdjustments)
            {
                NormalizePlb(plb);
            }

            Log.Information("Data normalization complete.");
        }

        /// <summary>
        /// Automatically synchronizes the contextual metadata (ServiceLine_ID, AdjustmentLevel) 
        /// of all adjustments based on their parent objects. Ensures scalability for future rules.
        /// </summary>
        public void SynchronizeContext(Edi835DataModel model)
        {
            if (model == null) return;

            foreach (var claim in model.Claims)
            {
                // Synchronize Claim-level adjustments
                foreach (var adj in claim.ClaimAdjustments)
                {
                    adj.AdjustmentLevel = "CLAIM";
                    adj.ServiceLineId = ""; // No service line ID at claim level
                    adj.ClaimIdPayer = claim.ClaimIdPayer;
                    adj.PaymentId = claim.PaymentId;
                }

                // Synchronize ServiceLine-level adjustments
                foreach (var line in claim.ServiceLines)
                {
                    foreach (var adj in line.Adjustments)
                    {
                        adj.AdjustmentLevel = "SERVICE_LINE";
                        adj.ServiceLineId = line.ServiceLineId;
                        adj.ClaimIdPayer = line.ClaimIdPayer;
                        // Use claim payment id if adjustment payment id is missing
                        if (string.IsNullOrEmpty(adj.PaymentId)) adj.PaymentId = claim.PaymentId;
                    }
                }
            }
            Log.Debug("[SynchronizeContext] Successfully synced metadata for all adjustments.");
        }

        private void NormalizeHeader(HeaderData header)
        {
            header.PaymentId = PerfectText(header.PaymentId);
            header.PayerName = PerfectText(header.PayerName);
            header.PayerId = PerfectText(header.PayerId);
            header.PayerAddressLine1 = NormalizeAddress(header.PayerAddressLine1, header.PayerName);
            header.PayerCity = PerfectText(header.PayerCity);
            header.PayerState = PerfectText(header.PayerState);
            header.PayerZip = PerfectText(header.PayerZip);

            header.ProviderName = PerfectText(header.ProviderName);
            header.ProviderNpi = PerfectText(header.ProviderNpi);
            header.ProviderTaxId = PerfectText(header.ProviderTaxId.Replace("*", "X"));
            header.ProviderAddressLine1 = NormalizeAddress(header.ProviderAddressLine1, header.ProviderName);
            header.ProviderCity = PerfectText(header.ProviderCity);
            header.ProviderState = PerfectText(header.ProviderState);
            header.ProviderZip = PerfectText(header.ProviderZip);

            header.PaymentMethod = PerfectText(header.PaymentMethod);
            header.PaymentDate = FormatEdiDate(header.PaymentDate);
            header.CheckOrEftNumber = PerfectText(header.CheckOrEftNumber);
            header.RoutingNumber = PerfectText(header.RoutingNumber);
            header.BankAccountNumber = PerfectText(header.BankAccountNumber);
            header.PayerEobType = PerfectText(header.PayerEobType);
            header.CurrencySymbol = PerfectText(header.CurrencySymbol);
            header.CurrencyCode = PerfectText(header.CurrencyCode);
            header.PayerCommunicationNumber = PerfectText(header.PayerCommunicationNumber);
        }

        private void NormalizeClaim(ClaimData claim)
        {
            claim.PaymentId = PerfectText(claim.PaymentId);
            claim.ClaimIdPayer = PerfectText(claim.ClaimIdPayer);
            claim.ClaimIdProvider = PerfectText(claim.ClaimIdProvider);
            claim.ClaimType = PerfectText(claim.ClaimType);

            claim.PatientName = PerfectText(claim.PatientName);
            var (last, first) = ParseName(claim.PatientName);
            claim.PatientLastName = string.IsNullOrEmpty(claim.PatientLastName) ? last : PerfectText(claim.PatientLastName);
            claim.PatientFirstName = string.IsNullOrEmpty(claim.PatientFirstName) ? first : PerfectText(claim.PatientFirstName);

            claim.PatientDob = FormatEdiDate(claim.PatientDob);
            claim.PatientId = PerfectText(claim.PatientId);

            claim.SubscriberName = PerfectText(claim.SubscriberName);
            claim.SubscriberId = PerfectText(claim.SubscriberId);

            claim.ProviderRenderingName = PerfectText(claim.ProviderRenderingName);
            claim.ProviderRenderingNpi = PerfectText(claim.ProviderRenderingNpi);

            claim.ClaimStatusCode = PerfectText(claim.ClaimStatusCode);

            // Detect reversal BEFORE normalizing amounts (negative paid = takeback/reversal)
            claim.IsReversal = claim.ClaimPaidAmount < 0;

            claim.ClaimBilledAmount = Math.Abs(claim.ClaimBilledAmount);
            claim.ClaimPaidAmount = Math.Abs(claim.ClaimPaidAmount);

            var serviceDates = ParseDateRange(claim.ClaimServiceDateFrom, claim.ClaimServiceDateTo);
            claim.ClaimServiceDateFrom = serviceDates.From;
            claim.ClaimServiceDateTo = serviceDates.To;

            claim.ClaimRemarkCodes = PerfectText(claim.ClaimRemarkCodes);

            if (claim.PatientResponsibilityAmount.HasValue)
            {
                claim.PatientResponsibilityAmount = Math.Abs(claim.PatientResponsibilityAmount.Value);
            }
        }

        private void NormalizeServiceLine(ServiceLineData line, string payerId, string payerEobType, MappingConfiguration mappings)
        {
            line.ClaimIdPayer = PerfectText(line.ClaimIdPayer);

            string rawCpt = NormalizeProcedureCode(line.CptCode);
            line.Modifier1 = PerfectText(line.Modifier1);
            line.Modifier2 = PerfectText(line.Modifier2);


            // Split CPT/HCPCS and Modifier (e.g. "99213-25", "A0428-RT", "J3490JW")
            // CPT/HCPCS are typically 5 characters, followed by a 2-character modifier.
            var modMatch = Regex.Match(rawCpt, @"^([A-Z0-9]{5})[-\s.:]*([A-Z0-9]{2})$", RegexOptions.IgnoreCase);
            if (modMatch.Success)
            {
                line.CptCode = modMatch.Groups[1].Value.ToUpperInvariant();
                string modifier = modMatch.Groups[2].Value.ToUpperInvariant();

                if (string.IsNullOrEmpty(line.Modifier1))
                {
                    line.Modifier1 = modifier;
                    Log.Information("Extracted Modifier '{Modifier}' from CPT code field.", modifier);
                }
                else if (string.IsNullOrEmpty(line.Modifier2) && !line.Modifier1.Equals(modifier, StringComparison.OrdinalIgnoreCase))
                {
                    line.Modifier2 = modifier;
                    Log.Information("Extracted second Modifier '{Modifier}' from CPT code field.", modifier);
                }
            }
            else
            {
                line.CptCode = rawCpt.ToUpperInvariant();
            }

            line.RevenueCode = PerfectText(line.RevenueCode);
            line.NdcCode = PerfectText(line.NdcCode);
            line.Units = PerfectText(line.Units);

            line.LineBilledAmount = Math.Abs(line.LineBilledAmount);
            line.LinePaidAmount = Math.Abs(line.LinePaidAmount);

            var lineDates = ParseDateRange(line.LineServiceDateFrom, line.LineServiceDateTo);
            line.LineServiceDateFrom = lineDates.From;
            line.LineServiceDateTo = lineDates.To;

            if (line.LinePatientResponsibilityAmount.HasValue)
            {
                line.LinePatientResponsibilityAmount = Math.Abs(line.LinePatientResponsibilityAmount.Value);
            }

            string rawRemarkText = PerfectText(line.LineRemarkCodes);
            string rawExplanationText = PerfectText(line.LineExplanationCodes);

            // Combine both strings for unified processing
            string combinedText = string.Join(", ", new[] { rawRemarkText, rawExplanationText }.Where(s => !string.IsNullOrWhiteSpace(s)));

            line.LineRemarkCodes = rawRemarkText;
            line.LineExplanationCodes = rawExplanationText;

            if (!string.IsNullOrEmpty(combinedText))
            {
                var finalCagcs = new List<string>();
                var finalCarcs = new List<string>();
                var finalRarcs = new List<string>();
                var validGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CO", "OA", "PI", "PR" };

                // Helper to process any token
                void ProcessToken(string input)
                {
                    if (string.IsNullOrWhiteSpace(input)) return;
                    string trimmed = input.Trim();
                    if (_placeholders.Contains(trimmed))
                    {
                        Log.Debug("Skipping placeholder: {Input}", trimmed);
                        return;
                    }

                    // User requested to consider space and slash separated values as well
                    // Also stripping non-sensical prefixes like '0>' and 'Q>'
                    var items = input.Split(new[] { ',', ';', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(t => Regex.Replace(t.Trim(), @"^(0>|Q>)+", "", RegexOptions.IgnoreCase))
                                     .Where(t => !string.IsNullOrEmpty(t));

                    foreach (var item in items)
                    {
                        if (_placeholders.Contains(item)) continue;

                        string cleanItem = item;
                        if (cleanItem.StartsWith("C0", StringComparison.OrdinalIgnoreCase))
                        {
                            cleanItem = "CO" + cleanItem.Substring(2);
                            Log.Information("Corrected OCR error C0 to CO in token: {Token}", item);
                        }
                        else if (cleanItem.StartsWith("0A", StringComparison.OrdinalIgnoreCase))
                        {
                            cleanItem = "OA" + cleanItem.Substring(2);
                            Log.Information("Corrected OCR error 0A to OA in token: {Token}", item);
                        }

                        var match = Regex.Match(cleanItem, @"^(CO|OA|PI|PR)[-\s]*(.+)$", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            string cagc = match.Groups[1].Value.ToUpperInvariant();
                            string rest = match.Groups[2].Value.Trim();
                            if (!finalCagcs.Contains(cagc)) finalCagcs.Add(cagc);
                            ProcessToken(rest);
                            continue;
                        }

                        if (validGroups.Contains(cleanItem))
                        {
                            string cagcUpper = cleanItem.ToUpperInvariant();
                            if (!finalCagcs.Contains(cagcUpper)) finalCagcs.Add(cagcUpper);
                            continue;
                        }

                        if (mappings.IsCarc(cleanItem))
                        {
                            if (!finalCarcs.Contains(cleanItem)) finalCarcs.Add(cleanItem);
                            continue;
                        }

                        if (!finalRarcs.Contains(cleanItem))
                        {
                            finalRarcs.Add(cleanItem);
                            if (mappings.IsRarc(cleanItem)) Log.Information("100% correct remark identified: {RemarkCode}", cleanItem);
                            else Log.Warning("Unrecognized remark code: {RemarkCode}. Not found in remark_codes_lookup.", cleanItem);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(combinedText))
                {
                    Log.Information("[NormalizeServiceLine] Processing LineRemarkCodes & LineExplanationCodes: '{Raw}'", combinedText);
                }

                ProcessToken(combinedText);

                string uniqueRemarks = string.Join(", ", finalRarcs.Distinct());

                // ALWAYS update to the cleaned list, even if empty, to wash out placeholders like "na"
                line.LineRemarkCodes = uniqueRemarks;

                // Sync explanation with remark codes if explanation is empty/placeholder
                if (string.IsNullOrEmpty(line.LineExplanationCodes) || _placeholders.Contains(line.LineExplanationCodes.Trim()))
                {
                    line.LineExplanationCodes = uniqueRemarks;
                }

                var pairs = new List<(string? CAGC, string CARC)>();
                var tokens = combinedText.Split(new[] { ',', ';', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Select(t => Regex.Replace(t.Trim(), @"^(0>|Q>)+", "", RegexOptions.IgnoreCase))
                                         .Where(t => !string.IsNullOrEmpty(t));

                foreach (var item in tokens)
                {
                    if (_placeholders.Contains(item)) continue;
                    string cleanItem = item;
                    if (cleanItem.StartsWith("C0", StringComparison.OrdinalIgnoreCase)) cleanItem = "CO" + cleanItem.Substring(2);
                    else if (cleanItem.StartsWith("0A", StringComparison.OrdinalIgnoreCase)) cleanItem = "OA" + cleanItem.Substring(2);

                    var match = Regex.Match(cleanItem, @"^(CO|OA|PI|PR)[-\s]*(.+)$", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string cagc = match.Groups[1].Value.ToUpperInvariant();
                        string rest = match.Groups[2].Value.Trim();
                        if (mappings.IsCarc(rest))
                        {
                            if (!pairs.Any(p => p.CARC == rest && p.CAGC == cagc))
                                pairs.Add((cagc, rest));
                        }
                    }
                    else if (mappings.IsCarc(cleanItem))
                    {
                        if (!pairs.Any(p => p.CARC == cleanItem && p.CAGC == null))
                            pairs.Add((null, cleanItem));
                    }
                }

                foreach (var pair in pairs)
                {
                    string resolvedCagc = mappings.ResolveCagc(payerId, payerEobType, pair.CARC, pair.CAGC);

                    line.Adjustments.Add(new AdjustmentData
                    {
                        AdjustmentGroupCode = resolvedCagc,
                        AdjustmentReasonCode = pair.CARC,
                        AdjustmentAmount = 0m,
                        AdjustmentLevel = "SERVICE_LINE",
                        ClaimIdPayer = line.ClaimIdPayer,
                        ServiceLineId = line.ServiceLineId,
                        CptCode = line.CptCode
                    });
                    Log.Information("[NormalizeServiceLine] Extracted CARC '{CARC}' (Group: {CAGC}) from RemarkCodes. Created empty adjustment.", pair.CARC, resolvedCagc);
                }
            }
        }

        private List<AdjustmentData> NormalizeComplexAdjustment(
            AdjustmentData rawAdj, string payerId, string eobType, MappingConfiguration mappings,
            string lineRemarkCodes = "", string lineExplanationCodes = "")
        {

            rawAdj.AdjustmentAmount = Math.Abs(rawAdj.AdjustmentAmount);

            // 1. Basic text perfection
            rawAdj.AdjustmentLevel = PerfectText(rawAdj.AdjustmentLevel);
            rawAdj.PaymentId = PerfectText(rawAdj.PaymentId);
            rawAdj.ClaimIdPayer = PerfectText(rawAdj.ClaimIdPayer);
            rawAdj.CptCode = NormalizeProcedureCode(rawAdj.CptCode);

            // Ensure breakdown amounts are strictly positive
            if (rawAdj.DeductibleAmount.HasValue) rawAdj.DeductibleAmount = Math.Abs(rawAdj.DeductibleAmount.Value);
            if (rawAdj.CopayAmount.HasValue) rawAdj.CopayAmount = Math.Abs(rawAdj.CopayAmount.Value);
            if (rawAdj.CoinsuranceAmount.HasValue) rawAdj.CoinsuranceAmount = Math.Abs(rawAdj.CoinsuranceAmount.Value);
            if (rawAdj.OtherInsuranceAmount.HasValue) rawAdj.OtherInsuranceAmount = Math.Abs(rawAdj.OtherInsuranceAmount.Value);
            if (rawAdj.SequestrationAmount.HasValue) rawAdj.SequestrationAmount = Math.Abs(rawAdj.SequestrationAmount.Value);

            var validGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CO", "OA", "PI", "PR" };
            var specialCodeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "PR:1",   "PR1"   },  // Deductible
                { "PR:2",   "PR2"   },  // Coinsurance
                { "PR:3",   "PR3"   },  // Copayment
                { "CO:253", "CO253" },  // Sequestration
                { "OA:23",  "OA23"  }   // Other Insurance
            };

            List<string> finalCagcs = new List<string>();
            List<string> finalCarcs = new List<string>();
            List<string> finalRarcs = new List<string>();

            // Helper to collect paired CARC/CAGC tokens
            var pairs = new List<(string? CAGC, string CARC)>();
            void ExtractPairs(string input)
            {
                if (string.IsNullOrWhiteSpace(input)) return;
                var items = input.Split(new[] { ',', ';', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(t => Regex.Replace(t.Trim(), @"^(0>|Q>)+", "", RegexOptions.IgnoreCase))
                                 .Where(t => !string.IsNullOrEmpty(t));

                foreach (var item in items)
                {
                    if (_placeholders.Contains(item)) continue;
                    string cleanItem = item;
                    if (cleanItem.StartsWith("C0", StringComparison.OrdinalIgnoreCase)) cleanItem = "CO" + cleanItem.Substring(2);
                    else if (cleanItem.StartsWith("0A", StringComparison.OrdinalIgnoreCase)) cleanItem = "OA" + cleanItem.Substring(2);

                    var match = Regex.Match(cleanItem, @"^(CO|OA|PI|PR)[-\s]*(.+)$", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string cagc = match.Groups[1].Value.ToUpperInvariant();
                        string rest = match.Groups[2].Value.Trim();
                        if (mappings.IsCarc(rest))
                        {
                            if (!pairs.Any(p => p.CARC == rest && p.CAGC == cagc))
                                pairs.Add((cagc, rest));
                        }
                    }
                    else if (mappings.IsCarc(cleanItem))
                    {
                        if (!pairs.Any(p => p.CARC == cleanItem && p.CAGC == null))
                            pairs.Add((null, cleanItem));
                    }
                    else if (mappings.IsRarc(cleanItem))
                    {
                        if (!finalRarcs.Contains(cleanItem)) finalRarcs.Add(cleanItem);
                    }
                }
            }

            // Process ALL possible code sources
            ExtractPairs(PerfectText(rawAdj.AdjustmentGroupCode));
            ExtractPairs(PerfectText(rawAdj.AdjustmentReasonCode));
            ExtractPairs(PerfectText(lineRemarkCodes));
            ExtractPairs(PerfectText(lineExplanationCodes));

            decimal amount = rawAdj.AdjustmentAmount;
            var bucket = new HashSet<string>();
            var results = new List<AdjustmentData>();

            // Separate Special vs Regular CARCs
            var regularCarcs = new List<(string CARC, string CAGC)>();

            foreach (var pair in pairs)
            {
                string carc = pair.CARC;
                string? rowGroup = PerfectText(rawAdj.AdjustmentGroupCode);

                // Tier Logic: 
                // 1. Use the CAGC specifically paired with this CARC in the token (e.g., 'PR' in 'PR3')
                // 2. If 'naked' (no prefix), consult the global template. If it's a known PR code, use 'PR'.
                // 3. Otherwise, fall back to the row's group (e.g. CO).
                string? templateGroup = mappings.LookupCagcByCarc(carc);
                string? inputSourceGroup = pair.CAGC;

                if (string.IsNullOrEmpty(inputSourceGroup))
                {
                    if (templateGroup == "PR") inputSourceGroup = "PR";
                    else inputSourceGroup = string.IsNullOrEmpty(rowGroup) ? null : rowGroup;
                }

                string resolvedCagc = mappings.ResolveCagc(payerId, eobType, carc, inputSourceGroup);

                string key = $"{resolvedCagc}:{carc}";
                if (specialCodeMap.TryGetValue(key, out var bucketCode))
                {
                    bucket.Add(bucketCode);
                    Log.Information("[Bucketing] Special code '{Code}' moved to bucket.", key);
                }
                else
                {
                    if (!regularCarcs.Any(x => x.CARC == carc))
                        regularCarcs.Add((carc, resolvedCagc));
                }
            }

            // Implementation of priority rule: First regular code gets the amount
            if (regularCarcs.Any())
            {
                bool isFirst = true;
                foreach (var rc in regularCarcs)
                {
                    results.Add(new AdjustmentData
                    {
                        AdjustmentLevel = rawAdj.AdjustmentLevel,
                        PaymentId = rawAdj.PaymentId,
                        ClaimIdPayer = rawAdj.ClaimIdPayer,
                        CptCode = rawAdj.CptCode,
                        AdjustmentGroupCode = rc.CAGC,
                        AdjustmentReasonCode = rc.CARC,
                        AdjustmentAmount = isFirst ? amount : 0m,
                        Explanation = rawAdj.Explanation,
                        RemarkCode = rawAdj.RemarkCode,
                        Metadata = new Dictionary<string, string>(rawAdj.Metadata, StringComparer.OrdinalIgnoreCase)
                    });
                    isFirst = false;
                }
            }
            else if (amount != 0)
            {
                // Fallback: No regular codes found to carry the non-zero amount -> use CO-45
                results.Add(new AdjustmentData
                {
                    AdjustmentLevel = rawAdj.AdjustmentLevel,
                    PaymentId = rawAdj.PaymentId,
                    ClaimIdPayer = rawAdj.ClaimIdPayer,
                    CptCode = rawAdj.CptCode,
                    AdjustmentGroupCode = "CO",
                    AdjustmentReasonCode = "45",
                    AdjustmentAmount = amount,
                    Explanation = rawAdj.Explanation,
                    RemarkCode = rawAdj.RemarkCode,
                    Metadata = new Dictionary<string, string>(rawAdj.Metadata, StringComparer.OrdinalIgnoreCase)
                });
                Log.Information("[Bucketing] Fallback CO-45 created for amount {Amount} as no regular codes were found.", amount);
            }

            // Carry forward bucket and Excel-provided amounts to the primary result
            var firstResult = results.FirstOrDefault();
            if (firstResult != null)
            {
                firstResult.SpecialCodeBucket = bucket;
                firstResult.DeductibleAmount = rawAdj.DeductibleAmount;
                firstResult.CoinsuranceAmount = rawAdj.CoinsuranceAmount;
                firstResult.CopayAmount = rawAdj.CopayAmount;
                firstResult.OtherInsuranceAmount = rawAdj.OtherInsuranceAmount;
                firstResult.SequestrationAmount = rawAdj.SequestrationAmount;
            }
            else if (bucket.Any())
            {
                // If NO results (e.g. amount was 0 and all codes were special), create a placeholder for the bucket
                results.Add(new AdjustmentData
                {
                    AdjustmentLevel = rawAdj.AdjustmentLevel,
                    PaymentId = rawAdj.PaymentId,
                    ClaimIdPayer = rawAdj.ClaimIdPayer,
                    ServiceLineId = rawAdj.ServiceLineId,
                    CptCode = rawAdj.CptCode,
                    AdjustmentGroupCode = rawAdj.AdjustmentGroupCode,
                    AdjustmentReasonCode = rawAdj.AdjustmentReasonCode,
                    AdjustmentAmount = 0m,
                    SpecialCodeBucket = bucket,
                    DeductibleAmount = rawAdj.DeductibleAmount,
                    CoinsuranceAmount = rawAdj.CoinsuranceAmount,
                    CopayAmount = rawAdj.CopayAmount,
                    OtherInsuranceAmount = rawAdj.OtherInsuranceAmount,
                    SequestrationAmount = rawAdj.SequestrationAmount,
                    Metadata = new Dictionary<string, string>(rawAdj.Metadata, StringComparer.OrdinalIgnoreCase)
                });
            }

            return results;
        }

        private void NormalizePlb(ProviderAdjustmentData plb)
        {
            plb.PaymentId = PerfectText(plb.PaymentId);
            plb.ProviderIdentifier = PerfectText(plb.ProviderIdentifier);
            plb.FiscalPeriodDate = FormatEdiDate(plb.FiscalPeriodDate);
            plb.PlbReasonCode = PerfectText(plb.PlbReasonCode);
        }

        #region Normalization Helpers

        /// <summary>
        /// Normalizes procedure codes by removing extraction anomalies (e.g. "99214 25" -> "99214").
        /// Also removes trailing or embedded dots (e.g. "99213." -> "99213").
        /// </summary>
        /// <summary>
        /// Normalizes procedure codes by removing extraction anomalies.
        /// Handles prefix junk like "i92,99396" while preserving CPT+Modifier combos like "99213-25".
        /// </summary>
        private string NormalizeProcedureCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // 1. Run standard perfection
            string result = PerfectText(input);

            // 2. Handle comma-separated prefix anomalies (e.g. "i92,99396")
            // This happens when OCR picks up surrounding junk text.
            // Look for a 5-char code that is likely the real CPT/HCPCS
            var prefixMatch = Regex.Match(result, @"^[^,]+,\s*([A-Z0-9]{5}.*)$", RegexOptions.IgnoreCase);
            if (prefixMatch.Success)
            {
                result = prefixMatch.Groups[1].Value;
            }

            // 3. Remove trailing dots/commas that might be OCR artifacts
            result = result.Trim().TrimEnd('.', ',');

            // 4. Final safety: if it's "99214 25", ensure it's "99214-25" or similar for the splitter
            // (Splitting happens in NormalizeServiceLine)

            return result;
        }

        /// <summary>
        /// Enterprise-grade text cleaning.
        /// - Strips extra whitespace and zero-width spaces
        /// - Collapses multiple spaces
        /// - Trims dangling commas and dots
        /// </summary>
        private string PerfectText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // 1. Remove non-breaking spaces, zero-width spaces, and other weird control chars
            string result = input.Replace("\u00A0", " ")
                                 .Replace("\u200B", "") // zero-width space
                                 .Replace("\uFEFF", ""); // zero-width no-break space

            // 2. Collapse multiple spaces into one
            result = Regex.Replace(result, @"\s+", " ");

            // 3. Trim outer whitespace, commas, and dots that might be OCR artifacts (unless it's a known decimal)
            result = result.Trim().Trim(',', '.');

            if (_placeholders.Contains(result))
            {
                return string.Empty;
            }

            return result;
        }

        private string NormalizeAddress(string address, string nameToRemove)
        {
            if (string.IsNullOrWhiteSpace(address)) return string.Empty;
            address = PerfectText(address);

            if (!string.IsNullOrWhiteSpace(nameToRemove))
            {
                nameToRemove = PerfectText(nameToRemove);

                // 1. Try exact prefix match first (most reliable)
                if (address.StartsWith(nameToRemove, StringComparison.OrdinalIgnoreCase))
                {
                    address = address.Substring(nameToRemove.Length).Trim();
                    return TruncateAddress(address.TrimStart('.', ',', '/', ' ', '-'));
                }

                // 2. Fuzzy word-by-word matching
                // Normalize name into a set of words
                var nameWords = nameToRemove.Split(new[] { ' ', ',', '.', '-' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Select(w => w.ToUpperInvariant())
                                           .ToHashSet();

                var addressWords = address.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                int lastMatchIndex = -1;
                int currentMatchCount = 0;

                for (int i = 0; i < addressWords.Length; i++)
                {
                    var word = addressWords[i].Trim(',', '.', '/', ' ', '-').ToUpperInvariant();
                    if (string.IsNullOrEmpty(word)) continue;

                    if (nameWords.Contains(word))
                    {
                        lastMatchIndex = i;
                        currentMatchCount++;
                    }
                    else if (word.Length == 1 && char.IsLetter(word[0]))
                    {
                        // It's likely an initial (e.g. "L" in "Craig L Sheflin")
                        // Keep going, but don't count it as a "strong" match for the threshold
                        lastMatchIndex = i;
                    }
                    else
                    {
                        // Hit something that isn't name-like, stop matching
                        break;
                    }
                }

                // Only strip if we matched at least one significant part of the name
                if (currentMatchCount > 0 && lastMatchIndex >= 0)
                {
                    var remainingWords = addressWords.Skip(lastMatchIndex + 1).ToArray();
                    if (remainingWords.Length > 0)
                    {
                        address = string.Join(" ", remainingWords).TrimStart('.', ',', '/', ' ', '-');
                    }
                }
            }

            return TruncateAddress(address);
        }

        private string TruncateAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return string.Empty;

            // EDI N301 max length is 55
            if (address.Length > 55)
            {
                address = address.Substring(0, 55).Trim();
            }

            return address;
        }

        private (string From, string To) ParseDateRange(string rawFrom, string rawTo)
        {
            if (string.IsNullOrWhiteSpace(rawFrom))
                return (string.Empty, FormatEdiDate(rawTo));

            rawFrom = rawFrom.Trim();

            // 1. Fast path: if the string parses as a single date cleanly, it's not a range.
            if (DateTime.TryParse(rawFrom, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtSingle))
            {
                return (dtSingle.ToString("yyyyMMdd"), FormatEdiDate(rawTo));
            }

            string[] delimiters = { " to ", " through ", " thru ", " - ", "  ", "-" };

            foreach (var delimiter in delimiters)
            {
                if (rawFrom.Contains(delimiter))
                {
                    var parts = rawFrom.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries);

                    // Specific check for single date with hyphens like "07-01-2025"
                    if (delimiter == "-" && parts.Length == 3)
                    {
                        continue;
                    }

                    if (parts.Length >= 2)
                    {
                        return (FormatEdiDate(parts[0]), FormatEdiDate(parts[1]));
                    }
                }
            }

            return (FormatEdiDate(rawFrom), FormatEdiDate(rawTo));
        }

        private string FormatEdiDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            raw = raw.Trim();

            // Try standard parser first for formats like MM-DD-YYYY or MM/DD/YYYY
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt.ToString("yyyyMMdd");
            }

            var digitsOnly = Regex.Replace(raw, @"\D", ""); // Strip non-digits
            if (string.IsNullOrWhiteSpace(digitsOnly)) return string.Empty;

            if (digitsOnly.Length == 8)
            {
                // MMDDYYYY
                if (DateTime.TryParseExact(digitsOnly, "MMddyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtMmDd))
                    return dtMmDd.ToString("yyyyMMdd");

                // YYYYMMDD
                if (DateTime.TryParseExact(digitsOnly, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtYyyyMm))
                    return dtYyyyMm.ToString("yyyyMMdd");

                return digitsOnly;
            }

            if (digitsOnly.Length == 6)
            {
                // MMDDYY
                if (DateTime.TryParseExact(digitsOnly, "MMddyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtMmDdYy))
                    return dtMmDdYy.ToString("yyyyMMdd");

                // YYMMDD
                if (DateTime.TryParseExact(digitsOnly, "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtYyMmDd))
                    return dtYyMmDd.ToString("yyyyMMdd");

                // Heuristic fallback
                return "20" + digitsOnly.Substring(4, 2) + digitsOnly.Substring(0, 4);
            }

            return raw;
        }

        private (string LastName, string FirstName) ParseName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return (string.Empty, string.Empty);

            var parts = fullName.Split(new[] { ',' }, 2);
            if (parts.Length >= 2)
            {
                return (parts[0].Trim(), parts[1].Trim());
            }

            var spaceParts = fullName.Split(new[] { ' ' }, 2);
            if (spaceParts.Length >= 2)
            {
                // Assume "First Last" if no comma
                return (spaceParts[1].Trim(), spaceParts[0].Trim());
            }

            return (fullName.Trim(), string.Empty);
        }

        #endregion
    }
}

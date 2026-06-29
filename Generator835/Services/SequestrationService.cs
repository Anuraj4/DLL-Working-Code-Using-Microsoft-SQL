using System;
using System.Collections.Generic;
using System.Linq;
using Edi.Generator835.Models;
using Edi.Generator835.Services.Interfaces;
using Serilog;

namespace Edi.Generator835.Services
{
    /// <summary>
    /// Sequestration detection and resolution service.
    /// Implements a 5-method candidate strategy to identify and resolve federal sequestration reductions.
    /// </summary>
    public class SequestrationService : ISequestrationService
    {
        public enum SequestrationLevel
        {
            ServiceLine,
            ClaimLevel
        }

        // ═══════════════════════════════════════════════════════════════
        // PUBLIC ENTRY POINT
        // ═══════════════════════════════════════════════════════════════

        public void ProcessClaim(ClaimData claim, HeaderData header)
        {
            if (claim?.ServiceLines == null || claim.ServiceLines.Count == 0 || header == null)
                return;

            bool isMedicare = IsMedicare(header, claim);
            if (isMedicare) Log.Debug("[SEQ-DETECT] Claim {ClaimId}: Medicare recognized (Payer: {Payer}, Type: {Type}).", 
                claim.ClaimIdPayer, header.PayerName, claim.ClaimType);

            bool sequestrationPresent = false;
            foreach (var line in claim.ServiceLines)
            {
                decimal excelSeq = GetExcelSequestration(line);
                bool hasCo253 = HasSequestrationCode(line);

                if (excelSeq != 0 || hasCo253)
                {
                    sequestrationPresent = true;
                    break;
                }
            }

            if (!sequestrationPresent) return;

            var level = DetectSequestrationLevel(claim, out decimal identicalAmount);

            if (level == SequestrationLevel.ServiceLine)
            {
                Log.Debug("[SEQ-DETECT] Claim {ClaimId}: Sequestration is SERVICE_LINE level.", claim.ClaimIdPayer);
                HandleServiceLineFallback(claim, header);
                return;
            }

            // Step 2: Confirm Claim-Level — if ANY line matches its calculation, then it's likely Service Level
            bool confirmsClaimLevel = true;
            foreach (var line in claim.ServiceLines)
            {
                decimal excelSeq = GetExcelSequestration(line);
                if (excelSeq == 0) continue;

                if (ConfirmServiceLineSequestration(line, excelSeq, isMedicare))
                {
                    Log.Information("[SEQ-DETECT] Claim {ClaimId}: Line {SvcId} matches service-line calculation. Treating claim as SERVICE_LINE level.",
                        claim.ClaimIdPayer, line.ServiceLineId);
                    confirmsClaimLevel = false;
                    break;
                }
            }

            if (!confirmsClaimLevel)
            {
                HandleServiceLineFallback(claim, header);
                return;
            }

            Log.Information("[SEQ-DETECT] Claim {ClaimId}: CONFIRMED as CLAIM-LEVEL sequestration. Resolving per-line amounts...",
                claim.ClaimIdPayer);

            var resolvedAmounts = ResolveClaimLevelSequestration(claim, identicalAmount, header);

            if (resolvedAmounts != null && resolvedAmounts.Count > 0)
            {
                SanitizeSequestrationAmounts(claim, resolvedAmounts);
                Log.Information("[SEQ-DETECT] Claim {ClaimId}: Claim-level resolved successfully. {LineCount} lines updated.",
                    claim.ClaimIdPayer, resolvedAmounts.Count);
            }
            else
            {
                Log.Warning("[SEQ-DETECT] Claim {ClaimId}: Resolution failed or returned no results. Zeroing sequestration.", claim.ClaimIdPayer);
                ZeroOutAllSequestration(claim);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SERVICE LINE FALLBACK
        // ═══════════════════════════════════════════════════════════════

        private void HandleServiceLineFallback(ClaimData claim, HeaderData header)
        {
            bool isMedicare = IsMedicare(header, claim);
            foreach (var line in claim.ServiceLines)
            {
                decimal excelSeq = GetExcelSequestration(line);
                
                if (excelSeq == 0 && HasSequestrationCode(line))
                {
                    decimal? bestCalc = CalculateBestSequestrationCandidate(line, isMedicare);
                    if (bestCalc.HasValue && bestCalc.Value > 0)
                    {
                        ApplySequestrationToLine(line, bestCalc.Value);
                        Log.Debug("[SEQ-FALLBACK] Calculated service-line seq {Amount} for line {SvcId}", 
                            bestCalc.Value, line.ServiceLineId);
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // STEP 1 — DETECT SEQUESTRATION LEVEL
        // ═══════════════════════════════════════════════════════════════

        public SequestrationLevel DetectSequestrationLevel(ClaimData claim, out decimal identicalAmount)
        {
            identicalAmount = 0m;
            var seqAmounts = new List<decimal>();

            foreach (var line in claim.ServiceLines)
            {
                decimal lineSeq = GetExcelSequestration(line);
                if (lineSeq != 0) seqAmounts.Add(lineSeq);
            }

            if (seqAmounts.Count <= 1)
                return SequestrationLevel.ServiceLine;

            decimal firstAmount = seqAmounts[0];
            bool allSame = seqAmounts.All(a => Math.Abs(a - firstAmount) <= 0.001m);

            if (allSame)
            {
                identicalAmount = firstAmount;
                return SequestrationLevel.ClaimLevel;
            }

            return SequestrationLevel.ServiceLine;
        }

        // ═══════════════════════════════════════════════════════════════
        // STEP 2 — CONFIRM SERVICE-LINE SEQUESTRATION
        // ═══════════════════════════════════════════════════════════════

        public bool ConfirmServiceLineSequestration(ServiceLineData line, decimal excelSequestration, bool isMedicare = false)
        {
            var candidates = GetAllCandidates(line);
            if (candidates.Any(c => Math.Abs(c - excelSequestration) <= 0.01m)) return true;

            // If medicare, also allow matching the reverse engineering target specifically
            if (isMedicare)
            {
                decimal? rev = CalculateSequestrationAmount(line.LinePaidAmount);
                if (rev.HasValue && Math.Abs(rev.Value - excelSequestration) <= 0.01m) return true;
            }

            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        // STEP 3 — RESOLVE CLAIM-LEVEL SEQUESTRATION
        // ═══════════════════════════════════════════════════════════════

        public Dictionary<string, decimal> ResolveClaimLevelSequestration(ClaimData claim, decimal totalClaimSeq, HeaderData header)
        {
            if (totalClaimSeq == 0) return new Dictionary<string, decimal>();

            bool isMedicare = IsMedicare(header, claim);
            var results = new Dictionary<string, decimal>();
            var co253Lines = claim.ServiceLines.Where(HasSequestrationCode).ToList();

            if (co253Lines.Count > 0)
            {
                decimal runningSum = 0m;
                foreach (var line in co253Lines)
                {
                    decimal bestCalc = CalculateBestSequestrationCandidate(line, isMedicare) ?? 0m;
                    results[line.ServiceLineId ?? line.CptCode ?? ""] = bestCalc;
                    runningSum += bestCalc;
                }

                decimal totalRem = totalClaimSeq - runningSum;
                if (Math.Abs(totalRem) > 0)
                {
                    var lastKey = co253Lines.Last().ServiceLineId ?? co253Lines.Last().CptCode ?? "";
                    results[lastKey] += totalRem;
                }
                return results;
            }

            // Fallback: Last line that had identical amount
            ServiceLineData? lastIdenticalLine = null;
            foreach (var line in claim.ServiceLines)
            {
                if (Math.Abs(GetExcelSequestration(line) - totalClaimSeq) <= 0.001m)
                    lastIdenticalLine = line;
            }

            if (lastIdenticalLine != null)
            {
                return new Dictionary<string, decimal> { { lastIdenticalLine.ServiceLineId ?? lastIdenticalLine.CptCode ?? "", totalClaimSeq } };
            }

            var firstLine = claim.ServiceLines.FirstOrDefault();
            if (firstLine != null)
            {
                return new Dictionary<string, decimal> { { firstLine.ServiceLineId ?? firstLine.CptCode ?? "", totalClaimSeq } };
            }

            return new Dictionary<string, decimal>();
        }

        // ═══════════════════════════════════════════════════════════════
        // CANDIDATE CALCULATION ENGINE (5-Methods)
        // ═══════════════════════════════════════════════════════════════

        private List<decimal> GetAllCandidates(ServiceLineData line)
        {
            decimal allowed = line.LineAllowedAmount ?? 0m;
            decimal paid = line.LinePaidAmount;
            decimal medicareBase = GetMedicarePaymentBase(line);

            var candidates = new List<decimal>();

            // Method A: Medicare Reverse (Paid / 0.98 * 0.02)
            decimal? methodA = CalculateSequestrationAmount(paid);
            if (methodA.HasValue) candidates.Add(methodA.Value);

            // Method B: CMS Payment Base (Allowed - PR) * 0.02
            candidates.Add(Math.Round(medicareBase * 0.02m, 2));

            // Method C: Direct Paid %
            candidates.Add(Math.Round(paid * 0.02m, 2));

            // Method D: Standard Allowed %
            candidates.Add(Math.Round(allowed * 0.02m, 2));

            // Method E: Gap Logic
            candidates.Add(CalculateLineGap(line));

            return candidates.Where(c => c > 0).Distinct().ToList();
        }

        private decimal? CalculateBestSequestrationCandidate(ServiceLineData line, bool isMedicare)
        {
            var candidates = GetAllCandidates(line);
            if (candidates.Count == 0) return null;

            // Method A (Paid/0.98) logic - user requested this for ALL cases
            decimal? methodARev = CalculateSequestrationAmount(line.LinePaidAmount);
            decimal allowed2Percent = Math.Round((line.LineAllowedAmount ?? 0m) * 0.02m, 2);

            decimal target;
            if (isMedicare)
            {
                // For Medicare, priority is CMS Base (Allowed-PR) or Reverse Engineering
                decimal medicareBase = GetMedicarePaymentBase(line);
                target = methodARev ?? Math.Round(medicareBase * 0.02m, 2);
            }
            else
            {
                // For non-Medicare, favor Reverse Engineering if it's near 2% of Allowed.
                // Otherwise fall back to Gap Logic to keep the math balanced.
                decimal gap = CalculateLineGap(line);

                if (methodARev.HasValue && Math.Abs(methodARev.Value - allowed2Percent) <= 0.05m)
                    target = methodARev.Value;
                else
                    target = gap;
            }

            return candidates.OrderBy(c => Math.Abs(c - target)).FirstOrDefault();
        }

        public static bool IsMedicare(HeaderData header, ClaimData claim)
        {
            // Check Insurance Type 07 specifically (standard for Medicare Part A/B)
            if (claim != null && (claim.ClaimType == "07" || claim.ClaimType == "MB" || claim.ClaimType == "MC")) 
                return true;

            if (string.IsNullOrEmpty(header?.PayerName)) return false;
            string name = header.PayerName.ToUpperInvariant();
            return name.Contains("MEDICARE") || name.Contains("CMS");
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        private decimal GetExcelSequestration(ServiceLineData line) => 
            line.Adjustments?.FirstOrDefault(a => (a.SequestrationAmount ?? 0m) != 0)?.SequestrationAmount ?? 0m;

        private bool HasSequestrationCode(ServiceLineData line) => 
            line.Adjustments?.Any(a => (a.SpecialCodeBucket != null && a.SpecialCodeBucket.Contains("CO253")) ||
                                     (a.AdjustmentGroupCode == "CO" && a.AdjustmentReasonCode == "253")) ?? false;

        private decimal GetMedicarePaymentBase(ServiceLineData line)
        {
            decimal allowed = line.LineAllowedAmount ?? 0m;
            decimal pr = 0m;
            if (line.Adjustments != null)
            {
                foreach (var adj in line.Adjustments)
                {
                    if (adj.AdjustmentGroupCode == "PR" && adj.AdjustmentReasonCode is "1" or "2" or "3")
                        pr += Math.Abs(adj.AdjustmentAmount);
                }
            }
            return Math.Max(0m, allowed - pr);
        }

        private decimal CalculateLineGap(ServiceLineData line)
        {
            decimal allowed = line.LineAllowedAmount ?? 0m;
            decimal paid = line.LinePaidAmount;
            decimal otherAdj = 0m;

            if (line.Adjustments != null)
            {
                foreach (var adj in line.Adjustments)
                {
                    if ((adj.SequestrationAmount ?? 0) != 0) continue;
                    if (adj.AdjustmentGroupCode == "CO" && adj.AdjustmentReasonCode == "253") continue;
                    otherAdj += adj.AdjustmentAmount;
                }
            }
            return Math.Round(Math.Max(0, allowed - paid - otherAdj), 2);
        }

        private void ApplySequestrationToLine(ServiceLineData line, decimal amount)
        {
            if (line.Adjustments == null) line.Adjustments = new List<AdjustmentData>();
            var existing = line.Adjustments.FirstOrDefault(a => a.AdjustmentReasonCode == "253" || (a.SequestrationAmount ?? 0) != 0);
            if (existing != null)
            {
                existing.SequestrationAmount = amount;
                existing.AdjustmentAmount = amount;
            }
            else
            {
                line.Adjustments.Add(new AdjustmentData { SequestrationAmount = amount, AdjustmentGroupCode = "CO", AdjustmentReasonCode = "253", AdjustmentAmount = amount });
            }
        }

        public void SanitizeSequestrationAmounts(ClaimData claim, Dictionary<string, decimal> resolvedAmounts)
        {
            foreach (var line in claim.ServiceLines)
            {
                string key = line.ServiceLineId ?? line.CptCode ?? "";
                if (resolvedAmounts.TryGetValue(key, out decimal correct))
                {
                    bool done = false;
                    if (line.Adjustments != null)
                    {
                        foreach (var adj in line.Adjustments)
                        {
                            // Target CO-253 or adjustments that already have sequestration value
                            if (adj.AdjustmentReasonCode == "253" || (adj.SequestrationAmount ?? 0m) != 0m)
                            {
                                if (!done)
                                {
                                    adj.SequestrationAmount = correct;
                                    adj.AdjustmentAmount = correct; 
                                    done = true;
                                }
                                else
                                {
                                    adj.SequestrationAmount = 0m;
                                    adj.AdjustmentAmount = 0m;
                                }
                            }
                        }
                    }
                    
                    // If no existing CO-253 was found but we HAVE a correct amount to apply, create it
                    if (!done && correct != 0)
                    {
                        ApplySequestrationToLine(line, correct);
                    }
                }
                else
                {
                    ZeroOutLine(line);
                }
            }
        }

        private void ZeroOutAllSequestration(ClaimData claim) { foreach (var l in claim.ServiceLines) ZeroOutLine(l); }
        private void ZeroOutLine(ServiceLineData l) 
        { 
            if (l.Adjustments == null) return; 
            foreach (var a in l.Adjustments) 
            {
                if ((a.SequestrationAmount ?? 0m) != 0m || a.AdjustmentReasonCode == "253")
                {
                    a.SequestrationAmount = 0m; 
                    a.AdjustmentAmount = 0m;
                }
            }
        }

        public static decimal? CalculateSequestrationAmount(decimal paid)
        {
            if (paid <= 0) return null;
            decimal estimate = paid / 0.98m;
            decimal[] candidates = { Math.Round(estimate, 2), Math.Floor(estimate * 100m) / 100m, Math.Ceiling(estimate * 100m) / 100m };
            foreach (var c in candidates)
            {
                decimal[] reds = { Math.Round(c * 0.02m, 2), Math.Floor(c * 0.02m * 100m) / 100m };
                foreach (var r in reds)
                {
                    if (r > 0 && Math.Round(c - r, 2) == paid) return r;
                }
            }
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Edi.Generator835.Configuration;
using Edi.Generator835.Models;
using Edi.Generator835.Services.Interfaces;
using Serilog;

namespace Edi.Generator835.Services
{
    public class SequestrationDetectionService : ISequestrationDetectionService
    {
        public void ProcessSequestration(Edi835DataModel model, MappingConfiguration mappings)
        {
            Log.Information("[SEQUEST-DETECT] ── Starting Sequestration Detection Phase ──");

            foreach (var claim in model.Claims)
            {
                ProcessClaimSequestration(claim, mappings);
            }

            Log.Information("[SEQUEST-DETECT] Sequestration detection complete.");
        }

        private void ProcessClaimSequestration(ClaimData claim, MappingConfiguration mappings)
        {
            // Gather all non-zero sequestration amounts from service lines
            var seqLines = claim.ServiceLines
                .Select(l => new { Line = l, Amount = GetLineSequestrationAmount(l) })
                .Where(x => x.Amount > 0)
                .ToList();

            if (!seqLines.Any()) return;

            Log.Debug("[SEQUEST-DETECT] Claim {ClaimId}: Found {Count} lines with sequestration.", claim.ClaimIdPayer, seqLines.Count);

            // Group by amount to find duplicates
            var groupedAmounts = seqLines.GroupBy(x => x.Amount).ToList();

            foreach (var group in groupedAmounts)
            {
                decimal amount = group.Key;
                var lines = group.ToList();

                // Categorize each line in this group independently
                var verifiedLines = new List<ServiceLineData>();
                var unverifiedLines = new List<ServiceLineData>();

                foreach (var x in lines)
                {
                    if (IsServiceLevelSequestration(x.Line, amount))
                    {
                        verifiedLines.Add(x.Line);
                    }
                    else
                    {
                        unverifiedLines.Add(x.Line);
                    }
                }

                if (verifiedLines.Any())
                {
                    // Case 1: Some lines matched Service Level math.
                    Log.Information("[SEQUEST-DETECT] Claim {ClaimId}: Amount {Amount} verified on {VerifiedCount} lines. Clearing {UnverifiedCount} unverified duplicates.", 
                        claim.ClaimIdPayer, amount, verifiedLines.Count, unverifiedLines.Count);

                    foreach (var line in verifiedLines)
                    {
                        Log.Debug("[SEQUEST-DETECT]   Line {ServiceLineId}: Kept (Verified)", line.ServiceLineId);
                        EnsureSequestrationReason(line, amount);
                    }

                    foreach (var line in unverifiedLines)
                    {
                        Log.Warning("[SEQUEST-DETECT]   Line {ServiceLineId}: Cleared (Unverified Duplicate)", line.ServiceLineId);
                        ClearSequestration(line);
                    }
                }
                else if (lines.Count > 1)
                {
                    // Case 2: Claim Level Duplication (None matched math, but there are multiple identical values)
                    Log.Warning("[SEQUEST-DETECT] Claim {ClaimId}: Identical amount {Amount} found on {Count} lines but NONE matched Service Level math. Treating as CLAIM LEVEL.", 
                        claim.ClaimIdPayer, amount, lines.Count);

                    // Keep exactly one representative
                    var targetLine = lines.Last().Line;
                    var lineWithReason = lines.FirstOrDefault(x => x.Line.Adjustments.Any(a => a.AdjustmentReasonCode == "253"))?.Line;
                    if (lineWithReason != null) targetLine = lineWithReason;

                    Log.Information("[SEQUEST-DETECT]   Deduplicating Claim Level. Keeping Line {ServiceLineId}, clearing {ClearedCount} others.", 
                        targetLine.ServiceLineId, lines.Count - 1);

                    foreach (var x in lines)
                    {
                        if (x.Line == targetLine)
                        {
                            EnsureSequestrationReason(x.Line, amount);
                        }
                        else
                        {
                            ClearSequestration(x.Line);
                        }
                    }
                }
                else
                {
                    // Single occurrence which didn't verify. Usually keep it if reason 253 exists, but log it.
                    var line = lines.First().Line;
                    Log.Debug("[SEQUEST-DETECT] Line {ServiceLineId}: Single sequestration amount {Amount} kept (Excel-provided).", line.ServiceLineId, amount);
                    EnsureSequestrationReason(line, amount);
                }
            }
        }

        private bool IsServiceLevelSequestration(ServiceLineData line, decimal excelAmount)
        {
            // Method 1: Robust 2% (Paid-based)
            decimal? calc1 = CalculateSequestration(line.LinePaidAmount);
            if (calc1.HasValue && Math.Abs(calc1.Value - excelAmount) <= 0.01m)
            {
                Log.Debug("[SEQUEST-DETECT]   Line {LineId} Method 1 (Paid/0.98 * 0.02) matched: {Calc}", line.ServiceLineId, calc1.Value);
                return true;
            }

            // Method 2: Gap Logic (Charge-based)
            decimal charge = line.LineBilledAmount;
            decimal paid = line.LinePaidAmount;
            decimal otherAdjs = line.Adjustments
                .Where(a => a.AdjustmentReasonCode != "253" && a.SequestrationAmount == null)
                .Sum(a => a.AdjustmentAmount);
            
            decimal gap = charge - paid - otherAdjs;
            if (Math.Abs(gap - excelAmount) <= 0.01m)
            {
                Log.Debug("[SEQUEST-DETECT]   Line {LineId} Method 2 (Gap Logic) matched: {Gap}", line.ServiceLineId, gap);
                return true;
            }

            // Method 3: Adjustment Difference
            decimal allowed = line.LineAllowedAmount ?? 0m;
            decimal calc3 = Math.Abs((charge - allowed) - otherAdjs);
            if (Math.Abs(calc3 - excelAmount) <= 0.01m)
            {
                Log.Debug("[SEQUEST-DETECT]   Line {LineId} Method 3 (Adj Difference) matched: {Calc}", line.ServiceLineId, calc3);
                return true;
            }

            // Method 4: Patient Resp (PR/Allowed-based)
            decimal prTotal = line.Adjustments
                .Where(a => a.AdjustmentGroupCode == "PR" && a.AdjustmentReasonCode != "253")
                .Sum(a => a.AdjustmentAmount);
            
            decimal baseForSeq = allowed - prTotal;
            decimal calc4 = Math.Round(baseForSeq * 0.02m, 2);
            if (Math.Abs(calc4 - excelAmount) <= 0.01m)
            {
                Log.Debug("[SEQUEST-DETECT]   Line {LineId} Method 4 (PR/Allowed-based) matched: {Calc}", line.ServiceLineId, calc4);
                return true;
            }

            return false;
        }

        public decimal? CalculateSequestration(decimal paid)
        {
            if (paid <= 0) return null;

            decimal estimate = paid / 0.98m;
            decimal[] candidates = { Math.Floor(estimate * 100) / 100m, Math.Ceiling(estimate * 100) / 100m };
            
            foreach (var candidate in candidates)
            {
                decimal reduction = Math.Floor(candidate * 0.02m * 100) / 100m;
                if (Math.Abs(Math.Round(candidate - reduction, 2) - paid) <= 0.001m)
                {
                    return reduction;
                }
            }

            return Math.Round(paid / 0.98m * 0.02m, 2);
        }

        private decimal GetLineSequestrationAmount(ServiceLineData line)
        {
            decimal amountFromColumn = line.Adjustments.Sum(a => a.SequestrationAmount ?? 0m);
            if (amountFromColumn > 0) return amountFromColumn;

            decimal amountFromCarc = line.Adjustments
                .Where(a => a.AdjustmentGroupCode == "CO" && a.AdjustmentReasonCode == "253")
                .Sum(a => a.AdjustmentAmount);
            
            return amountFromCarc;
        }

        private void EnsureSequestrationReason(ServiceLineData line, decimal amount)
        {
            var existing = line.Adjustments.FirstOrDefault(a => a.AdjustmentReasonCode == "253");
            if (existing == null)
            {
                line.Adjustments.Add(new AdjustmentData
                {
                    AdjustmentLevel = "SERVICE_LINE",
                    AdjustmentGroupCode = "CO",
                    AdjustmentReasonCode = "253",
                    AdjustmentAmount = amount,
                    SequestrationAmount = amount,
                    SpecialCodeBucket = new HashSet<string> { "CO253" }
                });
            }
            else
            {
                existing.AdjustmentAmount = amount;
                existing.SequestrationAmount = amount;
                if (existing.AdjustmentGroupCode != "CO") existing.AdjustmentGroupCode = "CO";
                if (existing.SpecialCodeBucket == null) existing.SpecialCodeBucket = new HashSet<string>();
                if (!existing.SpecialCodeBucket.Contains("CO253")) existing.SpecialCodeBucket.Add("CO253");
            }
        }

        private void ClearSequestration(ServiceLineData line)
        {
            foreach (var adj in line.Adjustments)
            {
                adj.SequestrationAmount = 0m;
                if (adj.AdjustmentReasonCode == "253")
                {
                    adj.AdjustmentAmount = 0m;
                }
                if (adj.SpecialCodeBucket != null && adj.SpecialCodeBucket.Contains("CO253"))
                {
                    adj.SpecialCodeBucket.Remove("CO253");
                }
            }
            
            // Actually remove the zeroed adjustments with reason 253 to keep clean
            line.Adjustments.RemoveAll(a => a.AdjustmentReasonCode == "253" && a.AdjustmentAmount == 0m);
        }
    }
}

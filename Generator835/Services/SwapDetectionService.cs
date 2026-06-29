using System;
using System.Collections.Generic;
using System.Linq;
using Edi.Generator835.Models;
using Serilog;

namespace Edi.Generator835.Services
{
    public class SwapDetectionService
    {
        private const decimal Tolerance = 0.01m;

        /// <summary>
        /// Detects service lines where the Allowed Amount and Adjustment Amount 
        /// are likely swapped due to OCR/extraction errors.
        /// </summary>
        /// <param name="model">The populated EOB data model</param>
        /// <returns>A hashset of (ClaimIdPayer, ServiceLineId) for lines identified as swapped.</returns>
        public HashSet<(string ClaimId, string ServiceLineId)> DetectSwappedLines(Edi835DataModel model)
        {
            var swappedLines = new HashSet<(string ClaimId, string ServiceLineId)>();

            if (model.Claims == null) return swappedLines;

            foreach (var claim in model.Claims)
            {
                if (claim.ServiceLines == null) continue;

                foreach (var line in claim.ServiceLines)
                {
                    if (IsLineSwapped(claim, line))
                    {
                        var key = (claim.ClaimIdPayer ?? "", line.ServiceLineId ?? "");
                        swappedLines.Add(key);
                        
                        Log.Warning("[SWAP-DETECT] Swap detected for Claim {ClaimId}, Service Line {ServiceLineId}. " +
                                    "Billed: {Billed}, Allowed: {Allowed}, Paid: {Paid}, Raw PR: {RawPR}",
                            claim.ClaimIdPayer, line.ServiceLineId, line.LineBilledAmount, 
                            line.LineAllowedAmount ?? 0m, line.LinePaidAmount, line.LinePatientResponsibilityAmount ?? 0m);
                    }
                }
            }

            return swappedLines;
        }

        private bool IsLineSwapped(ClaimData claim, ServiceLineData line)
        {
            decimal rawPR = line.LinePatientResponsibilityAmount ?? 0m;
            
            // If PR is missing or zero, we don't have enough independent data to confidently
            // declare a swap using the PR cross-check.
            if (rawPR == 0) return false;

            decimal allowed = line.LineAllowedAmount ?? 0m;
            decimal paid = line.LinePaidAmount;
            
            // Total sequestration applied to this line
            decimal seq = line.Adjustments?.Sum(a => a.SequestrationAmount ?? 0m) ?? 0m;

            // Base calculation: PR = Allowed - Paid
            decimal calcBase = allowed - paid;

            // Condition 1: Direct match (no sequestration interference)
            if (Math.Abs(calcBase - rawPR) <= Tolerance)
                return false; // Allowed matches PR, not swapped

            // Condition 2: Sequestration was subtracted from the allowed side before calculating PR
            // PR = (Allowed - Seq) - Paid
            decimal calcSeqSubtracted = (allowed - seq) - paid;
            if (Math.Abs(calcSeqSubtracted - rawPR) <= Tolerance)
                return false; // Seq explains the difference, not swapped

            // Condition 3: Sequestration was added (e.g., CO side seq increased the PR gap)
            // PR = (Allowed + Seq) - Paid
            decimal calcSeqAdded = (allowed + seq) - paid;
            if (Math.Abs(calcSeqAdded - rawPR) <= Tolerance)
                return false; // Seq explains the difference, not swapped

            // If we reach here, PR exists but NONE of the expected math formulas match the allowed/paid values.
            // This strongly indicates the Allowed amount is wrong (likely swapped with Adjustment).
            return true;
        }
    }
}

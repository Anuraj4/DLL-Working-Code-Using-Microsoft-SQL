using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using Edi.Generator835.Models;
using Xalta.Edi.BalancingValidation.Core;
using Xalta.Edi.BalancingValidation.Interfaces;
using Edi.Generator835.Configuration;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Rule that ensures financial balancing between Billed, Paid, and Adjustments.
    ///
    /// Priority order: Excel → Calculated → CO-45 gap fill
    ///
    /// Core formulas:
    ///   PR_Total  = Allowed - Paid          (patient responsibility)
    ///   CO_Total  = Charge  - Allowed       (contractual / write-off)
    ///   Gap       = Charge  - Paid - Sum(adj)  must equal 0
    ///
    /// PR rules:
    ///   1. Excel has copay/deductible/coinsurance values
    ///      → ALWAYS fully trust Excel values (PR-3/PR-1/PR-2)
    ///   2. No Excel breakdown at all
    ///      → Calculate PR = Allowed - Paid
    ///      → PR matches CLP05 perfectly → PR-3
    ///      → PR (minus Sequestration) matches CLP05 → PR-3
    ///      → PR (plus Sequestration) matches CLP05  → PR-3
    ///      → No exact match with CLP05 → fallback PR-3
    ///
    /// CO rules:
    ///   Excel non-special CO/OA adjustments preserved in Phase 1.
    ///   Excel sequestration col → CO-253
    ///   Excel other insurance col → OA-23
    ///   Gap == 0 → balanced, done
    ///   Gap  > 0 → sequestration check first, remainder to CO-45
    ///   Gap  < 0 → reduce/remove CO-45
    ///
    /// Sequestration (CO-253) rules (Phase 4, gap > 0 only):
    ///   Medicare     → always CO-253 = exactly 2% of Allowed
    ///   Non-Medicare → GAP ≈ 2% of Allowed (within $0.01) → CO-253 = GAP
    ///                  GAP not near 2%                     → skip CO-253, CO-45 only
    ///   Guard: if CO-253 already placed in Phase 3B → skip detection entirely
    /// </summary>
    public class MathBalancingRule : IRuleDefinition
    {
        private readonly IBalancingFixer _fixer;

        public string Name => "Math Balancing Rule";
        public int Priority => 10;
        public string TargetField => "SERVICE_LINE_ADJUSTMENTS";

        public MathBalancingRule(IBalancingFixer? fixer = null)
        {
            _fixer = fixer ?? new BalancingFixer();
        }

        public bool CanExecute(RuleExecutionContext context) => context.TargetField == TargetField;

        public string? Execute(RuleExecutionContext context) => "PROCESSED";

        public void PreProcessClaimSequestration(ClaimData claim)
        {
            var allLines = claim.ServiceLines.ToList();
            if (allLines.Count == 0) return;

            // 1. Identify identical sequestration values across lines (from Excel column)
            var lineSeqData = allLines
                .Select(l => new
                {
                    Line = l,
                    SeqAmount = l.Adjustments.Sum(a => a.SequestrationAmount ?? 0m),
                    HasCo253 = l.Adjustments.Any(a => a.AdjustmentReasonCode == "253")
                })
                .Where(x => x.SeqAmount > 0)
                .ToList();

            if (lineSeqData.Count <= 1) return;

            decimal firstAmount = lineSeqData[0].SeqAmount;
            bool allIdentical = lineSeqData.All(x => x.SeqAmount == firstAmount);
            bool anyHasCo253 = lineSeqData.Any(x => x.HasCo253);

            // Confirmation: identical amount AND co253 code not already present for these lines
            if (allIdentical && !anyHasCo253)
            {
                Log.Information("[SEQ-DETECT] Claim-level sequestration confirmed for Claim {ClaimId}. Total={Total}",
                    claim.ClaimIdPayer, firstAmount);

                var nonZeroPaidLines = allLines.Where(x => x.LinePaidAmount > 0).ToList();

                if (nonZeroPaidLines.Count > 0)
                {
                    // 2. Calculate distribution candidates
                    decimal totalToDistribute = firstAmount;
                    var distributions = new List<decimal>();
                    foreach (var line in nonZeroPaidLines)
                    {
                        decimal paid = line.LinePaidAmount;
                        decimal target2Percent = paid * 0.02m;

                        // Formulas as requested:
                        // 1. (Paid / 0.98) * 0.02
                        // 2. Ceiling(Paid * 0.02)
                        // 3. Paid * 0.02
                        decimal c1 = Math.Round(paid / 0.98m * 0.02m, 2);
                        decimal c2 = Math.Round(Math.Ceiling(paid * 0.02m * 100m) / 100m, 2);
                        decimal c3 = Math.Round(paid * 0.02m, 2);

                        // Selection: closest to 2% target
                        decimal best = c1;
                        if (Math.Abs(c2 - target2Percent) < Math.Abs(best - target2Percent)) best = c2;
                        if (Math.Abs(c3 - target2Percent) < Math.Abs(best - target2Percent)) best = c3;

                        distributions.Add(best);
                    }

                    decimal distributedSum = distributions.Sum();
                    decimal difference = totalToDistribute - distributedSum;

                    // 3. Equal Residue Distribution (only to CO-253 lines, which are all non-zero paid lines here)
                    if (Math.Abs(difference) > 0)
                    {
                        int count = distributions.Count;
                        decimal adjustmentPerLine = Math.Truncate((difference / count) * 100) / 100;
                        decimal remainder = difference - (adjustmentPerLine * count);

                        for (int i = 0; i < count; i++) distributions[i] += adjustmentPerLine;
                        distributions[count - 1] += remainder;
                    }

                    // 4. Update Model: Zero out old duplicated amounts, set new distributed amounts
                    foreach (var d in lineSeqData)
                    {
                        foreach (var adj in d.Line.Adjustments) adj.SequestrationAmount = 0;
                    }

                    for (int i = 0; i < nonZeroPaidLines.Count; i++)
                    {
                        var line = nonZeroPaidLines[i];
                        var targetAdj = line.Adjustments.FirstOrDefault();
                        if (targetAdj == null)
                        {
                            targetAdj = CreateAdj(line, "CO", "253", distributions[i]);
                            line.Adjustments.Add(targetAdj);
                        }
                        else
                        {
                            targetAdj.AdjustmentGroupCode = "CO";
                            targetAdj.AdjustmentReasonCode = "253";
                            targetAdj.SequestrationAmount = distributions[i];
                            // Preserve existing metadata if this was already a sequestration row
                        }
                        Log.Information("[SEQ-DETECT] Line {CPT}: Distributed seq assigned: {A}", line.CptCode, distributions[i]);
                    }
                }
                else
                {
                    // Fallback: add total to last line if no lines have paid amount
                    Log.Information("[SEQ-DETECT] No non-zero paid lines found. Assigning total {Total} to last line.", firstAmount);

                    foreach (var d in lineSeqData)
                    {
                        foreach (var adj in d.Line.Adjustments) adj.SequestrationAmount = 0;
                    }

                    var lastLine = allLines.Last();
                    var targetAdj = lastLine.Adjustments.FirstOrDefault();
                    if (targetAdj == null)
                    {
                        targetAdj = CreateAdj(lastLine, "CO", "253", firstAmount);
                        lastLine.Adjustments.Add(targetAdj);
                    }
                    else
                    {
                        targetAdj.AdjustmentGroupCode = "CO";
                        targetAdj.AdjustmentReasonCode = "253";
                        targetAdj.SequestrationAmount = firstAmount;
                    }
                }
            }
        }

        public List<AdjustmentData> BalanceServiceLine(
            ServiceLineData line,
            ClaimData claim,
            HeaderData header,
            MappingConfiguration mappings,
            bool performGapResolution = true)
        {
            decimal charge = line.LineBilledAmount;
            decimal paid = line.LinePaidAmount;
            decimal allowed = line.LineAllowedAmount ?? 0m;

            string payerId = header?.PayerId ?? string.Empty;
            string eobType = header?.PayerEobType ?? string.Empty;

            decimal calculatedPrTotal = allowed - paid;    // PR_Total = Allowed − Paid
            decimal calculatedCoTotal = charge - allowed; // CO_Total = Charge  − Allowed

            // CLP segment patient responsibility (7th element of CLP)
            decimal clpPatientResponsibility = claim.PatientResponsibilityAmount ?? 0m;

            bool isMedicare = claim.ClaimType != null &&
                              claim.ClaimType.IndexOf("Medicare", StringComparison.OrdinalIgnoreCase) >= 0;

            Log.Information(
                "[Balancing] CPT={CPT} | Charge={C} Allowed={A} Paid={P} | " +
                "PR_Calc={PR} CO_Calc={CO} CLP_PR={CPR} Medicare={M}",
                line.CptCode, charge, allowed, paid,
                calculatedPrTotal, calculatedCoTotal, clpPatientResponsibility, isMedicare);

            var originals = line.Adjustments.ToList();
            Log.Information("[BALANCER-DEBUG] --- Balancing ServiceLine {ID} (CPT: {CPT}) ---", line.ServiceLineId, line.CptCode);
            foreach (var adj in originals)
            {
                string bucketStr = adj.SpecialCodeBucket != null ? string.Join(",", adj.SpecialCodeBucket) : "null";
                Log.Information("[BALANCER-DEBUG] Inbound Adj: Group='{G}', Reason='{R}', Amount={A}, Bucket=[{B}]",
                    adj.AdjustmentGroupCode, adj.AdjustmentReasonCode, adj.AdjustmentAmount, bucketStr);
            }
            var finalAdjustments = new List<AdjustmentData>();

            // ═══════════════════════════════════════════════════════════════
            // PHASE 1 — Preserve non-special raw adjustments
            //
            // Keep every raw adjustment EXCEPT the codes we own and recalculate.
            // Owned: PR-1/2/3, OA-23, CO-45, CO-253
            // ═══════════════════════════════════════════════════════════════
            foreach (var adj in originals)
            {
                if (string.IsNullOrEmpty(adj.AdjustmentGroupCode) ||
                    string.IsNullOrEmpty(adj.AdjustmentReasonCode))
                    continue;

                bool isOwned =
                    (adj.AdjustmentGroupCode == "PR" && adj.AdjustmentReasonCode is "1" or "2" or "3") ||
                    (adj.AdjustmentGroupCode == "OA" && adj.AdjustmentReasonCode == "23") ||
                    (adj.AdjustmentGroupCode == "CO" && adj.AdjustmentReasonCode == "253");

                if (!isOwned)
                {
                    finalAdjustments.Add(adj);
                    Log.Debug("[Phase1] Kept raw: {G}-{R} = {A}",
                        adj.AdjustmentGroupCode, adj.AdjustmentReasonCode, adj.AdjustmentAmount);
                }
            }

            // ═══════════════════════════════════════════════════════════════
            // PHASE 2 — Read Excel column amounts
            // ═══════════════════════════════════════════════════════════════
            decimal excelDeductible = originals.Sum(a => a.DeductibleAmount ?? 0m);
            decimal excelCoinsurance = originals.Sum(a => a.CoinsuranceAmount ?? 0m);
            decimal excelCopay = originals.Sum(a => a.CopayAmount ?? 0m);
            decimal excelSequestration = originals.Sum(a => a.SequestrationAmount ?? 0m);
            decimal excelOtherIns = originals.Sum(a => a.OtherInsuranceAmount ?? 0m);

            bool hasAnyPrBreakdown = excelCopay != 0 || excelCoinsurance != 0 || excelDeductible != 0;

            Log.Debug(
                "[Phase2] Excel — Ded={D} CoIns={CI} Copay={CP} Seq={S} OtherIns={OI} | HasBreakdown={B}",
                excelDeductible, excelCoinsurance, excelCopay,
                excelSequestration, excelOtherIns, hasAnyPrBreakdown);

            // ═══════════════════════════════════════════════════════════════
            // PHASE 3A — Patient Responsibility (PR side)
            //
            // Step 1: Excel breakdown (only if any of copay/coinsurance/deductible present)
            //         validate sum vs Allowed-Paid
            //           match    → apply PR-3 copay, PR-1 ded, PR-2 coins
            //           mismatch → warn, apply PR-2 with calculated total
            //
            // Step 2: Always runs after Step 1
            //         Catches any prRemaining using CLP PR logic:
            //           prRemaining == CLP PR (within $0.01) → PR-3 only
            //           prRemaining  > CLP PR                → PR-3 = CLP PR, PR-1 = gap
            //           no CLP PR (zero)                     → PR-3 catch-all
            // ═══════════════════════════════════════════════════════════════

            // ── Step 1: Excel breakdown ──
            if (hasAnyPrBreakdown)
            {
                // User requirement: "Copay, coinsurance and deductible have value in excel --> then use all excel value not calculations"
                if (excelCopay != 0)
                {
                    string gc = "PR";
                    finalAdjustments.Add(CreateAdj(line, gc, "3", excelCopay));
                    Log.Debug("[Phase3A-Excel] PR-3 Copay (Excel): {A}", excelCopay);
                }

                if (excelDeductible != 0)
                {
                    string gc = "PR";
                    finalAdjustments.Add(CreateAdj(line, gc, "1", excelDeductible));
                    Log.Debug("[Phase3A-Excel] PR-1 Deductible (Excel): {A}", excelDeductible);
                }

                if (excelCoinsurance != 0)
                {
                    string gc = "PR";
                    finalAdjustments.Add(CreateAdj(line, gc, "2", excelCoinsurance));
                    Log.Debug("[Phase3A-Excel] PR-2 Coinsurance (Excel): {A}", excelCoinsurance);
                }
            }
            else
            {
                // ── Step 2: No breakdown in Excel, calculate PR ──
                decimal calcPr = allowed - paid;

                // Subtract any non-owned PR already emitted in Phase 1 (e.g., PR-49, etc.)
                decimal prEmitted = finalAdjustments
                    .Where(a => a.AdjustmentGroupCode == "PR")
                    .Sum(a => a.AdjustmentAmount);

                calcPr -= prEmitted;

                // Only calculate and add PR if a PR group code or bucketed PR code exists in the input
                bool hasPrReference = originals.Any(a =>
                    a.AdjustmentGroupCode == "PR" ||
                    (a.SpecialCodeBucket != null && a.SpecialCodeBucket.Any(b => b.StartsWith("PR"))));
                Log.Information("[BALANCER-DEBUG] PR Balancing Check: calcPr={CalcPR}, hasPrReference={HasRef}", calcPr, hasPrReference);

                if (calcPr > 0.001m && hasPrReference)
                {
                    string defaultPrReason = "3";

                    // Priority 1: Check for standard PR codes (1, 2, 3) in buckets or raw adjustments
                    if (originals.Any(a => (a.AdjustmentGroupCode == "PR" && a.AdjustmentReasonCode == "1") ||
                                           (a.SpecialCodeBucket != null && a.SpecialCodeBucket.Contains("PR1"))))
                    {
                        defaultPrReason = "1";
                        Log.Information("[BALANCER-DEBUG] Found PR1 in bucket or input. Using Reason 1.");
                    }
                    else if (originals.Any(a => (a.AdjustmentGroupCode == "PR" && a.AdjustmentReasonCode == "2") ||
                                                (a.SpecialCodeBucket != null && a.SpecialCodeBucket.Contains("PR2"))))
                    {
                        defaultPrReason = "2";
                        Log.Information("[BALANCER-DEBUG] Found PR2 in bucket or input. Using Reason 2.");
                    }
                    else if (originals.Any(a => (a.AdjustmentGroupCode == "PR" && a.AdjustmentReasonCode == "3") ||
                                                (a.SpecialCodeBucket != null && a.SpecialCodeBucket.Contains("PR3"))))
                    {
                        defaultPrReason = "3";
                        Log.Information("[BALANCER-DEBUG] Found PR3 in bucket or input. Using Reason 3.");
                    }
                    else
                    {
                        // Priority 2: Check for any existing PR adjustment that was provided in input (e.g. PR243)
                        var existingPr = originals.FirstOrDefault(a => a.AdjustmentGroupCode == "PR" && !string.IsNullOrEmpty(a.AdjustmentReasonCode));
                        if (existingPr != null)
                        {
                            defaultPrReason = existingPr.AdjustmentReasonCode;
                            Log.Information("[Phase3A] Using first available PR code '{Reason}' from input as balancing target.", defaultPrReason);
                        }
                    }

                    if (clpPatientResponsibility > 0)
                    {
                        if (Math.Abs(calcPr - clpPatientResponsibility) <= 0.01m)
                        {
                            string gc = "PR";
                            finalAdjustments.Add(CreateAdj(line, gc, defaultPrReason, calcPr));
                            Log.Debug("[Phase3A-Calc] PR-{Reason} calculated exactly matches CLP05 PR: {A}", defaultPrReason, calcPr);
                        }
                        else if (excelSequestration != 0 && Math.Abs((calcPr - excelSequestration) - clpPatientResponsibility) <= 0.01m)
                        {
                            string gc = "PR";
                            finalAdjustments.Add(CreateAdj(line, gc, defaultPrReason, calcPr - excelSequestration));
                            Log.Debug("[Phase3A-Calc] PR-{Reason} calculated (minus seq) matches CLP05 PR: {A}", defaultPrReason, calcPr - excelSequestration);
                        }
                        else if (excelSequestration != 0 && Math.Abs((calcPr + excelSequestration) - clpPatientResponsibility) <= 0.01m)
                        {
                            string gc = "PR";
                            finalAdjustments.Add(CreateAdj(line, gc, defaultPrReason, calcPr + excelSequestration));
                            Log.Debug("[Phase3A-Calc] PR-{Reason} calculated (plus seq) matches CLP05 PR: {A}", defaultPrReason, calcPr + excelSequestration);
                        }
                        else
                        {
                            string gc = "PR";
                            finalAdjustments.Add(CreateAdj(line, gc, defaultPrReason, calcPr));
                            Log.Debug("[Phase3A-Calc] PR-{Reason} calculated fallback (Allowed - Paid): {A}", defaultPrReason, calcPr);
                        }
                    }
                    else
                    {
                        // No CLP PR available → PR-3 catch-all
                        string gc = "PR";
                        finalAdjustments.Add(CreateAdj(line, gc, defaultPrReason, calcPr));
                        Log.Debug("[Phase3A-Calc] PR-{Reason} catch-all (no CLP PR available): {A}", defaultPrReason, calcPr);
                    }
                }
            }

            // ═══════════════════════════════════════════════════════════════
            // PHASE 3B — Contractual / Other (CO/OA side)
            //
            // CO-253 from Excel column only (Medicare auto-detect in Phase 4)
            // OA-23  from Excel column
            // Validate total CO/OA emitted vs Charge-Allowed — Phase 4 resolves gap.
            // ═══════════════════════════════════════════════════════════════
            if (excelSequestration != 0)
            {
                string gc = "CO";
                finalAdjustments.Add(CreateAdj(line, gc, "253", excelSequestration));
                Log.Debug("[Phase3B] CO-253 Sequestration (Excel): {A}", excelSequestration);
            }

            if (excelOtherIns != 0)
            {
                string gc = "OA";
                finalAdjustments.Add(CreateAdj(line, gc, "23", excelOtherIns));
                Log.Debug("[Phase3B] OA-23 Other Insurance (Excel): {A}", excelOtherIns);
            }

            decimal coOaEmitted = finalAdjustments
                .Where(a => a.AdjustmentGroupCode is "CO" or "OA" or "PI")
                .Sum(a => a.AdjustmentAmount);

            Log.Debug("[Phase3B] CO/OA/PI emitted={E} calc target={T} remaining={R}",
                coOaEmitted, calculatedCoTotal, calculatedCoTotal - coOaEmitted);

            if (Math.Abs(calculatedCoTotal - coOaEmitted) > 0.01m)
                Log.Warning(
                    "[Phase3B] Excel CO total ({E}) does not match Charge−Allowed ({T}). Phase 4 will resolve.",
                    coOaEmitted, calculatedCoTotal);

            if (performGapResolution)
            {
                // ═══════════════════════════════════════════════════════════════
                // PHASE 4 — GAP RESOLUTION
                //
                // True end-to-end gap: Charge − Paid − Sum(all adj)
                //
                //   Gap == 0 → balanced, done
                //   Gap  > 0 → sequestration check first, remainder to CO-45
                //   Gap  < 0 → surplus: reduce/remove existing CO-45
                //
                // Sequestration (Gap > 0 only, CO-253 not already placed):
                //   Medicare     → CO-253 = exactly 2% of Allowed, remainder → CO-45
                //   Non-Medicare → GAP ≈ 2% of Allowed → CO-253 = GAP
                //                  GAP not near 2%     → skip CO-253, full gap → CO-45
                // ═══════════════════════════════════════════════════════════════
                decimal totalEmitted = finalAdjustments.Sum(a => a.AdjustmentAmount);
                decimal gap = charge - paid - totalEmitted;

                Log.Debug("[Phase4] Charge={C} Paid={P} TotalAdj={T} Gap={G}",
                    charge, paid, totalEmitted, gap);

                if (Math.Abs(gap) <= 0.001m)
                {
                    Log.Information("[Phase4] Gap == 0 — balanced, no further adjustments needed.");
                }
                else if (gap > 0)
                {
                    bool hasCo253Already = finalAdjustments
                        .Any(a => a.AdjustmentGroupCode == "CO" && a.AdjustmentReasonCode == "253");
                    decimal seqThreshold = paid > 0 ? Math.Round((paid / 0.98m) * 0.02m, 2) : 0;
                    bool has253InOriginals = originals.Any(a => a.AdjustmentReasonCode == "253" && a.ServiceLineId == line.ServiceLineId);

                    if (!hasCo253Already && seqThreshold > 0 && has253InOriginals)
                    {
                        if (isMedicare)
                        {
                            // Medicare: CO-253 always exactly 2% of Allowed
                            string gc = "CO";
                            finalAdjustments.Add(CreateAdj(line, gc, "253", seqThreshold));
                            gap -= seqThreshold;
                            Log.Information(
                                "[Phase4] Medicare — CO-253 forced: (Paid / 0.98) * 0.02 = {S}",
                                seqThreshold);
                        }
                        else if (Math.Abs(gap - seqThreshold) <= 0.01m)
                        {
                            // Non-Medicare: gap reverse-engineered as sequestration
                            string gc = "CO";
                            finalAdjustments.Add(CreateAdj(line, gc, "253", gap));
                            Log.Information(
                                "[Phase4] Sequestration auto-detected: Row has code 253 and GAP={G} ≈ 2% of Allowed. CO-253 emitted.",
                                gap);
                            gap = 0;
                        }
                        else
                        {
                            // Non-Medicare: gap does not match 2% — not sequestration, fall to CO-45
                            Log.Debug(
                                "[Phase4] GAP={G} not near 2% of Allowed={A} — skipping CO-253, using CO-45.",
                                gap, allowed);
                        }
                    }

                    // The "Absorber" catches whatever gap remains after sequestration (or full gap if no seq)
                    if (Math.Abs(gap) > 0.001m)
                    {
                        var absorber = finalAdjustments.FirstOrDefault(a => a.AdjustmentGroupCode == "CO" && a.AdjustmentReasonCode == "45")
                                    ?? finalAdjustments.FirstOrDefault(a => a.AdjustmentGroupCode == "PI")
                                    ?? finalAdjustments.FirstOrDefault(a => a.AdjustmentGroupCode == "CO" && a.AdjustmentReasonCode != "253")
                                    ?? finalAdjustments.FirstOrDefault(a => a.AdjustmentGroupCode == "OA" && a.AdjustmentReasonCode != "23");

                        if (absorber != null)
                        {
                            absorber.AdjustmentAmount += gap;
                            Log.Information("[Phase4] {G}-{R} increased by {Gap} → {T}",
                                absorber.AdjustmentGroupCode, absorber.AdjustmentReasonCode, gap, absorber.AdjustmentAmount);
                        }
                        else
                        {
                            string gc = "CO";
                            finalAdjustments.Add(CreateAdj(line, gc, "45", gap));
                            Log.Information("[Phase4] CO-45 created: {G}", gap);
                        }
                    }
                }
                else
                {
                    // gap < 0 — surplus: reduce existing adjustment
                    var absorber = finalAdjustments.FirstOrDefault(a => a.AdjustmentGroupCode == "CO" && a.AdjustmentReasonCode == "45")
                                ?? finalAdjustments.FirstOrDefault(a => a.AdjustmentGroupCode == "PI")
                                ?? finalAdjustments.FirstOrDefault(a => a.AdjustmentGroupCode == "CO" && a.AdjustmentReasonCode != "253")
                                ?? finalAdjustments.FirstOrDefault(a => a.AdjustmentGroupCode == "OA" && a.AdjustmentReasonCode != "23");

                    if (absorber != null)
                    {
                        absorber.AdjustmentAmount += gap; // gap is negative → reduces
                        Log.Warning("[Phase4] {G}-{R} reduced by {Gap} → new total {T}",
                            absorber.AdjustmentGroupCode, absorber.AdjustmentReasonCode, gap, absorber.AdjustmentAmount);

                        if (Math.Abs(absorber.AdjustmentAmount) <= 0.001m)
                        {
                            finalAdjustments.Remove(absorber);
                            Log.Warning("[Phase4] {G}-{R} zeroed out and removed.", absorber.AdjustmentGroupCode, absorber.AdjustmentReasonCode);
                        }
                    }
                    else
                    {
                        Log.Error(
                            "[Phase4] Surplus gap={G} but no suitable adjustment exists to reduce. " +
                            "Check PR or non-special adjustments for over-coverage.",
                            gap);
                    }
                }

                // ═══════════════════════════════════════════════════════════════
                // GUARDRAIL — Floating-point rounding dust only (< $0.001)
                // A residual larger than $0.001 after Phase 4 is a data error.
                // ═══════════════════════════════════════════════════════════════
                decimal finalGap = charge - paid - finalAdjustments.Sum(a => a.AdjustmentAmount);

                if (Math.Abs(finalGap) > 0.001m)
                {
                    Log.Error(
                        "[GUARDRAIL] Unexpectedly large residual after Phase 4: {G}. Data error.",
                        finalGap);
                }
                else if (Math.Abs(finalGap) > 0)
                {
                    var absorber = finalAdjustments.FirstOrDefault(a => a.AdjustmentGroupCode == "CO" && a.AdjustmentReasonCode == "45")
                                ?? finalAdjustments.FirstOrDefault(a => a.AdjustmentGroupCode == "PI")
                                ?? finalAdjustments.FirstOrDefault(a => a.AdjustmentGroupCode == "CO" && a.AdjustmentReasonCode != "253")
                                ?? finalAdjustments.FirstOrDefault(a => a.AdjustmentGroupCode != "PR");

                    if (absorber != null)
                    {
                        absorber.AdjustmentAmount += finalGap;
                        if (Math.Abs(absorber.AdjustmentAmount) <= 0.001m)
                            finalAdjustments.Remove(absorber);
                    }
                    else
                    {
                        string gc = "CO";
                        finalAdjustments.Add(CreateAdj(line, gc, "45", finalGap));
                    }
                    Log.Warning("[GUARDRAIL] Rounding dust absorbed: {D}", finalGap);
                }
            }
            else
            {
                Log.Information("[Balancing] Gap resolution skipped as requested (Mapping-only mode).");
            }

            // ── Cleanup: remove zero-amount placeholders ──
            finalAdjustments.RemoveAll(a =>
                Math.Abs(a.AdjustmentAmount) < 0.001m &&
                string.IsNullOrEmpty(a.AdjustmentReasonCode));

            // ── Final verification ──
            decimal finalSum = finalAdjustments.Sum(a => a.AdjustmentAmount);
            if (!_fixer.IsBalanced(charge, paid, finalSum))
                Log.Error("[FAILURE] Charge={C} Paid={P} AdjSum={S} Diff={D}",
                    charge, paid, finalSum, charge - paid - finalSum);
            else
                Log.Information("[SUCCESS] Balanced: AdjSum={S} ✓", finalSum);

            return finalAdjustments;
        }

        private AdjustmentData CreateAdj(
            ServiceLineData line, string group, string reason, decimal amount)
        {
            var template = line.Adjustments.FirstOrDefault();
            return new AdjustmentData
            {
                AdjustmentGroupCode = group,
                AdjustmentReasonCode = reason,
                AdjustmentAmount = amount,
                AdjustmentLevel = "SERVICE_LINE",
                PaymentId = template?.PaymentId ?? string.Empty,
                ClaimIdPayer = template?.ClaimIdPayer ?? string.Empty,
                ServiceLineId = line.ServiceLineId,
                CptCode = line.CptCode,
                DeductibleAmount = template?.DeductibleAmount,
                CoinsuranceAmount = template?.CoinsuranceAmount,
                CopayAmount = template?.CopayAmount,
                OtherInsuranceAmount = template?.OtherInsuranceAmount,
                SequestrationAmount = template?.SequestrationAmount,
                Quantity = template?.Quantity,
                Explanation = template?.Explanation ?? string.Empty,
                RemarkCode = template?.RemarkCode ?? string.Empty
            };
        }
    }
}
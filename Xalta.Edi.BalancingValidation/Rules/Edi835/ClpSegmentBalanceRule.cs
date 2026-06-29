using System;
using System.Collections.Generic;
using System.Linq;
using EdiFabric.Templates.Hipaa5010;
using Xalta.Edi.BalancingValidation.Core;
using Xalta.Edi.BalancingValidation.Interfaces;
using Xalta.Edi.BalancingValidation.Constants;

namespace Xalta.Edi.BalancingValidation.Rules.Edi835
{
    public class ClpSegmentBalanceRule : IBalancingRule<TS835>
    {
        private readonly IBalancingFixer _fixer;
        public string RuleName => "Claim Level Balancing (CLP)";

        public ClpSegmentBalanceRule(IBalancingFixer? fixer = null)
        {
            _fixer = fixer ?? new BalancingFixer();
        }

        public EdiValidationResult Validate(TS835 transaction)
        {
            var errors = new List<EdiSegmentError>();

            if (transaction.Loop2000 == null) return new EdiValidationResult(true, errors);

            foreach (var loop2000 in transaction.Loop2000)
            {
                if (loop2000.Loop2100 == null) continue;

                foreach (var loop2100 in loop2000.Loop2100)
                {
                    var clp = loop2100.CLP_ClaimPaymentInformation;
                    if (clp == null) continue;

                    if (!decimal.TryParse(clp.TotalClaimChargeAmount_03, out var billed)) continue;
                    decimal.TryParse(clp.ClaimPaymentAmount_04, out var paid);

                    decimal claimLevelAdjustments = 0;
                    decimal prTotal = 0;
                    decimal co253Amount = 0;

                    if (loop2100.CAS_ClaimsAdjustment != null)
                    {
                        foreach (var cas in loop2100.CAS_ClaimsAdjustment)
                        {
                            decimal casSum = _fixer.SumCasAmounts(
                                cas.AdjustmentAmount_03, cas.AdjustmentAmount_06, cas.AdjustmentAmount_09,
                                cas.AdjustmentAmount_12, cas.AdjustmentAmount_15, cas.AdjustmentAmount_18);

                            claimLevelAdjustments += casSum;

                            if (cas.ClaimAdjustmentGroupCode_01 == "PR")
                            {
                                prTotal += casSum;
                            }

                            void Check253(string code, string amount)
                            {
                                if (code == "253" && decimal.TryParse(amount, out var val)) co253Amount += val;
                            }

                            Check253(cas.AdjustmentReasonCode_02, cas.AdjustmentAmount_03);
                            Check253(cas.AdjustmentReasonCode_05, cas.AdjustmentAmount_06);
                            Check253(cas.AdjustmentReasonCode_08, cas.AdjustmentAmount_09);
                            Check253(cas.AdjustmentReasonCode_11, cas.AdjustmentAmount_12);
                            Check253(cas.AdjustmentReasonCode_14, cas.AdjustmentAmount_15);
                            Check253(cas.AdjustmentReasonCode_17, cas.AdjustmentAmount_18);
                        }
                    }

                    decimal serviceLineAdjustments = 0;
                    if (loop2100.Loop2110 != null)
                    {
                        foreach (var loop2110 in loop2100.Loop2110)
                        {
                            if (loop2110.CAS_ServiceAdjustment != null)
                            {
                                foreach (var cas in loop2110.CAS_ServiceAdjustment)
                                {
                                    decimal casSum = _fixer.SumCasAmounts(
                                        cas.AdjustmentAmount_03, cas.AdjustmentAmount_06, cas.AdjustmentAmount_09,
                                        cas.AdjustmentAmount_12, cas.AdjustmentAmount_15, cas.AdjustmentAmount_18);

                                    serviceLineAdjustments += casSum;

                                    if (cas.ClaimAdjustmentGroupCode_01 == "PR")
                                    {
                                        prTotal += casSum;
                                    }

                                    void Check253(string code, string amount)
                                    {
                                        if (code == "253" && decimal.TryParse(amount, out var val)) co253Amount += val;
                                    }

                                    Check253(cas.AdjustmentReasonCode_02, cas.AdjustmentAmount_03);
                                    Check253(cas.AdjustmentReasonCode_05, cas.AdjustmentAmount_06);
                                    Check253(cas.AdjustmentReasonCode_08, cas.AdjustmentAmount_09);
                                    Check253(cas.AdjustmentReasonCode_11, cas.AdjustmentAmount_12);
                                    Check253(cas.AdjustmentReasonCode_14, cas.AdjustmentAmount_15);
                                    Check253(cas.AdjustmentReasonCode_17, cas.AdjustmentAmount_18);
                                }
                            }
                        }
                    }

                    var totalAdjustments = claimLevelAdjustments + serviceLineAdjustments;

                    if (!_fixer.IsBalanced(billed, paid, totalAdjustments))
                    {
                        var calculatedPaid = _fixer.CalculateClp04(billed, claimLevelAdjustments, serviceLineAdjustments);
                        var claimId = clp.PatientControlNumber_01 ?? "Unknown";

                        errors.Add(new EdiSegmentError(
                            SegmentName: "CLP",
                            SegmentPosition: -1,
                            Loop: "2100",
                            ElementErrors: new List<EdiElementError>
                            {
                                new EdiElementError(
                                    FieldKey: "CLP04",
                                    ElementPosition: 4,
                                    ElementCode: "782",
                                    BusinessName: "Claim Payment Amount",
                                    ErrorType: "BAL_CLP_001",
                                    Message: string.Format(ValidationMessages.ClaimBalancingFailed, claimId, paid, billed, totalAdjustments, calculatedPaid, prTotal, co253Amount),
                                    Value: paid.ToString()
                                )
                            }
                        ));

                    }
                }
            }

            return new EdiValidationResult(!errors.Any(), errors);
        }
    }
}

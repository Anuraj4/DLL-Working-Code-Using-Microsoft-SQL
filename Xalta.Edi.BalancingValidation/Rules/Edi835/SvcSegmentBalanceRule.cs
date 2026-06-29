using System;
using System.Collections.Generic;
using System.Linq;
using EdiFabric.Templates.Hipaa5010;
using Xalta.Edi.BalancingValidation.Core;
using Xalta.Edi.BalancingValidation.Interfaces;
using Xalta.Edi.BalancingValidation.Constants;

namespace Xalta.Edi.BalancingValidation.Rules.Edi835
{
    public class SvcSegmentBalanceRule : IBalancingRule<TS835>
    {
        private readonly IBalancingFixer _fixer;
        public string RuleName => "Service Claim Level Balancing (SVC)";

        public SvcSegmentBalanceRule(IBalancingFixer? fixer = null)
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
                    if (loop2100.Loop2110 == null) continue;

                    foreach (var loop2110 in loop2100.Loop2110)
                    {
                        var svc = loop2110.SVC_ServicePaymentInformation;
                        if (svc == null) continue;

                        if (!decimal.TryParse(svc.LineItemChargeAmount_02, out var billed)) continue;
                        decimal.TryParse(svc.MonetaryAmount_03, out var paid);

                        decimal adjustments = 0;
                        decimal prTotal = 0;
                        decimal co253Amount = 0;

                        if (loop2110.CAS_ServiceAdjustment != null)
                        {
                            foreach (var cas in loop2110.CAS_ServiceAdjustment)
                            {
                                decimal casSum = _fixer.SumCasAmounts(
                                    cas.AdjustmentAmount_03, cas.AdjustmentAmount_06, cas.AdjustmentAmount_09,
                                    cas.AdjustmentAmount_12, cas.AdjustmentAmount_15, cas.AdjustmentAmount_18);

                                adjustments += casSum;

                                if (cas.ClaimAdjustmentGroupCode_01 == "PR")
                                {
                                    prTotal += casSum;
                                }

                                // Check specifically for 253 (Sequestration) logic
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

                        if (!_fixer.IsBalanced(billed, paid, adjustments))
                        {
                            var calculatedPaid = _fixer.CalculateSvc03(billed, adjustments);
                            string proc = svc.CompositeMedicalProcedureIdentifier_01?.ProcedureCode_02 ?? "Unknown";
                            var claimId = loop2100.CLP_ClaimPaymentInformation?.PatientControlNumber_01 ?? "Unknown";

                            errors.Add(new EdiSegmentError(
                                SegmentName: "SVC",
                                SegmentPosition: -1,
                                Loop: "2110",
                                ElementErrors: new List<EdiElementError>
                                {
                                    new EdiElementError(
                                        FieldKey: "SVC03",
                                        ElementPosition: 3,
                                        ElementCode: "782",
                                        BusinessName: "Monetary Amount",
                                        ErrorType: "BAL_SVC_001",
                                        Message: string.Format(ValidationMessages.ServiceLineBalancingFailed, proc, paid, billed, adjustments, calculatedPaid, prTotal, co253Amount),
                                        Value: paid.ToString()
                                    )
                                }
                            ));
                        }
                    }
                }
            }

            return new EdiValidationResult(!errors.Any(), errors);
        }
    }
}

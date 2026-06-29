using System;
using System.Collections.Generic;
using System.Linq;
using EdiFabric.Templates.Hipaa5010;
using Xalta.Edi.BalancingValidation.Core;
using Xalta.Edi.BalancingValidation.Interfaces;
using Xalta.Edi.BalancingValidation.Constants;

namespace Xalta.Edi.BalancingValidation.Rules.Edi835
{
    public class TransactionBalanceRule : IBalancingRule<TS835>
    {
        private readonly IBalancingFixer _fixer;
        public string RuleName => "Transaction Level Balancing";

        public TransactionBalanceRule(IBalancingFixer? fixer = null)
        {
            _fixer = fixer ?? new BalancingFixer();
        }

        public EdiValidationResult Validate(TS835 transaction)
        {
            var errors = new List<EdiSegmentError>();

            if (transaction.BPR_FinancialInformation == null)
            {
                errors.Add(new EdiSegmentError(
                    SegmentName: "BPR",
                    SegmentPosition: 1, // BPR is usually position 1
                    Loop: "N/A",
                    ElementErrors: new List<EdiElementError>
                    {
                        new EdiElementError(
                            FieldKey: "BPR",
                            ElementPosition: 0,
                            ElementCode: "BPR",
                            BusinessName: "Financial Information",
                            ErrorType: "BAL_TRN_001",
                            Message: ValidationMessages.BprMissing,
                            Value: null
                        )
                    }
                ));
                return new EdiValidationResult(false, errors);
            }

            if (!decimal.TryParse(transaction.BPR_FinancialInformation.TotalPremiumPaymentAmount_02, out var declaredTotal))
            {
                errors.Add(new EdiSegmentError(
                    SegmentName: "BPR",
                    SegmentPosition: 1,
                    Loop: "N/A",
                    ElementErrors: new List<EdiElementError>
                    {
                        new EdiElementError(
                            FieldKey: "BPR02",
                            ElementPosition: 2,
                            ElementCode: "782",
                            BusinessName: "Total Premium Payment Amount",
                            ErrorType: "BAL_TRN_002",
                            Message: ValidationMessages.BprInvalidAmount,
                            Value: transaction.BPR_FinancialInformation.TotalPremiumPaymentAmount_02
                        )
                    }
                ));
                return new EdiValidationResult(false, errors);
            }

            decimal totalClaimsPaid = 0;
            if (transaction.Loop2000 != null)
            {
                foreach (var loop2000 in transaction.Loop2000)
                {
                    if (loop2000.Loop2100 != null)
                    {
                        foreach (var loop2100 in loop2000.Loop2100)
                        {
                            if (loop2100.CLP_ClaimPaymentInformation != null &&
                                decimal.TryParse(loop2100.CLP_ClaimPaymentInformation.ClaimPaymentAmount_04, out var claimPaid))
                            {
                                totalClaimsPaid += claimPaid;
                            }
                        }
                    }
                }
            }

            decimal totalAdjustments = 0;
            if (transaction.PLB_ProviderAdjustment != null)
            {
                foreach (var plb in transaction.PLB_ProviderAdjustment)
                {
                    totalAdjustments += _fixer.SumPlbAmounts(
                        plb.ProviderAdjustmentAmount_04, plb.ProviderAdjustmentAmount_06,
                        plb.ProviderAdjustmentAmount_08, plb.ProviderAdjustmentAmount_10,
                        plb.ProviderAdjustmentAmount_12, plb.ProviderAdjustmentAmount_14);
                }
            }

            if (!_fixer.IsBalanced(totalClaimsPaid, declaredTotal, totalAdjustments))
            {
                var calculatedTotal = _fixer.CalculateBpr02(totalClaimsPaid, totalAdjustments);
                errors.Add(new EdiSegmentError(
                    SegmentName: "BPR",
                    SegmentPosition: 1,
                    Loop: "N/A",
                    ElementErrors: new List<EdiElementError>
                    {
                        new EdiElementError(
                            FieldKey: "BPR02",
                            ElementPosition: 2,
                            ElementCode: "782",
                            BusinessName: "Total Premium Payment Amount",
                            ErrorType: "BAL_TRN_003",
                            Message: string.Format(ValidationMessages.TransactionBalancingFailed, declaredTotal, totalClaimsPaid, totalAdjustments, calculatedTotal),
                            Value: declaredTotal.ToString()
                        )
                    }
                ));
            }

            return new EdiValidationResult(!errors.Any(), errors);
        }
    }
}

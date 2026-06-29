using System;
using Xalta.Edi.BalancingValidation.Interfaces;

namespace Xalta.Edi.BalancingValidation.Core
{
    public class BalancingFixer : IBalancingFixer
    {
        public decimal Alpha { get; set; } = 0.01m;

        public decimal CalculateSvc03(decimal billed, decimal adjustments)
        {
            return billed - adjustments;
        }

        public decimal CalculateClp04(decimal billed, decimal claimAdjustments, decimal serviceAdjustments)
        {
            return billed - (claimAdjustments + serviceAdjustments);
        }

        public decimal CalculateBpr02(decimal totalClaimsPaid, decimal totalProviderAdjustments)
        {
            return totalClaimsPaid - totalProviderAdjustments;
        }

        public bool IsBalanced(decimal billed, decimal paid, decimal adjustments, decimal? tolerance = null)
        {
            return Math.Abs(billed - adjustments - paid) <= (tolerance ?? Alpha);
        }

        public decimal SumCasAmounts(params string?[] amounts)
        {
            return Sum(amounts);
        }

        public decimal SumPlbAmounts(params string?[] amounts)
        {
            return Sum(amounts);
        }

        private decimal Sum(string?[] amounts)
        {
            decimal total = 0;
            foreach (var amt in amounts)
            {
                if (decimal.TryParse(amt, out var d))
                    total += d;
            }
            return total;
        }
    }
}

namespace Xalta.Edi.BalancingValidation.Interfaces
{
    /// <summary>
    /// Service for calculating required amounts to ensure EDI 835 segments are balanced.
    /// Can be used both for proactive fixing (generation) and reactive validation.
    /// </summary>
    public interface IBalancingFixer
    {
        /// <summary>
        /// Formula 1: 835 SVC02 - Sum 2110 CAS = SVC03
        /// </summary>
        decimal CalculateSvc03(decimal billed, decimal adjustments);

        /// <summary>
        /// Formula 2: 835 CLP03 - (Sum 2100 CAS + Sum 2110 CAS) = CLP04
        /// </summary>
        decimal CalculateClp04(decimal billed, decimal claimAdjustments, decimal serviceAdjustments);

        /// <summary>
        /// Formula 3: 835 Sum 2100 CLP04 - Sum PLB = BPR02
        /// </summary>
        decimal CalculateBpr02(decimal totalClaimsPaid, decimal totalProviderAdjustments);

        /// <summary>
        /// Tolerance for balancing checks. Default is 0.01.
        /// </summary>
        decimal Alpha { get; set; }

        /// <summary>
        /// Checks if the amounts are balanced within a tolerance.
        /// </summary>
        bool IsBalanced(decimal billed, decimal paid, decimal adjustments, decimal? tolerance = null);

        /// <summary>
        /// Sums all adjustment amounts in a CAS segment.
        /// </summary>
        decimal SumCasAmounts(params string?[] amounts);

        /// <summary>
        /// Sums all adjustment amounts in a PLB segment.
        /// </summary>
        decimal SumPlbAmounts(params string?[] amounts);
    }
}

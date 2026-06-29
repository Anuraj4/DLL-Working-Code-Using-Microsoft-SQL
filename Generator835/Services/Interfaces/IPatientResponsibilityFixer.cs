using Edi.Generator835.Models;

namespace Edi.Generator835.Services.Interfaces
{
    /// <summary>
    /// Service to validate and fix patient responsibility amounts at the service line level.
    /// </summary>
    public interface IPatientResponsibilityFixer
    {
        /// <summary>
        /// Validates and fixes LinePatientResponsibilityAmount based on breakdown columns (Copay, Coinsurance, Deductible).
        /// </summary>
        /// <param name="model">The full 835 data model.</param>
        void FixPatientResponsibility(Edi835DataModel model);
    }
}

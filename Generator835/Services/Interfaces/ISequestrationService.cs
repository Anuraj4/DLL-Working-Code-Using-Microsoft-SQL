using Edi.Generator835.Models;

namespace Edi.Generator835.Services.Interfaces
{
    /// <summary>
    /// Detects whether sequestration is claim-level or service-line level,
    /// and resolves claim-level sequestration into correct per-line amounts.
    /// </summary>
    public interface ISequestrationService
    {
        /// <summary>
        /// Orchestrates the full 4-step sequestration detection and resolution for a claim.
        /// Only modifies SequestrationAmount on adjustments when claim-level duplication is detected.
        /// Service-line level sequestration passes through unchanged.
        /// </summary>
        void ProcessClaim(ClaimData claim, HeaderData header);
    }
}

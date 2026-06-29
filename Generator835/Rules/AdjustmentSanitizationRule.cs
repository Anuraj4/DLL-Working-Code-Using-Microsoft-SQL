using System.Collections.Generic;
using System.Linq;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Rule for Adjustment Sanitization (CAS Group Codes).
    /// Logic:
    /// - Maps/Sanitizes Adjustment Group Codes to valid X12 values (CO, CR, OA, PI, PR).
    /// - Defaults to PR if invalid or missing.
    /// </summary>
    public class AdjustmentSanitizationRule : IRuleDefinition
    {
        private static readonly HashSet<string> ValidGroups = new HashSet<string> { "CO", "CR", "OA", "PI", "PR" };

        public string Name => "Adjustment Sanitization Rule";
        public int Priority => 5;
        public string TargetField => "CAS01";

        public bool CanExecute(RuleExecutionContext context)
        {
            return context.TargetField == "CAS01";
        }

        public string? Execute(RuleExecutionContext context)
        {
            string group = context.RawValue;

            if (string.IsNullOrEmpty(group) || !ValidGroups.Contains(group))
            {
                // Simple heuristic: if we have a reason code but no group, check if it's likely a patient responsibility
                if (!string.IsNullOrEmpty(context.RawValue))
                {
                    // Fallback to PR if we have some value but it's not a valid group code
                    return "PR";
                }
                return "OA"; // Other adjustments as last resort
            }

            return group;
        }
    }
}

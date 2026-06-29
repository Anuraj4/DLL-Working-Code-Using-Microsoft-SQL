using System;
using System.Linq;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Rule to determine the communication qualifier (PER03) and value (PER04) 
    /// for the Payer (Loop 1000A) based on the PayerCommunicationNumber column.
    /// </summary>
    public class PayerCommunicationRule : IRuleDefinition
    {
        public string Name => "Payer Communication Rule";

        public int Priority => 10;

        public string TargetField => "*"; // Handles both PER03_PR and PER04_PR

        public bool CanExecute(RuleExecutionContext context)
        {
            return (context.TargetField == "PER03_PR" || context.TargetField == "PER04_PR") &&
                   context.RowData.TryGetValue("PayerCommunicationNumber", out var val) &&
                   !string.IsNullOrWhiteSpace(val);
        }

        public string? Execute(RuleExecutionContext context)
        {
            if (!context.RowData.TryGetValue("PayerCommunicationNumber", out var commValue) || string.IsNullOrWhiteSpace(commValue))
            {
                return null;
            }

            if (context.TargetField == "PER03_PR")
            {
                // Logic: 
                // 1. If contains '@' -> EM (Email)
                // 2. If starts with http or www -> UR (URL)
                // 3. If contains any digits -> TE (Telephone)
                // 4. Default -> TE

                if (commValue.Contains("@")) return "EM";
                if (commValue.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    commValue.IndexOf("www", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "UR";
                }

                // Simple check for phone: if it has digits, treat as TE
                if (commValue.Any(char.IsDigit)) return "TE";

                return "TE";
            }

            if (context.TargetField == "PER04_PR")
            {
                return commValue;
            }

            return null;
        }
    }
}

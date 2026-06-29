using System;
using System.Linq;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Rule for TRN03: Payer Identifier.
    /// Requirement: Must be a '1' followed by the payer's EIN (or TIN).
    /// Format: String (AN), Length: Exactly 10.
    /// </summary>
    public class Trn03Rule : IRuleDefinition
    {
        public string Name => "TRN03 Payer Identifier Rule";

        public int Priority => 10;

        public string TargetField => "TRN03";

        public bool CanExecute(RuleExecutionContext context)
        {
            return context.TargetField == "TRN03";
        }

        public string? Execute(RuleExecutionContext context)
        {
            // Logic: 1 + PayerID (EIN/TIN), forced to 10 chars
            string? payerId = null;

            // 1. Prioritize Matched Payer from Registry
            if (context.Mappings.MatchedPayer != null)
            {
                payerId = context.Mappings.GetPayerTin(context.Mappings.MatchedPayer);
            }

            // 2. Fallback to RawValue (which is 'tin' passed from Generator)
            if (string.IsNullOrWhiteSpace(payerId))
            {
                payerId = context.RawValue;
                if (!string.IsNullOrEmpty(payerId)) Serilog.Log.Debug("[Trn03Rule] Using RawValue (tin) from generator: '{PayerID}'", payerId);
            }

            // 3. Fallback to RowData
            if (string.IsNullOrWhiteSpace(payerId))
            {
                if (context.RowData.TryGetValue("PayerID", out var pid))
                {
                    payerId = pid;
                }
            }

            if (string.IsNullOrWhiteSpace(payerId) || string.Equals(payerId, "Fallback", StringComparison.OrdinalIgnoreCase))
            {
                return "1999999999";
            }

            // Standard says "1" followed by 9-digit EIN.
            // If already 10 digits and starts with "1", assume it's already formatted.
            if (payerId != null && payerId.Length == 10 && payerId.StartsWith("1"))
            {
                return payerId;
            }

            // Otherwise, strip non-numeric or extra chars to get 9 digits
            string cleanId = new string(payerId!.Where(char.IsDigit).ToArray());
            if (cleanId.Length > 9) cleanId = cleanId.Substring(0, 9);

            string result = "1" + cleanId.PadRight(9, '0');

            return result;
        }
    }
}

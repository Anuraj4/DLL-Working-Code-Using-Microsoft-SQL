using System;
using Serilog;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Determines the qualifier for LQ segment (LQ01).
    /// Returns "RX" for NCPDP codes, "HE" for standard Remark codes.
    /// </summary>
    public class RemarkCodeQualifierRule : IRuleDefinition
    {
        public string Name => "Remark-Code-Qualifier-Rule";

        public int Priority => 10;

        public string TargetField => "LQ01";

        public bool CanExecute(RuleExecutionContext context) => context.TargetField == TargetField;

        public string? Execute(RuleExecutionContext context)
        {
            string remarkCode = context.RawValue;
            if (string.IsNullOrWhiteSpace(remarkCode)) return null;

            if (context.Mappings.IsNcpdp(remarkCode))
            {
                Log.Debug("[RemarkCodeQualifierRule] Code '{RemarkCode}' identified as NCPDP. Using RX qualifier.", remarkCode);
                return "RX";
            }

            // Default to HE for everything else
            return "HE";
        }
    }
}

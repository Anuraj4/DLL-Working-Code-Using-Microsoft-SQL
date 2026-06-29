namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Rule for Contact Information (PER segments).
    /// Logic:
    /// - Determines Name, Qualifier (TE, FX, EM), and Number for contacts.
    /// </summary>
    public class ContactInfoRule : IRuleDefinition
    {
        public string Name => "Contact Info Rule";
        public int Priority => 5;
        public string TargetField => "*";

        public bool CanExecute(RuleExecutionContext context)
        {
            return context.TargetField.StartsWith("PER02_") || context.TargetField.StartsWith("PER04_");
        }

        public string? Execute(RuleExecutionContext context)
        {
            if (context.TargetField == "PER02_PayerTechnicalContact")
                return !string.IsNullOrEmpty(context.RawValue) ? context.RawValue : "TECH SUPPORT";

            if (context.TargetField == "PER02_PayerBusinessContact")
                return !string.IsNullOrEmpty(context.RawValue) ? context.RawValue : "";

            if (context.TargetField == "PER04_PayerTechnicalContact" || context.TargetField == "PER04_PayerBusinessContact" || context.TargetField == "PER04_ClaimContact")
            {
                // If rawValue is just a placeholder, use global default
                if (string.IsNullOrEmpty(context.RawValue))
                {
                    var globalSupport = context.Mappings.GetSetting("GlobalSupportNumber");
                    return !string.IsNullOrEmpty(globalSupport) ? globalSupport : "";
                }
                return context.RawValue;
            }

            return null;
        }
    }
}

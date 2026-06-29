namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Rule for Additional Identification (REF segments).
    /// Targets REF01 (Qualifier) and REF02 (Identifier).
    /// Logic:
    /// - For Payee (Loop 1000B): Returns TJ for TaxID.
    /// - Can be extended for other REF types.
    /// </summary>
    public class AdditionalIdentificationRule : IRuleDefinition
    {
        public string Name => "Additional Identification Rule";
        public int Priority => 5;
        public string TargetField => "*";

        public bool CanExecute(RuleExecutionContext context)
        {
            return context.TargetField == "REF01_Payee" || context.TargetField == "REF02_Payee";
        }

        public string? Execute(RuleExecutionContext context)
        {
            var header = context.DataModel.Header;

            if (context.TargetField == "REF01_Payee")
            {
                return !string.IsNullOrEmpty(header.ProviderTaxId) ? "TJ" : null;
            }
            if (context.TargetField == "REF02_Payee")
            {
                return header.ProviderTaxId;
            }

            return null;
        }
    }
}

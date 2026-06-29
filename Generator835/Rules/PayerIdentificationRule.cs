namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Rule for Payer Identification (N103/N104 in Loop 1000A).
    /// Logic:
    /// - If Payer ID is available -> N103="XV", N104=PayerID.
    /// - Otherwise, returns null to allow fallback.
    /// </summary>
    public class PayerIdentificationRule : IRuleDefinition
    {
        public string Name => "Payer Identification Rule";
        public int Priority => 5;
        public string TargetField => "*";

        public bool CanExecute(RuleExecutionContext context)
        {
            return context.TargetField == "N103_Payer" || context.TargetField == "N104_Payer";
        }

        public string? Execute(RuleExecutionContext context)
        {
            var header = context.DataModel.Header;
            string payerId = context.RawValue; // Expected to be passed as rawValue from generator

            if (string.IsNullOrWhiteSpace(payerId)) payerId = header.PayerId;

            if (context.TargetField == "N103_Payer")
            {
                return !string.IsNullOrEmpty(payerId) ? "XV" : null;
            }
            if (context.TargetField == "N104_Payer")
            {
                return !string.IsNullOrEmpty(payerId) ? payerId : null;
            }

            return null;
        }
    }
}

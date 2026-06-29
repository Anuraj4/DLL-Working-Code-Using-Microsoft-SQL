namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Global rule to lookup settings from 'default_payment_settings' sheet.
    /// Acts as a hierarchical fallback (Matched Payer -> Global Fallback).
    /// </summary>
    public class PaymentSettingRule : IRuleDefinition
    {
        public string Name => "Payment Setting Rule";
        public int Priority => 80; // High priority, runs before FallbackDefault but after specific rules
        public string TargetField => "*";

        public bool CanExecute(RuleExecutionContext context)
        {
            // Only apply to fields that aren't already determined and are likely to be in payment settings (BPR, etc.)
            return true;
        }

        public string? Execute(RuleExecutionContext context)
        {
            // Hierarchical lookup: Matched Payer Row -> Fallback Row
            var value = context.Mappings.GetPaymentSetting(context.TargetField);
            return !string.IsNullOrEmpty(value) ? value : null;
        }
    }
}

using System.Collections.Generic;
using Edi.Generator835.Configuration;
using Xalta.Edi.CodeCrossWalk.Interfaces;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Central place to define and register the sequence of rules.
    /// This makes the "code flow" explicit and easy to audit in one file.
    /// </summary>
    public static class RuleRegistry
    {
        public static RuleExecutionEngine CreateEngine(MappingConfiguration mappings)
        {
            var rules = new List<IRuleDefinition>
            {
                // -- Priority 5: High-Impact / Calculation Rules --
                new ClaimFillingIndicatorRule(),
                new CarcCagcRule(),
                new RenderingProviderRule(),
                new MathBalancingRule(),

                // -- Priority 10: Mapping Lookups --
                new CsvLookupRule("BPR01", "TransactionHandlingCodes", 10),
                new CsvLookupRule("BPR04", "PaymentMethods", 10),
                new CsvLookupRule("CLP02", "ClaimStatusCodes", 10),
                new CsvLookupRule("N104_PR", "PayerIDs", 10),
                new Trn03Rule(),

                // -- Priority 20: Conditional Logic --
                ConditionalLogicRule.WhenFieldContains("BPR04", "PaymentMethod", "Virtual Card",
                    mappings.GetDefault("BPR04_VirtualCardFallback", "ACH"), 20),

                // -- Priority 30-50: Calculated Segment Rules --
                new TransactionHandlingRule(),
                new PayerCommunicationRule(),
                new Svc01QualifierRule(), // Priority of this is more than Svc01ProcedureCodeRule always ensure it.
                new Svc01ProcedureCodeRule(),
                new RemarkCodeQualifierRule(),
                new PayeeIdentificationRule(),
                new PayerIdentificationRule(),
                new PatientIdentificationRule(),
                new PaymentSettingRule(),
                new ServiceDateRule(),
                new AdditionalIdentificationRule(),
                new AdjustmentSanitizationRule(),
                new ContactInfoRule(),

                // -- Priority 100: Global Fallbacks --
                new FallbackDefaultRule("ISA01", mappings.GetDefault("ISA01", "00"), 100),
                new FallbackDefaultRule("ISA03", mappings.GetDefault("ISA03", "00"), 100),
                new FallbackDefaultRule("GS01", mappings.GetDefault("GS01", "HP"), 100),
                new FallbackDefaultRule("ST01", mappings.GetDefault("ST01", "835"), 100),
                new FallbackDefaultRule("BPR03", mappings.GetDefault("BPR03", "C"), 100),
                new FallbackDefaultRule("BPR05", mappings.GetDefault("BPR05", "CCP"), 100),
                new FallbackDefaultRule("BPR06", mappings.GetDefault("BPR06", "01"), 100),
                new FallbackDefaultRule("BPR12", mappings.GetDefault("BPR12", "01"), 100),
                new FallbackDefaultRule("*", null, 150) // Absolute catch-all
            };

            return new RuleExecutionEngine(rules);
        }
    }
}

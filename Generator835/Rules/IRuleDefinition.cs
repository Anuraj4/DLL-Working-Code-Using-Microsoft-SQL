using System.Collections.Generic;
using Edi.Generator835.Configuration;
using Edi.Generator835.Context;
using Edi.Generator835.Models;
using Xalta.Edi.CodeCrossWalk.Interfaces;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Core rule interface. Every rule that transforms/populates an EDI field implements this.
    /// Adding a new rule = implementing this interface + registering it. No engine changes needed.
    /// </summary>
    public interface IRuleDefinition
    {
        /// <summary>Human-readable name for logging/debugging.</summary>
        string Name { get; }

        /// <summary>Execution order. Lower priority = executes first.</summary>
        int Priority { get; }

        /// <summary>
        /// Which EDI field this rule targets (e.g., "BPR04", "CLP02", "CAS01").
        /// Use "*" for rules that apply to any field.
        /// </summary>
        string TargetField { get; }

        /// <summary>
        /// Determines if this rule can execute in the current context.
        /// </summary>
        bool CanExecute(RuleExecutionContext context);

        /// <summary>
        /// Executes the rule and returns the determined value.
        /// Return null to indicate "no match — pass to next rule".
        /// </summary>
        string? Execute(RuleExecutionContext context);
    }

    /// <summary>
    /// Rich context passed to each rule during execution.
    /// Contains everything a rule needs to make its determination.
    /// </summary>
    public class RuleExecutionContext
    {
        /// <summary>The EDI field being determined (e.g., "BPR04").</summary>
        public string TargetField { get; set; } = string.Empty;

        /// <summary>The raw value from the EOB data.</summary>
        public string RawValue { get; set; } = string.Empty;

        /// <summary>All columns from the current Excel row.</summary>
        public Dictionary<string, string> RowData { get; set; } = new Dictionary<string, string>();

        /// <summary>All loaded CSV mappings.</summary>
        public MappingConfiguration Mappings { get; set; } = new MappingConfiguration();

        /// <summary>Generation context with control numbers and state.</summary>
        public GenerationContext GenerationContext { get; set; } = null!;

        /// <summary>The full data model being processed.</summary>
        public Edi835DataModel DataModel { get; set; } = null!;

        /// <summary>Extensible cross-walk service for external master mappings.</summary>
        public ICodeCrossWalkService? CrossWalkService { get; set; }

        /// <summary>Sheet context: which Excel sheet this data came from.</summary>
        public string SheetName { get; set; } = string.Empty;

        /// <summary>Payer identifier for payer-specific logic.</summary>
        public string? PayerId { get; set; }

        /// <summary>Claim ID for claim-level context.</summary>
        public string? ClaimId { get; set; }
    }
}

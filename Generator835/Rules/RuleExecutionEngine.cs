using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Executes an ordered array of rules for each target field.
    /// First matching rule wins (Chain of Responsibility pattern).
    /// Open-Closed: add new rules without modifying this class.
    /// </summary>
    public class RuleExecutionEngine
    {
        private readonly List<IRuleDefinition> _rules;

        public RuleExecutionEngine(IEnumerable<IRuleDefinition> rules)
        {
            _rules = rules.OrderBy(r => r.Priority).ToList();
        }

        /// <summary>
        /// Gets the ordered list of registered rules (read-only for inspection).
        /// </summary>
        public IReadOnlyList<IRuleDefinition> Rules => _rules.AsReadOnly();

        /// <summary>
        /// Determine the value for a specific field by running all applicable rules in priority order.
        /// </summary>
        public string DetermineValue(string targetField, RuleExecutionContext context)
        {
            var resolution = ResolveValue(targetField, context);
            return resolution.FinalValue;
        }

        /// <summary>
        /// Resolves a field value and returns a rich resolution object with a full audit trail.
        /// </summary>
        public RuleResolution ResolveValue(string targetField, RuleExecutionContext context)
        {
            context.TargetField = targetField;
            var resolution = new RuleResolution { TargetField = targetField };

            Log.Debug("--- Resolving Field: {TargetField} (Raw: '{RawValue}') ---", targetField, context.RawValue);

            var applicableRules = _rules.Where(r => r.TargetField == targetField || r.TargetField == "*").ToList();

            foreach (var rule in applicableRules)
            {
                var step = new RuleTraceStep { RuleName = rule.Name, Priority = rule.Priority };

                if (!rule.CanExecute(context))
                {
                    step.Status = RuleStepStatus.Skipped;
                    step.Message = "Condition not met";
                    resolution.Trace.Add(step);
                    continue;
                }

                var result = rule.Execute(context);
                if (result != null)
                {
                    step.Status = RuleStepStatus.Matched;
                    step.ResultValue = result;
                    step.Message = "Rule Matched";
                    resolution.Trace.Add(step);

                    resolution.FinalValue = result;
                    resolution.MatchedRuleName = rule.Name;

                    Log.Information("[RuleEngine] {TargetField} -> Matched '{RuleName}' (Priority {Priority}). Result: '{Result}'",
                        targetField, rule.Name, rule.Priority, result);

                    return resolution;
                }
                else
                {
                    step.Status = RuleStepStatus.NoMatch;
                    step.Message = "Rule returned null";
                    resolution.Trace.Add(step);
                }
            }

            // Fallback: return raw value as-is
            resolution.FinalValue = context.RawValue;
            resolution.MatchedRuleName = "None (Fallback to Raw)";

            Log.Debug("[RuleEngine] {TargetField} -> No rule matched. Using RawValue: '{RawValue}'", targetField, context.RawValue);

            return resolution;
        }

        /// <summary>
        /// Add a rule dynamically at runtime (e.g., for payer-specific overrides).
        /// Re-sorts the rule list by priority.
        /// </summary>
        public void AddRule(IRuleDefinition rule)
        {
            _rules.Add(rule);
            _rules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }
    }
}

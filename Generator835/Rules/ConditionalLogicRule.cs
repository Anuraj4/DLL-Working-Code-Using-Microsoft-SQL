using System;
using System.Collections.Generic;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Applies conditional if/then logic based on other field values in the row.
    /// E.g., "If PaymentMethod contains 'Virtual Card' then BPR04 = 'NON'"
    /// </summary>
    public class ConditionalLogicRule : IRuleDefinition
    {
        private readonly string _targetField;
        private readonly Func<RuleExecutionContext, bool> _condition;
        private readonly Func<RuleExecutionContext, string> _valueProducer;

        public string Name { get; }
        public int Priority { get; }
        public string TargetField => _targetField;

        /// <param name="name">Human-readable name for this rule instance.</param>
        /// <param name="targetField">EDI field this rule targets.</param>
        /// <param name="condition">Predicate that determines if this rule applies.</param>
        /// <param name="valueProducer">Function that produces the output value.</param>
        /// <param name="priority">Execution priority (default 30).</param>
        public ConditionalLogicRule(
            string name,
            string targetField,
            Func<RuleExecutionContext, bool> condition,
            Func<RuleExecutionContext, string> valueProducer,
            int priority = 30)
        {
            Name = name;
            _targetField = targetField;
            _condition = condition;
            _valueProducer = valueProducer;
            Priority = priority;
        }

        public bool CanExecute(RuleExecutionContext context) => _condition(context);

        public string? Execute(RuleExecutionContext context) => _valueProducer(context);

        #region Factory Methods for Common Patterns

        /// <summary>
        /// Creates a rule: if RowData[checkField] contains the given text, return constant value.
        /// </summary>
        public static ConditionalLogicRule WhenFieldContains(
            string targetField, string checkField, string containsText, string resultValue, int priority = 30)
        {
            return new ConditionalLogicRule(
                $"When({checkField} contains '{containsText}') → {resultValue}",
                targetField,
                ctx => ctx.RowData.TryGetValue(checkField, out var val) &&
                       val != null && val.IndexOf(containsText, StringComparison.OrdinalIgnoreCase) >= 0,
                _ => resultValue,
                priority);
        }

        /// <summary>
        /// Creates a rule: if RowData[checkField] equals the given text, return constant value.
        /// </summary>
        public static ConditionalLogicRule WhenFieldEquals(
            string targetField, string checkField, string equalsText, string resultValue, int priority = 30)
        {
            return new ConditionalLogicRule(
                $"When({checkField} == '{equalsText}') → {resultValue}",
                targetField,
                ctx => ctx.RowData.TryGetValue(checkField, out var val) &&
                       string.Equals(val, equalsText, StringComparison.OrdinalIgnoreCase),
                _ => resultValue,
                priority);
        }

        /// <summary>
        /// Creates a rule: if raw value is empty/null, return a fallback value.
        /// </summary>
        public static ConditionalLogicRule WhenEmpty(
            string targetField, string fallbackValue, int priority = 50)
        {
            return new ConditionalLogicRule(
                $"WhenEmpty({targetField}) → {fallbackValue}",
                targetField,
                ctx => string.IsNullOrWhiteSpace(ctx.RawValue),
                _ => fallbackValue,
                priority);
        }

        #endregion
    }
}

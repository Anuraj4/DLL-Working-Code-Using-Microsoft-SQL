using System;
using System.Text.RegularExpressions;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Applies regex pattern matching and transformation to the raw value.
    /// Useful for extracting structured data from free-text EOB fields.
    /// </summary>
    public class RegexTransformRule : IRuleDefinition
    {
        private readonly string _targetField;
        private readonly string _pattern;
        private readonly string _replacement;
        private readonly Regex _regex;

        public string Name { get; }
        public int Priority { get; }
        public string TargetField => _targetField;

        /// <param name="name">Human-readable name.</param>
        /// <param name="targetField">EDI field this rule targets.</param>
        /// <param name="pattern">Regex pattern to match against the raw value.</param>
        /// <param name="replacement">Replacement pattern (supports $1, $2, etc.).</param>
        /// <param name="priority">Execution priority (default 50).</param>
        public RegexTransformRule(
            string name, string targetField, string pattern, string replacement, int priority = 50)
        {
            Name = name;
            _targetField = targetField;
            _pattern = pattern;
            _replacement = replacement;
            _regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Priority = priority;
        }

        public bool CanExecute(RuleExecutionContext context)
        {
            return !string.IsNullOrWhiteSpace(context.RawValue) && _regex.IsMatch(context.RawValue);
        }

        public string? Execute(RuleExecutionContext context)
        {
            var result = _regex.Replace(context.RawValue, _replacement);
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
    }
}

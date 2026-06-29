namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Returns a configurable default value if no other rule matched.
    /// Should always be the last rule (highest priority number).
    /// </summary>
    public class FallbackDefaultRule : IRuleDefinition
    {
        private readonly string _targetField;
        private readonly string? _hardcodedDefault;

        public string Name => $"FallbackDefault({_targetField})";
        public int Priority { get; }
        public string TargetField => _targetField;

        /// <param name="targetField">EDI field this rule targets.</param>
        /// <param name="hardcodedDefault">If set, always return this value. If null, lookup from code_defaults.csv.</param>
        /// <param name="priority">Should be high (default 100) to run last.</param>
        public FallbackDefaultRule(string targetField, string? hardcodedDefault = null, int priority = 100)
        {
            _targetField = targetField;
            _hardcodedDefault = hardcodedDefault;
            Priority = priority;
        }

        public bool CanExecute(RuleExecutionContext context)
        {
            return true; // Always executes (it's the fallback)
        }

        public string? Execute(RuleExecutionContext context)
        {
            if (_hardcodedDefault != null)
                return _hardcodedDefault;

            // Try the defaults table from code_defaults.csv
            var defaultVal = context.Mappings.GetDefault(context.TargetField);
            return !string.IsNullOrWhiteSpace(defaultVal) ? defaultVal : null;
        }
    }
}

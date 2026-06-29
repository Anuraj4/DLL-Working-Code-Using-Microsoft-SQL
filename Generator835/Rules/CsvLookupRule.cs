namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Looks up a value in the CSV mapping tables.
    /// Matches the raw EOB value against the configured mapping CSV for the target field.
    /// </summary>
    public class CsvLookupRule : IRuleDefinition
    {
        private readonly string _targetField;
        private readonly string _mappingTableName;

        public string Name => $"CsvLookup({_mappingTableName})";
        public int Priority { get; }
        public string TargetField => _targetField;

        /// <param name="targetField">EDI field this rule targets (e.g., "BPR04", "CLP02", "CAS01").</param>
        /// <param name="mappingTableName">CSV file name (without extension) to use for lookup.</param>
        /// <param name="priority">Execution priority (default 10 — runs early).</param>
        public CsvLookupRule(string targetField, string mappingTableName, int priority = 10)
        {
            _targetField = targetField;
            _mappingTableName = mappingTableName;
            Priority = priority;
        }

        public bool CanExecute(RuleExecutionContext context)
        {
            return !string.IsNullOrWhiteSpace(context.RawValue);
        }

        public string? Execute(RuleExecutionContext context)
        {
            return context.Mappings.LookupMapping(_mappingTableName, context.RawValue);
        }
    }
}

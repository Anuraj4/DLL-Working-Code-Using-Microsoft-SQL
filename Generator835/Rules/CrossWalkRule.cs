namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Delegates code lookups to the Xalta.Edi.CodeCrossWalk library.
    /// Enables complex multi-field lookups from external cross-walk data.
    /// </summary>
    public class CrossWalkRule : IRuleDefinition
    {
        private readonly string _targetField;

        public string Name => $"CrossWalk({_targetField})";
        public int Priority { get; }
        public string TargetField => _targetField;

        private readonly string _tableName;

        public CrossWalkRule(string targetField, string tableName, int priority = 20)
        {
            _targetField = targetField;
            _tableName = tableName;
            Priority = priority;
        }

        public bool CanExecute(RuleExecutionContext context)
        {
            return !string.IsNullOrWhiteSpace(context.RawValue);
        }

        public string? Execute(RuleExecutionContext context)
        {
            if (context.CrossWalkService == null) return null;

            // Lookup the RawValue in the specified master table (sheet)
            var result = context.CrossWalkService.Lookup(_tableName, context.RawValue);

            if (!string.IsNullOrEmpty(result) && result != context.RawValue)
            {
                return result;
            }

            return null; // Pass to next rule
        }
    }
}

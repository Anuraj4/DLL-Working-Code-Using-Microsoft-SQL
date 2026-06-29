using System.Collections.Generic;
using System.Text;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Represents the complete resolution outcome for a single EDI field.
    /// Provides the final value and a detailed "audit trail" of the decision-making process.
    /// </summary>
    public class RuleResolution
    {
        public string TargetField { get; set; } = string.Empty;
        public string FinalValue { get; set; } = string.Empty;
        public string MatchedRuleName { get; set; } = "None (Fallback)";

        /// <summary>Detailed step-by-step history of every rule checked.</summary>
        public List<RuleTraceStep> Trace { get; } = new List<RuleTraceStep>();

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[Resolution] {TargetField} -> '{FinalValue}' (Winning Rule: {MatchedRuleName})");
            foreach (var step in Trace)
            {
                sb.AppendLine($"  {step}");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// A single step in the rule resolution audit trail.
    /// </summary>
    public class RuleTraceStep
    {
        public string RuleName { get; set; } = string.Empty;
        public int Priority { get; set; }
        public RuleStepStatus Status { get; set; }
        public string ResultValue { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public override string ToString()
        {
            string statusIcon = Status switch
            {
                RuleStepStatus.Matched => "✅",
                RuleStepStatus.Skipped => "⏩",
                RuleStepStatus.NoMatch => "❌",
                _ => "?"
            };

            return $"{statusIcon} [{Priority}] {RuleName}: {Message} {(Status == RuleStepStatus.Matched ? $"-> '{ResultValue}'" : "")}";
        }
    }

    public enum RuleStepStatus
    {
        Skipped,
        NoMatch,
        Matched
    }
}

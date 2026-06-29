using System;
using Edi.Generator835.Context;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Cleans the SVC01-02 (Procedure Code) by removing any prefixed qualifiers (e.g. "HC:937033" -> "937033").
    /// </summary>
    public class Svc01ProcedureCodeRule : IRuleDefinition
    {
        public string Name => "SVC01-02 Procedure Code Sanitizer";
        public int Priority => 10;
        public string TargetField => "SVC01-02";

        public bool CanExecute(RuleExecutionContext context)
        {
            return context.TargetField == "SVC01-02";
        }

        public string? Execute(RuleExecutionContext context)
        {
            string code = context.RawValue ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;

            // 1. Remove prefixes
            var match = System.Text.RegularExpressions.Regex.Match(code, @"^(?:[a-zA-Z0-9]{2}[:>\.]\s*)+(.*)$");
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                code = match.Groups[1].Value.Trim(); // Do not return early; pass the remaining string to suffix remover
            }

            // 2. Remove suffix modifiers with separators (e.g., 99214:25 -> 99214)
            if (code.Contains(":") || code.Contains(">") || code.Contains("."))
            {
                var separators = new[] { ':', '>', '.' };
                var parts = code.Split(separators, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 0)
                {
                    return parts[0].Trim();
                }
            }

            // 3. Remove seamlessly concatenated modifiers
            if (!code.Contains(":") && !code.Contains(">") && !code.Contains("."))
            {
                var strippedSpaces = code.Replace(" ", "");
                if (strippedSpaces.Length == 7 || strippedSpaces.Length == 9)
                {
                    return strippedSpaces.Substring(0, 5);
                }
            }

            return code;
        }
    }
}

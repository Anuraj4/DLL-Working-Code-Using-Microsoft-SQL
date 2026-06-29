using System;
using System.Text.RegularExpressions;
using Edi.Generator835.Context;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Determines the SVC01-01 (Product/Service ID Qualifier) based on the format of the code.
    /// Implements rules for CPT, HCPCS, NDC, Revenue Codes, Dental, etc.
    /// </summary>
    public class Svc01QualifierRule : IRuleDefinition
    {
        public string Name => "SVC01-01 Regex Qualifier Rule";
        public int Priority => 10;
        public string TargetField => "SVC01-01";

        public bool CanExecute(RuleExecutionContext context)
        {
            return context.TargetField == "SVC01-01";
        }

        public string? Execute(RuleExecutionContext context)
        {
            string code = context.RawValue ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code)) return "HC";

            // Extract the first 2-character prefix followed by a separator (like HC:, HC>, or HC.)
            var match = Regex.Match(code, @"^([a-zA-Z0-9]{2})[:>\.]");
            if (match.Success)
            {
                return match.Groups[1].Value.ToUpperInvariant();
            }

            if (code.Contains(":") || code.Contains(">"))
            {
                var separator = code.Contains(":") ? ':' : '>';
                var parts = code.Split(separator);
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[0]))
                {
                    return parts[0].Trim().ToUpperInvariant();
                }
            }

            // Load dynamic patterns from config
            if (context.Mappings.RawMappingTables.TryGetValue("svc_qualifier_patterns", out var patterns))
            {
                foreach (var record in patterns)
                {
                    if (record.TryGetValue("Pattern", out var pattern) &&
                        record.TryGetValue("Qualifier", out var qualifier))
                    {
                        try
                        {
                            if (Regex.IsMatch(code, pattern, RegexOptions.IgnoreCase))
                            {
                                return qualifier;
                            }
                        }
                        catch
                        {
                            // Skip invalid regex in config
                            continue;
                        }
                    }
                }
            }

            return "HC"; // Default fallback if no pattern matches
        }
    }
}

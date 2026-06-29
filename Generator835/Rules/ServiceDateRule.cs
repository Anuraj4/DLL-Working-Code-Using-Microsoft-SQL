using System;
using System.Collections.Generic;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Rule for Service Date determination (Loop 2110 DTM).
    /// Logic:
    /// - If FromDate == ToDate -> Qualifier "472".
    /// - If FromDate != ToDate -> Returns "150" for the first segment and the generator should handle the range.
    /// - Currently limited to returning single values; generator will use this to decide branch.
    /// </summary>
    public class ServiceDateRule : IRuleDefinition
    {
        public string Name => "Service Date Rule";
        public int Priority => 5;
        public string TargetField => "DTM01_ServiceDate";

        public bool CanExecute(RuleExecutionContext context)
        {
            return context.TargetField == "DTM01_ServiceDate";
        }

        public string? Execute(RuleExecutionContext context)
        {
            string from = "";
            string to = "";
            context.RowData.TryGetValue("LineServiceDateFrom", out var fromVal);
            from = fromVal ?? "";
            context.RowData.TryGetValue("LineServiceDateTo", out var toVal);
            to = toVal ?? "";

            if (string.IsNullOrEmpty(from)) return null;

            if (from == to || string.IsNullOrEmpty(to))
            {
                return "472";
            }

            return "150"; // Indicates start of range
        }
    }
}

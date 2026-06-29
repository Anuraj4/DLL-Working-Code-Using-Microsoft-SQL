namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Rule for Rendering Provider Identification (NM108/NM109 in Loop 2100).
    /// Logic:
    /// - If RenderingNPI (>= 2 chars) -> NM108="XX", NM109=NPI.
    /// - Otherwise, returns null.
    /// </summary>
    public class RenderingProviderRule : IRuleDefinition
    {
        public string Name => "Rendering Provider Identification Rule";
        public int Priority => 5;
        public string TargetField => "*";

        public bool CanExecute(RuleExecutionContext context)
        {
            return context.TargetField == "NM108_Rendering" || context.TargetField == "NM109_Rendering";
        }

        public string? Execute(RuleExecutionContext context)
        {
            string npi = "";
            context.RowData.TryGetValue("ProviderRenderingNpi", out var npiVal);
            npi = npiVal ?? "";

            bool hasNpi = !string.IsNullOrEmpty(npi) && npi.Length >= 2;

            if (context.TargetField == "NM108_Rendering")
            {
                return hasNpi ? "XX" : null;
            }
            if (context.TargetField == "NM109_Rendering")
            {
                return hasNpi ? npi : null;
            }

            return null;
        }
    }
}

using System;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Rule for Payee Identification (N103/N104 in Loop 1000B).
    /// Logic:
    /// - If NPI is provided and valid (length >= 2) -> N103="XX", N104=NPI.
    /// - Else if Tax ID is provided -> N103="FI", N104=TaxID.
    /// - Otherwise, returns null to allow fallback to defaults.
    /// </summary>
    public class PayeeIdentificationRule : IRuleDefinition
    {
        public string Name => "Payee Identification Rule (NPI vs TaxID Fallback)";

        public int Priority => 5; // Relatively high priority to ensure it runs before generic fallbacks

        public string TargetField => "*"; // Handles multiple specific fields via CanExecute

        public bool CanExecute(RuleExecutionContext context)
        {
            return context.TargetField == "N102_Payee" || context.TargetField == "N103_Payee" || context.TargetField == "N104_Payee";
        }

        public string Execute(RuleExecutionContext context)
        {
            if (context.TargetField == "N102_Payee")
            {
                // Passthrough the name, but allows for potential future mapping/cleaning here
                return context.RawValue ?? string.Empty; // Ensure non-null return for string type
            }

            var header = context.DataModel.Header;

            // Handle potential null header or its properties gracefully
            if (header == null)
            {
                return string.Empty;
            }

            bool hasNpi = !string.IsNullOrWhiteSpace(header.ProviderNpi) && header.ProviderNpi.Trim().Length >= 2;
            bool hasTaxId = !string.IsNullOrWhiteSpace(header.ProviderTaxId);

            if (context.TargetField == "N103_Payee")
            {
                // Original logic: if (hasNpi) return "XX"; if (hasTaxId) return "FI";
                // Modified to ensure non-null return and align with the spirit of the provided snippet's structure
                return hasNpi ? "XX" : (hasTaxId ? "FI" : string.Empty);
            }
            else if (context.TargetField == "N104_Payee")
            {
                // Original logic: if (hasNpi) return header.ProviderNpi.Trim(); if (hasTaxId) return header.ProviderTaxId.Trim();
                // Modified to ensure non-null return and align with the spirit of the provided snippet's structure
                return hasNpi ? header.ProviderNpi.Trim() : (hasTaxId ? header.ProviderTaxId.Trim() : string.Empty);
            }

            return string.Empty;
        }
    }
}

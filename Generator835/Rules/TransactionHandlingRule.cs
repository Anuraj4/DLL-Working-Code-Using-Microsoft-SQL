using System;
using System.Linq;
using Edi.Generator835.Models;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Calculated rule for BPR01 (Transaction Handling Code).
    /// </summary>
    public class TransactionHandlingRule : IRuleDefinition
    {
        public string Name => "BPR01-Calculation";
        public string TargetField => "BPR01";
        public int Priority => 50;

        public bool CanExecute(RuleExecutionContext context) =>
            context.TargetField == "BPR01" ||
            context.TargetField == "BPR03" ||
            context.TargetField == "BPR04";

        public string? Execute(RuleExecutionContext context)
        {
            var data = context.DataModel;
            if (data == null) return null;

            if (context.TargetField == "BPR01")
            {
                bool hasEra = data.Claims.Any();
                bool hasPayment = Math.Abs(data.Header.TotalPaymentAmount) > 0;

                if (!hasPayment) return "H"; // Notification Only (Zero Pay or RA only)
                if (hasEra) return "C";      // Payment accompanies RA
                return "D";                  // Payment only
            }

            if (context.TargetField == "BPR03")
            {
                if (Math.Abs(data.Header.TotalPaymentAmount) == 0) return "C"; // Credits (as per user example)

                var totalPlb = data.ProviderAdjustments.Sum(a => a.PlbAmount);
                var net = data.Header.TotalPaymentAmount - totalPlb;
                return net < 0 ? "D" : "C";
            }

            if (context.TargetField == "BPR04")
            {
                if (Math.Abs(data.Header.TotalPaymentAmount) == 0) return "NON";

                if (!string.IsNullOrEmpty(data.Header.RoutingNumber) && !string.IsNullOrEmpty(data.Header.BankAccountNumber))
                    return "ACH";

                if (!string.IsNullOrEmpty(data.Header.CheckOrEftNumber))
                    return "CHK";

                return null; // Fallback to config
            }

            return null;
        }
    }
}

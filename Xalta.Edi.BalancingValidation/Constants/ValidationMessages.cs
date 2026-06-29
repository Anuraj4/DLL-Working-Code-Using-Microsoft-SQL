namespace Xalta.Edi.BalancingValidation.Constants
{
    /// <summary>
    /// Centralized repository for all validation error messages.
    /// </summary>
    public static class ValidationMessages
    {
        // Transaction Balancing
        public const string BprMissing = "BPR segment is missing.";
        public const string BprInvalidAmount = "BPR02 (Total Amount) is not a valid number.";
        public const string TransactionBalancingFailed = "Transaction balancing failed. BPR02 reported {0}, but calculation shows {3}. To balance, BPR02 must equal sum of Claims ({1}) minus Provider Adjustments ({2}).";

        // Claim Level Balancing
        public const string ClaimBalancingFailed = "Claim {0} balancing failed. CLP04 (Paid) is {1}, but should be {4}. To fix, ensure Paid = Billed ({2}) - Adjustments ({3}). [Details: PR Total={5}, Sequestration(CO-253)={6}]";

        // Service Line Balancing
        public const string ServiceLineBalancingFailed = "Service Line {0} balancing failed. SVC03 (Paid) is {1}, but should be {4}. To fix, ensure Paid = Billed ({2}) - Adjustments ({3}). [Details: PR Total={5}, Sequestration(CO-253)={6}]";

        // Structure Validation
        public const string StructuralError = "{0}: {1}"; // Location, Message
        public const string StructuralErrorDefault = "{0}: Structural Error (See standard for position)";
    }
}

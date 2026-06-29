namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Rule for Patient Identification (NM108/NM109 in Loop 2100).
    /// Logic:
    /// - If PatientId (>= 2 chars) -> NM108="MI", NM109=PatientId.
    /// - Else if SubscriberId (>= 2 chars) -> NM108="MI", NM109=SubscriberId.
    /// - Otherwise, returns null.
    /// </summary>
    public class PatientIdentificationRule : IRuleDefinition
    {
        public string Name => "Patient Identification Rule";
        public int Priority => 5;
        public string TargetField => "*";

        public bool CanExecute(RuleExecutionContext context)
        {
            return context.TargetField == "NM108_Patient" || context.TargetField == "NM109_Patient";
        }

        public string? Execute(RuleExecutionContext context)
        {
            // We need access to the specific claim. 
            // The generator should pass the claim context if possible, 
            // or the engine can look at context.RowData if populated.
            // For now, let's assume the generator passes the relevant ID as context.RawValue for NM108/NM109.
            // But actually, it's better if the rule knows how to find it.

            // In Edi835Generator.ToRowData(ClaimData), we add some fields.
            // Let's ensure we have PatientId and SubscriberId in RowData or use the DataModel if we know which claim.

            // Actually, Edi835Generator.Generate loops through claims. 
            // If the generator populates RowData with claim-specific info, the rule can use it.

            string patientId = "";
            string subscriberId = "";

            context.RowData.TryGetValue("PatientId", out var pId);
            patientId = pId ?? "";

            context.RowData.TryGetValue("SubscriberId", out var sId);
            subscriberId = sId ?? "";

            bool hasPatientId = !string.IsNullOrEmpty(patientId) && patientId.Length >= 2;
            bool hasSubscriberId = !string.IsNullOrEmpty(subscriberId) && subscriberId.Length >= 2;

            if (context.TargetField == "NM108_Patient")
            {
                return (hasPatientId || hasSubscriberId) ? "MI" : null;
            }
            if (context.TargetField == "NM109_Patient")
            {
                if (hasPatientId) return patientId;
                if (hasSubscriberId) return subscriberId;
            }

            return null;
        }
    }
}

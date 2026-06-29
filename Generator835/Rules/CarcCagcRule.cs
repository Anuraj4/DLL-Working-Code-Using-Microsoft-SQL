using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Master mapping rule for CARC to GAGC (CAS01 determined by CAS02).
    /// Uses robust lookup engine from MappingConfiguration.
    /// </summary>
    public class CarcCagcRule : IRuleDefinition
    {
        public string Name => "CARC-to-CAGC-Mapping";

        // Priority should be higher (lower number) than generic lookups to ensure master mapping wins
        public int Priority => 5;

        public string TargetField => "CAS01";

        public bool CanExecute(RuleExecutionContext context) => context.TargetField == TargetField;

        public string? Execute(RuleExecutionContext context)
        {
            // Extracted values from EOB or Data Model
            string groupCode = context.RowData.ContainsKey("AdjustmentGroupCode") ? context.RowData["AdjustmentGroupCode"] : "";
            string reasonCode = context.RowData.ContainsKey("AdjustmentReasonCode") ? context.RowData["AdjustmentReasonCode"] : context.RawValue;
            string payerName = context.DataModel?.Header?.PayerName ?? "";
            string payerId = context.DataModel?.Header?.PayerId ?? "";
            string eobType = context.DataModel?.Header?.PayerEobType ?? "";

            Log.Debug("[CarcCagcRule] Executing for ReasonCode: {ReasonCode}, Payer: {Payer} (ID: {PayerId}), EOBType: {EOBType}, InitialGroup: {InitialGroup}",
                reasonCode, payerName, payerId, eobType, groupCode);

            // 1. New Robust Multi-Phase Lookup (Payer-Specific -> Global -> Combined)
            var (foundCagc, foundCarc) = context.Mappings.LookupAdjustment(payerId, eobType, groupCode, reasonCode);

            if (!string.IsNullOrEmpty(foundCagc))
            {
                Log.Information("[CarcCagcRule] Found CAGC in adjustment_group_mapping: {CAGC} for Reason: {Reason}", foundCagc, reasonCode);
                return foundCagc;
            }

            // 2. Secondary Fallback: Precise Reason Code Match using generic mapping table (carc_cagc_mapping)
            var fallbackGroupCode = context.Mappings.LookupMapping("carc_cagc_mapping", reasonCode);

            if (!string.IsNullOrEmpty(fallbackGroupCode) && fallbackGroupCode != reasonCode)
            {
                Log.Information("[CarcCagcRule] Found CAGC in secondary fallback mapping: {CAGC} for Reason: {Reason}", fallbackGroupCode, reasonCode);
                return fallbackGroupCode;
            }

            // 3. Final Fallback: Master CrossWalk Service
            if (context.CrossWalkService != null)
            {
                var crossWalkGroup = context.CrossWalkService.Lookup("CARC_GAGC_GEM", reasonCode);
                if (!string.IsNullOrEmpty(crossWalkGroup) && crossWalkGroup != reasonCode)
                {
                    Log.Information("[CarcCagcRule] Found CAGC in Master CrossWalk lookup: {CAGC} for Reason: {Reason}", crossWalkGroup, reasonCode);
                    return crossWalkGroup;
                }
            }

            Log.Debug("[CarcCagcRule] No mapping found for ReasonCode: {ReasonCode}. Passing to next rule.", reasonCode);
            return null; // Fallback to next rule
        }
    }
}

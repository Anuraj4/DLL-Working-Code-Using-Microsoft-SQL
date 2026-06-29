using System;
using System.Linq;
using Edi.Generator835.Models;
using Edi.Generator835.Services.Interfaces;
using Serilog;

namespace Edi.Generator835.Services
{
    public class PatientResponsibilityFixerService : IPatientResponsibilityFixer
    {
        public void FixPatientResponsibility(Edi835DataModel model)
        {
            Log.Information("[PR-FIX] Starting patient responsibility validation and fix...");

            if (model?.Claims == null) return;

            foreach (var claim in model.Claims)
            {
                foreach (var line in claim.ServiceLines)
                {
                    FixServiceLinePr(line);
                }

                // 2. Synchronize claim level total
                var prValues = claim.ServiceLines.Select(l => l.LinePatientResponsibilityAmount).Where(v => v != null).ToList();
                claim.PatientResponsibilityAmount = prValues.Any() ? prValues.Sum() : (decimal?)null;
                Log.Debug("[PR-FIX] Claim {ClaimId} total PR synchronized to {Total}", claim.ClaimIdPayer, claim.PatientResponsibilityAmount);
            }

            Log.Information("[PR-FIX] Patient responsibility validation and fix complete.");
        }

        private void FixServiceLinePr(ServiceLineData line)
        {
            // 1. Calculate sum from breakdown columns in adjustments
            decimal excelCopay = line.Adjustments.Sum(a => a.CopayAmount ?? 0);
            decimal excelCoinsurance = line.Adjustments.Sum(a => a.CoinsuranceAmount ?? 0);
            decimal excelDeductible = line.Adjustments.Sum(a => a.DeductibleAmount ?? 0);
            decimal excelSequestration = line.Adjustments.Sum(a => a.SequestrationAmount ?? 0);

            bool hasBreakdown = excelCopay != 0 || excelCoinsurance != 0 || excelDeductible != 0;

            if (!hasBreakdown)
            {
                return; // Nothing to validate against
            }

            decimal calPr = excelCopay + excelCoinsurance + excelDeductible;
            decimal rawLinePr = line.LinePatientResponsibilityAmount ?? 0;
            decimal allowedMinusPaid = (line.LineAllowedAmount ?? 0) - line.LinePaidAmount;

            // Logic:
            // if (calpr != raw patient responbility or cal pr + seq != raw patientresponsiblity or calPr - seq != raw patientresponsiblity) 
            // AND (calPr != allowed -paid or calpr - seq != allowed - paid or calpr + seq != allowed - paid)

            bool matchesRawPr = Math.Abs(calPr - rawLinePr) <= 0.01m ||
                                 Math.Abs((calPr + excelSequestration) - rawLinePr) <= 0.01m ||
                                 Math.Abs((calPr - excelSequestration) - rawLinePr) <= 0.01m;

            bool matchesAllowedPaid = Math.Abs(calPr - allowedMinusPaid) <= 0.01m ||
                                      Math.Abs((calPr - excelSequestration) - allowedMinusPaid) <= 0.01m ||
                                      Math.Abs((calPr + excelSequestration) - allowedMinusPaid) <= 0.01m;

            if (!matchesRawPr && matchesAllowedPaid)
            {
                Log.Warning("[PR-FIX] Mismatch detected for Service Line {SvcId} (CPT: {Cpt}). " +
                            "CalPr={CalPr} (Breakdown Sum), RawLinePr={RawPr}, Allowed-Paid={AP}. Fixing Line PR to {FixVal}.",
                            line.ServiceLineId, line.CptCode, calPr, rawLinePr, allowedMinusPaid, calPr);

                line.LinePatientResponsibilityAmount = calPr;
            }
            else
            {
                Log.Debug("[PR-FIX] Service Line {SvcId} (CPT: {Cpt}) PR is valid. calPr={CalPr}, RawPr={RawPr}, Allowed-Paid={AP}.",
                          line.ServiceLineId, line.CptCode, calPr, rawLinePr, allowedMinusPaid);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EOB_TO_EDI_835.AvaloniaUI.Models;

namespace EOB_TO_EDI_835.AvaloniaUI.Services;

public class EdiMappingService
{
    public (string Sender, string Receiver, TransactionHeaderInfo Header, List<ClaimViewModel> Claims) MapEdiToFunctionalModels(string content)
    {
        var segments = new List<EdiSegment>();
        var segmentStrings = content.Split('~', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segStr in segmentStrings)
        {
            var parts = segStr.Split('*');
            if (parts.Length == 0) continue;

            var segment = new EdiSegment
            {
                Tag = parts[0].Trim(),
                RawContent = segStr,
                Elements = parts.Skip(1).Select((val, idx) => new EdiElement
                {
                    Index = idx + 1,
                    Value = val
                }).ToList()
            };
            segments.Add(segment);
        }

        string sender = "Unknown";
        string receiver = "Unknown";
        var header = new TransactionHeaderInfo();
        var claims = new List<ClaimViewModel>();
        ClaimViewModel? currentClaim = null;

        var isa = segments.FirstOrDefault(s => s.Tag == "ISA");
        if (isa != null && isa.Elements.Count >= 8)
        {
            sender = isa.Elements[5].Value.Trim();
            receiver = isa.Elements[7].Value.Trim();
        }

        // Parse header-level segments (before any CLP)
        EntityInfo? currentEntity = null;
        bool inClaimSection = false;

        foreach (var seg in segments)
        {
            if (seg.Tag == "CLP") inClaimSection = true;

            // Header-level segments (before first CLP)
            if (!inClaimSection)
            {
                switch (seg.Tag)
                {
                    case "BPR":
                        header.PaymentMethod = seg.Elements.Count >= 1 ? seg.Elements[0].Value : "";
                        header.PaymentAmount = seg.Elements.Count >= 2 && decimal.TryParse(seg.Elements[1].Value, out var bprAmt) ? bprAmt : 0;
                        header.PaymentDate = seg.Elements.Count >= 16 ? seg.Elements[15].Value : "";
                        break;
                    case "TRN":
                        header.TraceNumber = seg.Elements.Count >= 2 ? seg.Elements[1].Value : "";
                        header.TraceOriginatingId = seg.Elements.Count >= 3 ? seg.Elements[2].Value : "";
                        break;
                    case "N1":
                        var entityCode = seg.Elements.Count >= 1 ? seg.Elements[0].Value : "";
                        if (entityCode == "PR") currentEntity = header.Payer;
                        else if (entityCode == "PE") currentEntity = header.Payee;
                        else currentEntity = null;
                        if (currentEntity != null)
                        {
                            currentEntity.Name = seg.Elements.Count >= 2 ? seg.Elements[1].Value : "";
                            currentEntity.IdQualifier = seg.Elements.Count >= 3 ? seg.Elements[2].Value : "";
                            currentEntity.Id = seg.Elements.Count >= 4 ? seg.Elements[3].Value : "";
                        }
                        break;
                    case "N3" when currentEntity != null:
                        currentEntity.Address1 = seg.Elements.Count >= 1 ? seg.Elements[0].Value : "";
                        currentEntity.Address2 = seg.Elements.Count >= 2 ? seg.Elements[1].Value : "";
                        break;
                    case "N4" when currentEntity != null:
                        currentEntity.City = seg.Elements.Count >= 1 ? seg.Elements[0].Value : "";
                        currentEntity.State = seg.Elements.Count >= 2 ? seg.Elements[1].Value : "";
                        currentEntity.Zip = seg.Elements.Count >= 3 ? seg.Elements[2].Value : "";
                        break;
                    case "PER" when currentEntity != null:
                        currentEntity.ContactName = seg.Elements.Count >= 2 ? seg.Elements[1].Value : "";
                        currentEntity.ContactPhone = seg.Elements.Count >= 4 ? seg.Elements[3].Value : "";
                        currentEntity.ContactEmail = seg.Elements.Count >= 8 ? seg.Elements[7].Value : "";
                        break;
                    case "REF" when currentEntity != null:
                        currentEntity.References.Add(new GenericEdiData
                        {
                            Code = seg.Elements.Count >= 1 ? seg.Elements[0].Value : "",
                            Value = seg.Elements.Count >= 2 ? seg.Elements[1].Value : ""
                        });
                        break;
                    case "REF":
                        header.HeaderReferences.Add(new GenericEdiData
                        {
                            Code = seg.Elements.Count >= 1 ? seg.Elements[0].Value : "",
                            Value = seg.Elements.Count >= 2 ? seg.Elements[1].Value : ""
                        });
                        break;
                }
                continue;
            }

            // Claim-level segments
            if (seg.Tag == "CLP")
            {
                currentClaim = new ClaimViewModel
                {
                    OriginalSegment = seg,
                    ClaimId = seg.Elements.Count >= 1 ? seg.Elements[0].Value : "",
                    Status = seg.Elements.Count >= 2 ? seg.Elements[1].Value : "",
                    TotalCharge = seg.Elements.Count >= 3 && decimal.TryParse(seg.Elements[2].Value, out var c) ? c : 0,
                    TotalPaid = seg.Elements.Count >= 4 && decimal.TryParse(seg.Elements[3].Value, out var p) ? p : 0,
                    PatientControlNumber = seg.Elements.Count >= 7 ? seg.Elements[6].Value : ""
                };
                claims.Add(currentClaim);
            }
            else if (seg.Tag == "NM1" && currentClaim != null && !currentClaim.ServiceLines.Any())
            {
                var entityId = seg.Elements.Count >= 1 ? seg.Elements[0].Value : "";
                var name = seg.Elements.Count >= 3 ? seg.Elements[2].Value : "";
                var firstName = seg.Elements.Count >= 4 ? seg.Elements[3].Value : "";
                var fullName = string.IsNullOrWhiteSpace(firstName) ? name : $"{firstName} {name}".Trim();
                if (entityId == "QC") currentClaim.PatientName = fullName;
                else if (entityId == "82" || entityId == "PE") currentClaim.ProviderName = fullName;
                else if (entityId == "PR") currentClaim.PayerName = fullName;
            }
            else if (seg.Tag == "REF" && currentClaim != null)
            {
                var refData = new GenericEdiData { Code = seg.Elements.Count >= 1 ? seg.Elements[0].Value : "", Value = seg.Elements.Count >= 2 ? seg.Elements[1].Value : "" };
                if (currentClaim.ServiceLines.Any()) currentClaim.ServiceLines.Last().ServiceReferences.Add(refData);
                else currentClaim.ClaimReferences.Add(refData);
            }
            else if (seg.Tag == "CAS" && currentClaim != null)
            {
                var groupCode = seg.Elements.Count >= 1 ? seg.Elements[0].Value : "";
                for (int i = 1; i < seg.Elements.Count; i += 3)
                {
                    if (string.IsNullOrWhiteSpace(seg.Elements[i].Value)) continue;
                    var cas = new AdjustmentData
                    {
                        GroupCode = groupCode,
                        ReasonCode = seg.Elements[i].Value,
                        Amount = decimal.TryParse(seg.Elements.Count > i + 1 ? seg.Elements[i + 1].Value : "0", out var a) ? a : 0
                    };
                    if (currentClaim.ServiceLines.Any()) currentClaim.ServiceLines.Last().ServiceAdjustments.Add(cas);
                    else currentClaim.ClaimAdjustments.Add(cas);
                }
            }
            else if (seg.Tag == "AMT" && currentClaim != null)
            {
                var amtData = new GenericEdiData { Code = seg.Elements.Count >= 1 ? seg.Elements[0].Value : "", Value = seg.Elements.Count >= 2 ? seg.Elements[1].Value : "" };
                if (currentClaim.ServiceLines.Any()) currentClaim.ServiceLines.Last().ServiceAmounts.Add(amtData);
                else currentClaim.ClaimAmounts.Add(amtData);
            }
            else if (seg.Tag == "SVC" && currentClaim != null)
            {
                var svcParts = seg.Elements.Count >= 1 ? seg.Elements[0].Value.Split('>') : new[] { "" };
                var service = new ServiceLineViewModel
                {
                    OriginalSegment = seg,
                    ServiceCode = svcParts.Length > 1 ? svcParts[1] : svcParts[0],
                    Charge = seg.Elements.Count >= 2 && decimal.TryParse(seg.Elements[1].Value, out var c) ? c : 0,
                    Paid = seg.Elements.Count >= 3 && decimal.TryParse(seg.Elements[2].Value, out var p) ? p : 0,
                    RevenueCode = seg.Elements.Count >= 4 ? seg.Elements[3].Value : ""
                };
                currentClaim.ServiceLines.Add(service);
            }
            else if (seg.Tag == "DTM" && currentClaim != null && currentClaim.ServiceLines.Any())
            {
                var lastSvc = currentClaim.ServiceLines.Last();
                if (seg.Elements.Count >= 2 && seg.Elements[0].Value == "472")
                {
                    lastSvc.ServiceDate = seg.Elements[1].Value;
                }
            }
        }

        return (sender, receiver, header, claims);
    }
}
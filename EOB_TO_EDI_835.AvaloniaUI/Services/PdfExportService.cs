using System;
using System.Collections.Generic;
using System.IO;
using EOB_TO_EDI_835.AvaloniaUI.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EOB_TO_EDI_835.AvaloniaUI.Services;

public class PdfExportService
{
    public string ExportClaimsToPdf(string filePath, string sender, string receiver, TransactionHeaderInfo? header, IEnumerable<ClaimViewModel> claims)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var outputPath = filePath.Replace(".txt", ".pdf").Replace(".edi", ".pdf");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Verdana));

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("HEALTHCARE PAYMENT ADVICE (835)").FontSize(18).SemiBold().FontColor(Colors.Blue.Medium);
                        col.Item().Text($"{DateTime.Now:MMMM dd, yyyy}").FontSize(9).Italic();
                    });

                    row.RelativeItem().AlignRight().Column(col =>
                    {
                        col.Item().Text("PROFESSIONAL EOB REPORT").FontSize(11).SemiBold();
                        col.Item().Text($"Ref: {Path.GetFileName(filePath)}").FontSize(8).FontColor(Colors.Grey.Medium);
                    });
                });

                page.Content().PaddingVertical(10).Column(column =>
                {
                    // ── BPR / TRN Payment Info ──
                    if (header != null)
                    {
                        column.Item().Border(1).BorderColor(Colors.Blue.Lighten3).Column(bprCol =>
                        {
                            bprCol.Item().Background(Colors.Blue.Lighten4).Padding(6).Text("PAYMENT INFORMATION (BPR / TRN)").FontSize(9).SemiBold().FontColor(Colors.Blue.Darken2);
                            bprCol.Item().Padding(8).Row(r =>
                            {
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Payment Method").FontSize(7).FontColor(Colors.Grey.Medium);
                                    c.Item().Text(header.PaymentMethod).Bold();
                                });
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Payment Amount").FontSize(7).FontColor(Colors.Grey.Medium);
                                    c.Item().Text($"{header.PaymentAmount:C}").Bold().FontColor(Colors.Green.Medium);
                                });
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Payment Date").FontSize(7).FontColor(Colors.Grey.Medium);
                                    c.Item().Text(header.PaymentDate).Bold();
                                });
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Trace Number").FontSize(7).FontColor(Colors.Grey.Medium);
                                    c.Item().Text(header.TraceNumber).Bold();
                                });
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Originating ID").FontSize(7).FontColor(Colors.Grey.Medium);
                                    c.Item().Text(header.TraceOriginatingId);
                                });
                            });
                        });

                        column.Item().PaddingTop(8);
                    }

                    // ── Payer & Payee Section ──
                    column.Item().Row(row =>
                    {
                        // Payer
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Column(c =>
                        {
                            c.Item().Background(Colors.Grey.Lighten4).Padding(6).Text("PAYER (N1*PR)").FontSize(8).SemiBold().FontColor(Colors.Grey.Medium);
                            c.Item().Padding(8).Column(inner =>
                            {
                                if (header != null && !string.IsNullOrEmpty(header.Payer.Name))
                                {
                                    inner.Item().Text(header.Payer.Name).FontSize(11).Bold();
                                    inner.Item().Text($"ID: {header.Payer.IdQualifier} - {header.Payer.Id}").FontSize(8);
                                    if (!string.IsNullOrEmpty(header.Payer.Address1))
                                        inner.Item().Text(header.Payer.Address1).FontSize(8);
                                    if (!string.IsNullOrEmpty(header.Payer.Address2))
                                        inner.Item().Text(header.Payer.Address2).FontSize(8);
                                    if (!string.IsNullOrEmpty(header.Payer.City))
                                        inner.Item().Text($"{header.Payer.City}, {header.Payer.State} {header.Payer.Zip}").FontSize(8);
                                    if (!string.IsNullOrEmpty(header.Payer.ContactName))
                                        inner.Item().Text($"Contact: {header.Payer.ContactName}").FontSize(8);
                                    if (!string.IsNullOrEmpty(header.Payer.ContactPhone))
                                        inner.Item().Text($"Phone: {header.Payer.ContactPhone}").FontSize(8);
                                    foreach (var r in header.Payer.References)
                                        inner.Item().Text($"REF {r.Code}: {r.Value}").FontSize(7).FontColor(Colors.Grey.Darken1);
                                }
                                else
                                {
                                    inner.Item().Text(sender).FontSize(10).Bold();
                                }
                            });
                        });

                        row.ConstantItem(12);

                        // Payee
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Column(c =>
                        {
                            c.Item().Background(Colors.Grey.Lighten4).Padding(6).Text("PAYEE (N1*PE)").FontSize(8).SemiBold().FontColor(Colors.Grey.Medium);
                            c.Item().Padding(8).Column(inner =>
                            {
                                if (header != null && !string.IsNullOrEmpty(header.Payee.Name))
                                {
                                    inner.Item().Text(header.Payee.Name).FontSize(11).Bold();
                                    inner.Item().Text($"ID: {header.Payee.IdQualifier} - {header.Payee.Id}").FontSize(8);
                                    if (!string.IsNullOrEmpty(header.Payee.Address1))
                                        inner.Item().Text(header.Payee.Address1).FontSize(8);
                                    if (!string.IsNullOrEmpty(header.Payee.Address2))
                                        inner.Item().Text(header.Payee.Address2).FontSize(8);
                                    if (!string.IsNullOrEmpty(header.Payee.City))
                                        inner.Item().Text($"{header.Payee.City}, {header.Payee.State} {header.Payee.Zip}").FontSize(8);
                                    if (!string.IsNullOrEmpty(header.Payee.ContactName))
                                        inner.Item().Text($"Contact: {header.Payee.ContactName}").FontSize(8);
                                    if (!string.IsNullOrEmpty(header.Payee.ContactPhone))
                                        inner.Item().Text($"Phone: {header.Payee.ContactPhone}").FontSize(8);
                                    foreach (var r in header.Payee.References)
                                        inner.Item().Text($"REF {r.Code}: {r.Value}").FontSize(7).FontColor(Colors.Grey.Darken1);
                                }
                                else
                                {
                                    inner.Item().Text(receiver).FontSize(10).Bold();
                                }
                            });
                        });
                    });

                    // ── Header-level References ──
                    if (header != null && header.HeaderReferences.Count > 0)
                    {
                        column.Item().PaddingTop(6).Column(refCol =>
                        {
                            refCol.Item().Text("HEADER REFERENCES").FontSize(8).SemiBold().FontColor(Colors.Grey.Medium);
                            foreach (var r in header.HeaderReferences)
                                refCol.Item().Text($"REF {r.Code}: {r.Value}").FontSize(8);
                        });
                    }

                    column.Item().PaddingTop(15).Text("CLAIM DETAILS").FontSize(12).SemiBold().Underline();

                    foreach (var claim in claims)
                    {
                        column.Item().PaddingTop(10).Border(1).BorderColor(Colors.Grey.Lighten3).Column(claimCol =>
                        {
                            claimCol.Item().Background(Colors.Grey.Lighten4).Padding(6).Row(r =>
                            {
                                r.RelativeItem().Text($"Claim ID: {claim.ClaimId}").Bold().FontSize(10);
                                r.RelativeItem().AlignRight().Text($"Total Paid: {claim.TotalPaid:C}").Bold().FontSize(10).FontColor(Colors.Green.Medium);
                            });

                            claimCol.Item().Padding(6).Row(r =>
                            {
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Patient:").SemiBold().FontSize(8).FontColor(Colors.Grey.Medium);
                                    c.Item().Text(string.IsNullOrEmpty(claim.PatientName) ? "N/A" : claim.PatientName);
                                });
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Provider:").SemiBold().FontSize(8).FontColor(Colors.Grey.Medium);
                                    c.Item().Text(string.IsNullOrEmpty(claim.ProviderName) ? "N/A" : claim.ProviderName);
                                });
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Patient Control #:").SemiBold().FontSize(8).FontColor(Colors.Grey.Medium);
                                    c.Item().Text(claim.PatientControlNumber);
                                });
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Status:").SemiBold().FontSize(8).FontColor(Colors.Grey.Medium);
                                    c.Item().Text(claim.Status);
                                });
                            });

                            if (claim.ClaimReferences.Count > 0 || claim.ClaimAdjustments.Count > 0 || claim.ClaimAmounts.Count > 0)
                            {
                                claimCol.Item().PaddingHorizontal(6).PaddingBottom(4).BorderTop(1).BorderColor(Colors.Grey.Lighten4).PaddingTop(4).Column(detailsCol =>
                                {
                                    // CAS Adjustments Table
                                    if (claim.ClaimAdjustments.Count > 0)
                                    {
                                        detailsCol.Item().PaddingBottom(4).Column(casCol =>
                                        {
                                            casCol.Item().Text("ADJUSTMENTS (CAS)").FontSize(7).SemiBold().FontColor(Colors.Grey.Medium);
                                            casCol.Item().Table(t =>
                                            {
                                                t.ColumnsDefinition(c => { c.ConstantColumn(60); c.RelativeColumn(); c.ConstantColumn(70); });
                                                t.Header(h =>
                                                {
                                                    h.Cell().Text("Group").SemiBold().FontSize(7).FontColor(Colors.Grey.Medium);
                                                    h.Cell().Text("Reason Code").SemiBold().FontSize(7).FontColor(Colors.Grey.Medium);
                                                    h.Cell().AlignRight().Text("Amount").SemiBold().FontSize(7).FontColor(Colors.Grey.Medium);
                                                    h.Cell().ColumnSpan(3).PaddingVertical(1).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten3);
                                                });
                                                foreach (var adj in claim.ClaimAdjustments)
                                                {
                                                    t.Cell().Text(adj.GroupCode).Bold().FontSize(8);
                                                    t.Cell().Text(adj.ReasonCode).FontSize(8);
                                                    t.Cell().AlignRight().Text($"{adj.Amount:C}").FontSize(8).FontColor(Colors.Red.Medium);
                                                }
                                            });
                                        });
                                    }

                                    // REF References Table
                                    if (claim.ClaimReferences.Count > 0)
                                    {
                                        detailsCol.Item().PaddingBottom(4).Column(refCol =>
                                        {
                                            refCol.Item().Text("REFERENCES (REF)").FontSize(7).SemiBold().FontColor(Colors.Grey.Medium);
                                            refCol.Item().Table(t =>
                                            {
                                                t.ColumnsDefinition(c => { c.ConstantColumn(60); c.RelativeColumn(); });
                                                t.Header(h =>
                                                {
                                                    h.Cell().Text("Code").SemiBold().FontSize(7).FontColor(Colors.Grey.Medium);
                                                    h.Cell().Text("Value").SemiBold().FontSize(7).FontColor(Colors.Grey.Medium);
                                                    h.Cell().ColumnSpan(2).PaddingVertical(1).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten3);
                                                });
                                                foreach (var refData in claim.ClaimReferences)
                                                {
                                                    t.Cell().Text(refData.Code).Bold().FontSize(8);
                                                    t.Cell().Text(refData.Value).FontSize(8);
                                                }
                                            });
                                        });
                                    }

                                    // AMT Amounts Table
                                    if (claim.ClaimAmounts.Count > 0)
                                    {
                                        detailsCol.Item().PaddingBottom(4).Column(amtCol =>
                                        {
                                            amtCol.Item().Text("AMOUNTS (AMT)").FontSize(7).SemiBold().FontColor(Colors.Grey.Medium);
                                            amtCol.Item().Table(t =>
                                            {
                                                t.ColumnsDefinition(c => { c.ConstantColumn(60); c.RelativeColumn(); });
                                                t.Header(h =>
                                                {
                                                    h.Cell().Text("Code").SemiBold().FontSize(7).FontColor(Colors.Grey.Medium);
                                                    h.Cell().AlignRight().Text("Value").SemiBold().FontSize(7).FontColor(Colors.Grey.Medium);
                                                    h.Cell().ColumnSpan(2).PaddingVertical(1).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten3);
                                                });
                                                foreach (var amt in claim.ClaimAmounts)
                                                {
                                                    t.Cell().Text(amt.Code).Bold().FontSize(8);
                                                    t.Cell().AlignRight().Text($"${amt.Value}").FontSize(8);
                                                }
                                            });
                                        });
                                    }
                                });
                            }

                            // Service Lines Table
                            claimCol.Item().Padding(6).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(60);
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(60);
                                    columns.ConstantColumn(70);
                                    columns.ConstantColumn(70);
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Text("Date").SemiBold().FontSize(8);
                                    h.Cell().Text("Procedure").SemiBold().FontSize(8);
                                    h.Cell().AlignRight().Text("Rev Code").SemiBold().FontSize(8);
                                    h.Cell().AlignRight().Text("Charge").SemiBold().FontSize(8);
                                    h.Cell().AlignRight().Text("Paid").SemiBold().FontSize(8);
                                    h.Cell().ColumnSpan(5).PaddingVertical(2).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                                });

                                foreach (var svc in claim.ServiceLines)
                                {
                                    table.Cell().PaddingTop(4).Text(svc.ServiceDate).FontSize(8);
                                    table.Cell().PaddingTop(4).Text(svc.ServiceCode).Bold().FontSize(8);
                                    table.Cell().PaddingTop(4).AlignRight().Text(svc.RevenueCode).FontSize(8);
                                    table.Cell().PaddingTop(4).AlignRight().Text($"{svc.Charge:C}").FontSize(8);
                                    table.Cell().PaddingTop(4).AlignRight().Text($"{svc.Paid:C}").FontSize(8).FontColor(Colors.Green.Medium);

                                    // SVC-level CAS sub-table
                                    if (svc.ServiceAdjustments.Count > 0)
                                    {
                                        table.Cell().ColumnSpan(5).PaddingLeft(60).PaddingTop(2).PaddingBottom(2).Table(casT =>
                                        {
                                            casT.ColumnsDefinition(c => { c.ConstantColumn(50); c.RelativeColumn(); c.ConstantColumn(60); });
                                            casT.Header(h =>
                                            {
                                                h.Cell().Text("Group").FontSize(6).SemiBold().FontColor(Colors.Grey.Medium);
                                                h.Cell().Text("Reason").FontSize(6).SemiBold().FontColor(Colors.Grey.Medium);
                                                h.Cell().AlignRight().Text("Adj. Amt").FontSize(6).SemiBold().FontColor(Colors.Grey.Medium);
                                            });
                                            foreach (var adj in svc.ServiceAdjustments)
                                            {
                                                casT.Cell().Text(adj.GroupCode).Bold().FontSize(7);
                                                casT.Cell().Text(adj.ReasonCode).FontSize(7);
                                                casT.Cell().AlignRight().Text($"{adj.Amount:C}").FontSize(7).FontColor(Colors.Red.Medium);
                                            }
                                        });
                                    }

                                    // SVC-level REF sub-table
                                    if (svc.ServiceReferences.Count > 0)
                                    {
                                        table.Cell().ColumnSpan(5).PaddingLeft(60).PaddingTop(1).PaddingBottom(2).Table(refT =>
                                        {
                                            refT.ColumnsDefinition(c => { c.ConstantColumn(50); c.RelativeColumn(); });
                                            refT.Header(h =>
                                            {
                                                h.Cell().Text("REF Code").FontSize(6).SemiBold().FontColor(Colors.Grey.Medium);
                                                h.Cell().Text("Value").FontSize(6).SemiBold().FontColor(Colors.Grey.Medium);
                                            });
                                            foreach (var r in svc.ServiceReferences)
                                            {
                                                refT.Cell().Text(r.Code).Bold().FontSize(7);
                                                refT.Cell().Text(r.Value).FontSize(7);
                                            }
                                        });
                                    }

                                    // SVC-level AMT sub-table
                                    if (svc.ServiceAmounts.Count > 0)
                                    {
                                        table.Cell().ColumnSpan(5).PaddingLeft(60).PaddingTop(1).PaddingBottom(2).Table(amtT =>
                                        {
                                            amtT.ColumnsDefinition(c => { c.ConstantColumn(50); c.RelativeColumn(); });
                                            amtT.Header(h =>
                                            {
                                                h.Cell().Text("AMT Code").FontSize(6).SemiBold().FontColor(Colors.Grey.Medium);
                                                h.Cell().AlignRight().Text("Value").FontSize(6).SemiBold().FontColor(Colors.Grey.Medium);
                                            });
                                            foreach (var amt in svc.ServiceAmounts)
                                            {
                                                amtT.Cell().Text(amt.Code).Bold().FontSize(7);
                                                amtT.Cell().AlignRight().Text($"${amt.Value}").FontSize(7);
                                            }
                                        });
                                    }
                                }
                            });
                        });
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" of ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf(outputPath);

        return outputPath;
    }
}

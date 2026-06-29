using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Edi.Generator835.Models;
using ExcelDataReader;
using Serilog;
using Edi.Generator835.Configuration;
using Edi.Generator835.Services;

namespace Edi.Generator835.Readers
{
    /// <summary>
    /// Abstraction for reading EOB data from a source into the data model.
    /// </summary>
    public interface IEobExcelReader
    {
        Edi835DataModel ReadEobData(string filePath, MappingConfiguration? mappings = null);
    }

    /// <summary>
    /// Reads Eob_Data.xlsx and maps all sheets to the Edi835DataModel hierarchy.
    /// Sheets: payment_header, claims, service_lines, adjustments, plb.
    /// </summary>
    public class EobExcelReader : IEobExcelReader
    {
        static EobExcelReader()
        {
#if !NET48
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
#endif
        }

        public Edi835DataModel ReadEobData(string filePath, MappingConfiguration? mappings = null)
        {
            Log.Information("Reading EOB Excel data from {FilePath}", filePath);
            if (!File.Exists(filePath))
            {
                Log.Error("EOB Excel file not found: {FilePath}", filePath);
                throw new FileNotFoundException($"EOB Excel file not found: {filePath}");
            }

            var model = new Edi835DataModel();
            var splitter = mappings != null ? new CarcRarcSplitter(mappings) : null;

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
                });

                Log.Information("Excel file loaded. Sheets found: {SheetCount}", dataSet.Tables.Count);

                if (dataSet.Tables.Contains("payment_header"))
                {
                    Log.Debug("Processing 'payment_header' sheet...");
                    ReadPaymentHeader(dataSet, model);
                }

                if (dataSet.Tables.Contains("claims"))
                {
                    Log.Debug("Processing 'claims' sheet...");
                    ReadClaims(dataSet, model);
                    Log.Information("Extracted {ClaimCount} claims.", model.Claims.Count);
                }

                if (dataSet.Tables.Contains("service_lines"))
                {
                    Log.Debug("Processing 'service_lines' sheet...");
                    ReadServiceLines(dataSet, model);
                }

                if (dataSet.Tables.Contains("adjustments"))
                {
                    Log.Debug("Processing 'adjustments' sheet...");
                    ReadAdjustments(dataSet, model, splitter);
                }

                if (dataSet.Tables.Contains("plb"))
                {
                    Log.Debug("Processing 'plb' (Provider Level Adjustments) sheet...");
                    ReadPlb(dataSet, model);
                    Log.Information("Extracted {PlbCount} provider adjustments.", model.ProviderAdjustments.Count);
                }
            }

            Log.Information("Excel data extraction complete for {FilePath}.", Path.GetFileName(filePath));
            return model;
        }

        private void ReadPaymentHeader(DataSet ds, Edi835DataModel model)
        {
            var table = ds.Tables["payment_header"];
            if (table == null || table.Rows.Count == 0) return;

            var row = table.Rows[0];

            model.Header = new HeaderData
            {
                PaymentId = GetString(row, "PaymentID"),
                PayerName = GetString(row, "PayerName"),
                PayerId = GetString(row, "PayerID"),
                PayerAddressLine1 = GetString(row, "PayerAddressLine1"),
                PayerCity = GetString(row, "PayerCity"),
                PayerState = GetString(row, "PayerState"),
                PayerZip = GetString(row, "PayerZip"),
                ProviderName = GetAnyString(row, "ProviderName", "PayeeName", "Payee_Name", "Provider_Name"),
                ProviderNpi = GetAnyString(row, "ProviderNPI", "PayeeNPI", "Payee_NPI", "Provider_NPI"),
                ProviderTaxId = GetAnyString(row, "ProviderTaxID", "PayeeTaxID", "ProviderTaxId", "PayeeTaxId"),
                ProviderAddressLine1 = GetAnyString(row, "ProviderAddressLine1", "PayeeAddressLine1", "Provider_Address", "Payee_Address"),
                ProviderCity = GetString(row, "ProviderCity"),
                ProviderState = GetString(row, "ProviderState"),
                ProviderZip = GetString(row, "ProviderZip"),
                PaymentMethod = GetString(row, "PaymentMethod"),
                PaymentDate = GetString(row, "PaymentDate"),
                CheckOrEftNumber = GetString(row, "CheckOrEFTNumber"),
                BankAccountNumber = GetString(row, "BankAccountNumber"),
                TotalPaymentAmount = GetDecimal(row, "TotalPaymentAmount", out string symbol),
                PayerEobType = GetString(row, "PayerEOBType"),
                CurrencySymbol = symbol,
                PayerCommunicationNumber = GetString(row, "PayerCommunicationNumber")
            };

            Log.Information("Payment Header processed: TraceNum={TraceNum}, Amount={Amount}", model.Header.CheckOrEftNumber, model.Header.TotalPaymentAmount);
        }

        private void ReadClaims(DataSet ds, Edi835DataModel model)
        {
            var table = ds.Tables["claims"];
            if (table == null) return;

            foreach (DataRow row in table.Rows)
            {
                var claimId = GetString(row, "ClaimID_Payer");
                if (string.IsNullOrWhiteSpace(claimId)) continue;

                Log.Information("[READER-DEBUG] Extracted claim with ClaimID_Payer='{ClaimId}' on PaymentID='{PaymentId}'", claimId, GetString(row, "PaymentID"));

                // Parse patient name (format: "Last, First")
                var patientName = GetString(row, "PatientName");
                var (lastName, firstName) = ParseName(patientName);

                model.Claims.Add(new ClaimData
                {
                    PaymentId = GetString(row, "PaymentID"),
                    ClaimIdPayer = claimId,
                    ClaimIdProvider = GetString(row, "ClaimID_Provider"),
                    ClaimType = GetString(row, "ClaimType"),
                    PatientName = patientName,
                    PatientLastName = lastName,
                    PatientFirstName = firstName,
                    PatientDob = GetString(row, "PatientDOB"),
                    PatientId = GetString(row, "PatientID"),
                    SubscriberName = GetString(row, "SubscriberName"),
                    SubscriberId = GetString(row, "SubscriberID"),
                    ProviderRenderingName = GetString(row, "ProviderRenderingName"),
                    ProviderRenderingNpi = GetString(row, "ProviderRenderingNPI"),
                    ClaimStatusCode = GetString(row, "ClaimStatusCode"),
                    ClaimBilledAmount = GetDecimal(row, "ClaimBilledAmount"),
                    ClaimAllowedAmount = GetDecimalNullable(row, "ClaimAllowedAmount"),
                    ClaimPaidAmount = GetDecimal(row, "ClaimPaidAmount"),
                    PatientResponsibilityAmount = GetDecimalNullable(row, "PatientResponsibilityAmount"),
                    ClaimServiceDateFrom = GetString(row, "ClaimServiceDateFrom"),
                    ClaimServiceDateTo = GetString(row, "ClaimServiceDateTo"),
                    ClaimRemarkCodes = GetString(row, "ClaimRemarkCodes")
                });
            }
        }

        private void ReadServiceLines(DataSet ds, Edi835DataModel model)
        {
            var table = ds.Tables["service_lines"];
            if (table == null) return;

            // Group claims by ClaimID_Payer for precise lookup
            var claimMap = model.Claims
                .Where(c => !string.IsNullOrWhiteSpace(c.ClaimIdPayer))
                .GroupBy(c => NormalizeId(c.ClaimIdPayer))
                .ToDictionary(g => g.Key, g => g.First());

            int svcCount = 0;

            foreach (DataRow row in table.Rows)
            {
                var claimId = GetString(row, "ClaimID_Payer");

                if (string.IsNullOrWhiteSpace(claimId))
                {
                    Log.Warning("[READER] Service line missing ClaimID_Payer. Skipping row.");
                    continue;
                }

                var svc = new ServiceLineData
                {
                    ClaimIdPayer = claimId,
                    ServiceLineId = GetAnyString(row, "ServiceLine_ID", "ServiceLineID", "Service_Line_ID"),
                    CptCode = GetString(row, "CPTCode"),
                    Modifier1 = GetString(row, "Modifier1"),
                    Modifier2 = GetString(row, "Modifier2"),
                    RevenueCode = GetString(row, "RevenueCode"),
                    NdcCode = GetString(row, "NDCCode"),
                    Units = GetString(row, "Units"),
                    LineServiceDateFrom = GetString(row, "LineServiceDateFrom"),
                    LineServiceDateTo = GetString(row, "LineServiceDateTo"),
                    LineBilledAmount = GetDecimal(row, "LineBilledAmount"),
                    LineAllowedAmount = GetDecimalNullable(row, "LineAllowedAmount"),
                    LinePaidAmount = GetDecimal(row, "LinePaidAmount"),
                    LinePatientResponsibilityAmount = GetDecimalNullable(row, "LinePatientResponsibilityAmount"),
                    LineRemarkCodes = GetString(row, "LineRemarkCodes"),
                    LineExplanationCodes = GetAnyString(row, "LineExplanationCodes", "Explanation")
                };

                CaptureMetadata(row, svc.Metadata, new[] { 
                    "ClaimID_Payer", "ServiceLine_ID", "ServiceLineID", "Service_Line_ID", 
                    "CPTCode", "Modifier1", "Modifier2", "RevenueCode", "NDCCode", "Units", 
                    "LineServiceDateFrom", "LineServiceDateTo", "LineBilledAmount", 
                    "LineAllowedAmount", "LinePaidAmount", "LinePatientResponsibilityAmount", 
                    "LineRemarkCodes", "LineExplanationCodes", "Explanation" 
                });

                var normalizedClaimId = NormalizeId(claimId);
                if (claimMap.TryGetValue(normalizedClaimId, out var claim))
                {
                    claim.ServiceLines.Add(svc);
                    svcCount++;
                }
                else
                {
                    Log.Warning("[READER] Could not find Claim '{ClaimId}' for service line.", claimId);
                }
            }
            Log.Information("Extracted {ServiceLineCount} service lines across claims.", svcCount);
        }

        private void ReadAdjustments(DataSet ds, Edi835DataModel model, CarcRarcSplitter? splitter = null)
        {
            var table = ds.Tables["adjustments"];
            if (table == null) return;

            // Group claims by PaymentID
            var claimsByPayment = model.Claims
                .Where(c => !string.IsNullOrWhiteSpace(c.PaymentId))
                .GroupBy(c => NormalizeId(c.PaymentId))
                .ToDictionary(g => g.Key, g => g.ToList());

            int adjCount = 0;

            foreach (DataRow row in table.Rows)
            {
                var level = GetString(row, "AdjustmentLevel").ToUpperInvariant();
                var paymentId = GetString(row, "PaymentID");

                if (string.IsNullOrWhiteSpace(paymentId))
                {
                    Log.Warning("[READER] Adjustment missing PaymentID. Row Level={Level}", level);
                    continue;
                }

                var normalizedPaymentId = NormalizeId(paymentId);

                if (!claimsByPayment.TryGetValue(normalizedPaymentId, out var claimsForPayment) || claimsForPayment.Count == 0)
                {
                    Log.Warning("[READER] Adjustment orphaned: No claims found for PaymentID '{PaymentId}'.", paymentId);
                    continue;
                }

                var rawReason = GetString(row, "AdjustmentReasonCode");
                var finalReason = rawReason;
                string extraRarcs = string.Empty;

                if (splitter != null && !string.IsNullOrEmpty(rawReason))
                {
                    var (carc, rarc) = splitter.Split(rawReason);
                    finalReason = carc;
                    extraRarcs = rarc;
                }

                var adj = new AdjustmentData
                {
                    AdjustmentLevel = level,
                    PaymentId = paymentId,
                    ServiceLineId = GetAnyString(row, "ServiceLine_ID", "ServiceLineID", "Service_Line_ID"),
                    AdjustmentGroupCode = GetString(row, "AdjustmentGroupCode"),
                    AdjustmentReasonCode = finalReason,
                    AdjustmentAmount = GetDecimal(row, "AdjustmentAmount"),
                    Quantity = HasColumn(row, "Quantity") ? GetDecimalNullable(row, "Quantity") : null,
                    DeductibleAmount = GetDecimalNullable(row, "DeductibleAmount"),
                    CoinsuranceAmount = GetDecimalNullable(row, "CoinsuranceAmount"),
                    CopayAmount = GetDecimalNullable(row, "CopayAmount"),
                    OtherInsuranceAmount = GetDecimalNullable(row, "OtherInsuranceAmount"),
                    SequestrationAmount = GetDecimalNullable(row, "SequestrationAmount"),
                    RemarkCode = GetAnyString(row, "RemarkCode", "Remark Code", "RARC"),
                    Explanation = GetAnyString(row, "Explanation", "Explanation Code", "Description")
                };

                CaptureMetadata(row, adj.Metadata, new[] {
                    "AdjustmentLevel", "PaymentID", "ServiceLine_ID", "ServiceLineID", "Service_Line_ID",
                    "AdjustmentGroupCode", "AdjustmentReasonCode", "AdjustmentAmount", "Quantity",
                    "DeductibleAmount", "CoinsuranceAmount", "CopayAmount", "OtherInsuranceAmount",
                    "SequestrationAmount", "RemarkCode", "Remark Code", "RARC", "Explanation", 
                    "Explanation Code", "Description", "ClaimID_Payer"
                });

                var firstClaim = claimsForPayment.First();
                adj.ClaimIdPayer = firstClaim.ClaimIdPayer;

                if (level == "CLAIM")
                {
                    if (adj.AdjustmentAmount == 0)
                    {
                        adj.AdjustmentAmount = firstClaim.ClaimBilledAmount - (firstClaim.ClaimAllowedAmount ?? 0);
                    }

                    if (!string.IsNullOrEmpty(extraRarcs))
                    {
                        Log.Information("[READER] Appending recovered RARCs '{RARCs}' to Claim Remark Codes.", extraRarcs);
                        firstClaim.ClaimRemarkCodes = string.IsNullOrEmpty(firstClaim.ClaimRemarkCodes) 
                            ? extraRarcs 
                            : firstClaim.ClaimRemarkCodes + ", " + extraRarcs;
                    }

                    firstClaim.ClaimAdjustments.Add(adj);
                    adjCount++;
                }
                else if (level == "SERVICE_LINE")
                {
                    string svcId = adj.ServiceLineId;
                    string cpt = string.Empty;
                    if (HasColumn(row, "CPTCode"))
                    {
                        cpt = GetString(row, "CPTCode");
                        adj.CptCode = cpt;
                    }

                    ServiceLineData? matchingSvc = null;
                    ClaimData? actualClaim = null;

                    // 1. Try to match by ServiceLine_ID
                    if (!string.IsNullOrWhiteSpace(svcId))
                    {
                        foreach (var claim in claimsForPayment)
                        {
                            matchingSvc = claim.ServiceLines.FirstOrDefault(s => s.ServiceLineId.Equals(svcId, StringComparison.OrdinalIgnoreCase));
                            if (matchingSvc != null)
                            {
                                actualClaim = claim;
                                break;
                            }
                        }
                    }

                    // 2. Fallback to match by CPTCode if ServiceLine_ID is missing or doesn't match
                    if (matchingSvc == null && !string.IsNullOrEmpty(cpt))
                    {
                        foreach (var claim in claimsForPayment)
                        {
                            matchingSvc = claim.ServiceLines.FirstOrDefault(s => s.CptCode.Equals(cpt, StringComparison.OrdinalIgnoreCase));
                            if (matchingSvc != null)
                            {
                                actualClaim = claim;
                                break;
                            }
                        }
                    }

                    // 3. Fallback to first line if no match
                    if (matchingSvc == null)
                    {
                        actualClaim = firstClaim;
                        matchingSvc = actualClaim.ServiceLines.FirstOrDefault();
                    }

                    if (matchingSvc != null && actualClaim != null)
                    {
                        adj.ClaimIdPayer = actualClaim.ClaimIdPayer;

                        if (!string.IsNullOrEmpty(extraRarcs))
                        {
                            Log.Information("[READER] Appending recovered RARCs '{RARCs}' to Service Line Remark Codes.", extraRarcs);
                            matchingSvc.LineRemarkCodes = string.IsNullOrEmpty(matchingSvc.LineRemarkCodes) 
                                ? extraRarcs 
                                : matchingSvc.LineRemarkCodes + ", " + extraRarcs;
                        }

                        if (adj.AdjustmentAmount == 0)
                        {
                            adj.AdjustmentAmount = matchingSvc.LineBilledAmount - (matchingSvc.LineAllowedAmount ?? 0);
                        }

                        matchingSvc.Adjustments.Add(adj);
                        adjCount++;
                    }
                    else
                    {
                        // Fall back to claim level entirely if absolutely no service lines exist
                        actualClaim = firstClaim;
                        adj.ClaimIdPayer = actualClaim.ClaimIdPayer;

                        if (adj.AdjustmentAmount == 0)
                        {
                            adj.AdjustmentAmount = actualClaim.ClaimBilledAmount - (actualClaim.ClaimAllowedAmount ?? 0);
                        }

                        Log.Warning("[READER] Service line adjustment fallback: No service lines found for Payment '{PaymentId}'. Attaching to claim level.", paymentId);
                        actualClaim.ClaimAdjustments.Add(adj);
                        adjCount++;
                    }
                }
                else
                {
                    Log.Warning("[READER] Unknown adjustment level '{Level}' for Payment '{PaymentId}'. Skipping.", level, paymentId);
                }
            }
            Log.Information("Extracted {AdjustmentCount} adjustments.", adjCount);
        }

        private void ReadPlb(DataSet ds, Edi835DataModel model)
        {
            var table = ds.Tables["plb"];
            if (table == null) return;

            foreach (DataRow row in table.Rows)
            {
                var providerId = GetString(row, "ProviderIdentifier");
                if (string.IsNullOrWhiteSpace(providerId)) continue;

                model.ProviderAdjustments.Add(new ProviderAdjustmentData
                {
                    PaymentId = GetString(row, "PaymentID"),
                    ProviderIdentifier = providerId,
                    PlbReasonCode = GetString(row, "PLBReasonCode"),
                    PlbAmount = GetDecimal(row, "PLBAmount"),
                    FiscalPeriodDate = GetString(row, "FiscalPeriodDate")
                });
            }
        }

        #region Helper Methods

        private static string GetString(DataRow row, string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return string.Empty;
            if (!HasColumn(row, columnName)) return string.Empty;
            return row[columnName]?.ToString()?.Trim() ?? string.Empty;
        }

        private static string GetAnyString(DataRow row, params string[] columnNames)
        {
            foreach (var name in columnNames)
            {
                var val = GetString(row, name);
                if (!string.IsNullOrEmpty(val)) return val;
            }
            return string.Empty;
        }

        private static decimal GetDecimal(DataRow row, string columnName)
        {
            return GetDecimal(row, columnName, out _);
        }

        private static decimal GetDecimal(DataRow row, string columnName, out string currencySymbol)
        {
            currencySymbol = string.Empty;
            var raw = GetString(row, columnName);
            if (string.IsNullOrWhiteSpace(raw)) return 0m;

            raw = raw.Trim();
            if (raw.Length > 0 && !char.IsDigit(raw[0]) && raw[0] != '-')
            {
                currencySymbol = raw[0].ToString();
                raw = raw.Substring(1);
            }

            raw = raw.Replace("$", "").Replace(",", "").Trim();
            return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : 0m;
        }

        private static decimal? GetDecimalNullable(DataRow row, string columnName)
        {
            var raw = GetString(row, columnName);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Replace("$", "").Replace(",", "").Trim();
            return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : (decimal?)null;
        }

        private static bool HasColumn(DataRow row, string colName)
        {
            return row.Table.Columns.Contains(colName) && row[colName] != DBNull.Value;
        }

        private static void CaptureMetadata(DataRow row, IDictionary<string, string> metadata, string[] knownColumns)
        {
            if (row == null || metadata == null) return;
            var knownSet = new HashSet<string>(knownColumns, StringComparer.OrdinalIgnoreCase);
            
            foreach (DataColumn col in row.Table.Columns)
            {
                if (!knownSet.Contains(col.ColumnName))
                {
                    string val = row[col]?.ToString()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(val))
                    {
                        metadata[col.ColumnName] = val;
                    }
                }
            }
        }

        private static (string LastName, string FirstName) ParseName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return (string.Empty, string.Empty);

            var parts = fullName.Split(new[] { ',' }, 2);
            if (parts.Length >= 2)
            {
                return (parts[0].Trim(), parts[1].Trim());
            }

            var spaceParts = fullName.Split(new[] { ' ' }, 2);
            if (spaceParts.Length >= 2)
            {
                return (spaceParts[1].Trim(), spaceParts[0].Trim());
            }

            return (fullName.Trim(), string.Empty);
        }

        private static string NormalizeId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return string.Empty;

            // 1. Remove non-alphanumeric (dashes, dots, spaces)
            string normalized = new string(id.Where(c => char.IsLetterOrDigit(c)).ToArray());

            // 2. Remove leading zeros
            normalized = normalized.TrimStart('0');

            // 3. To Upper
            return normalized.ToUpperInvariant();
        }

        #endregion
    }
}

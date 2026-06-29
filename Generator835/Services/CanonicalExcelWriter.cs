using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Edi.Generator835.Models;
using Serilog;

namespace Edi.Generator835.Services
{
    /// <summary>
    /// Writes the fully normalized and balanced data model back to the Excel file.
    /// This creates an inspectable "canonical model" — open the Excel after processing
    /// to see exactly what CAGC, CARC, remarks, and amounts were resolved.
    /// </summary>
    public class CanonicalExcelWriter
    {
        /// <summary>
        /// Writes resolved model data back to the Excel file, updating the adjustments
        /// sheet and service_lines sheet with normalized, balanced values.
        /// </summary>
        public void WriteBack(string excelPath, Edi835DataModel model)
        {
            if (!File.Exists(excelPath))
            {
                Log.Warning("[CanonicalWriter] Excel file not found: {Path}", excelPath);
                return;
            }

            try
            {
                using (var workbook = new XLWorkbook(excelPath))
                {
                    WriteAdjustmentsSheet(workbook, model);
                    WriteServiceLinesSheet(workbook, model);
                    WriteClaimsSheet(workbook, model);

                    workbook.Save();
                    Log.Information("[CanonicalWriter] Canonical Excel saved to {Path}", excelPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CanonicalWriter] Failed to write canonical Excel: {Message}", ex.Message);
                throw;
            }
        }

        private void WriteAdjustmentsSheet(XLWorkbook workbook, Edi835DataModel model)
        {
            // Try to find the adjustments worksheet
            var ws = FindWorksheet(workbook, "adjustments", "adjustment", "adj");
            if (ws == null)
            {
                Log.Debug("[CanonicalWriter] No adjustments sheet found. Skipping.");
                return;
            }

            // Build column index map from header row
            var colMap = BuildColumnMap(ws, 1);
            int dataStartRow = 2;

            // Clear existing data rows (keep header)
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
            if (lastRow > 1)
            {
                ws.Range(dataStartRow, 1, lastRow, lastCol).Delete(XLShiftDeletedCells.ShiftCellsUp);
            }

            // --- Root Fix: Dynamic Column Header Creation ---
            var allMetadataKeys = model.Claims
                .SelectMany(c => c.ClaimAdjustments.SelectMany(a => a.Metadata.Keys)
                    .Concat(c.ServiceLines.SelectMany(l => l.Adjustments.SelectMany(a => a.Metadata.Keys))))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var key in allMetadataKeys)
            {
                if (!colMap.ContainsKey(key))
                {
                    int nextCol = (colMap.Values.Count > 0 ? colMap.Values.Max() : 0) + 1;
                    ws.Cell(1, nextCol).Value = key;
                    colMap[key] = nextCol;
                    Log.Debug("[CanonicalWriter] Added dynamic metadata column: '{Key}' at index {Index}", key, nextCol);
                }
            }

            // Write all adjustments from all claims (both claim-level and service-line-level)
            int row = dataStartRow;
            foreach (var claim in model.Claims)
            {
                // 1. Claim-level adjustments
                foreach (var adj in claim.ClaimAdjustments)
                {
                    if (string.IsNullOrEmpty(adj.AdjustmentGroupCode) && adj.AdjustmentAmount == 0) continue;

                    SetCell(ws, row, colMap, "PaymentID", adj.PaymentId);
                    SetCell(ws, row, colMap, "ClaimID_Payer", adj.ClaimIdPayer);
                    SetCell(ws, row, colMap, "ServiceLine_ID", ""); // Blank for claim level
                    SetCell(ws, row, colMap, "CptCode", "");
                    SetCell(ws, row, colMap, "AdjustmentGroupCode", adj.AdjustmentGroupCode);
                    SetCell(ws, row, colMap, "AdjustmentReasonCode", adj.AdjustmentReasonCode);
                    SetCell(ws, row, colMap, "AdjustmentAmount", adj.AdjustmentAmount.ToString("F2"));
                    SetCell(ws, row, colMap, "AdjustmentLevel", "CLAIM");

                    SetCell(ws, row, colMap, "DeductibleAmount", adj.DeductibleAmount?.ToString("F2") ?? "");
                    SetCell(ws, row, colMap, "CoinsuranceAmount", adj.CoinsuranceAmount?.ToString("F2") ?? "");
                    SetCell(ws, row, colMap, "CopayAmount", adj.CopayAmount?.ToString("F2") ?? "");
                    SetCell(ws, row, colMap, "OtherInsuranceAmount", adj.OtherInsuranceAmount?.ToString("F2") ?? "");
                    SetCell(ws, row, colMap, "SequestrationAmount", adj.SequestrationAmount?.ToString("F2") ?? "");
                    if (adj.Quantity.HasValue) SetCell(ws, row, colMap, "Quantity", adj.Quantity.Value.ToString("F2"));
                    SetCell(ws, row, colMap, "RemarkCode", adj.RemarkCode);
                    SetCell(ws, row, colMap, "Explanation", adj.Explanation);

                    // Write dynamic metadata
                    foreach (var kvp in adj.Metadata)
                    {
                        SetCell(ws, row, colMap, kvp.Key, kvp.Value);
                    }

                    row++;
                }

                // 2. Service-line level adjustments
                foreach (var line in claim.ServiceLines)
                {
                    foreach (var adj in line.Adjustments)
                    {
                        if (string.IsNullOrEmpty(adj.AdjustmentGroupCode) && adj.AdjustmentAmount == 0) continue;

                        SetCell(ws, row, colMap, "PaymentID", adj.PaymentId);
                        SetCell(ws, row, colMap, "ClaimID_Payer", adj.ClaimIdPayer);
                        // Robustness: use parent line ID if available
                        SetCell(ws, row, colMap, "ServiceLine_ID", line.ServiceLineId);
                        SetCell(ws, row, colMap, "CptCode", line.CptCode);
                        SetCell(ws, row, colMap, "AdjustmentGroupCode", adj.AdjustmentGroupCode);
                        SetCell(ws, row, colMap, "AdjustmentReasonCode", adj.AdjustmentReasonCode);
                        SetCell(ws, row, colMap, "AdjustmentAmount", adj.AdjustmentAmount.ToString("F2"));
                        SetCell(ws, row, colMap, "AdjustmentLevel", "SERVICE_LINE");

                        SetCell(ws, row, colMap, "DeductibleAmount", adj.DeductibleAmount?.ToString("F2") ?? "");
                        SetCell(ws, row, colMap, "CoinsuranceAmount", adj.CoinsuranceAmount?.ToString("F2") ?? "");
                        SetCell(ws, row, colMap, "CopayAmount", adj.CopayAmount?.ToString("F2") ?? "");
                        SetCell(ws, row, colMap, "OtherInsuranceAmount", adj.OtherInsuranceAmount?.ToString("F2") ?? "");
                        SetCell(ws, row, colMap, "SequestrationAmount", adj.SequestrationAmount?.ToString("F2") ?? "");
                        if (adj.Quantity.HasValue) SetCell(ws, row, colMap, "Quantity", adj.Quantity.Value.ToString("F2"));
                        SetCell(ws, row, colMap, "RemarkCode", adj.RemarkCode);
                        SetCell(ws, row, colMap, "Explanation", adj.Explanation);

                        // Write dynamic metadata
                        foreach (var kvp in adj.Metadata)
                        {
                            SetCell(ws, row, colMap, kvp.Key, kvp.Value);
                        }

                        row++;
                    }
                }
            }

            Log.Information("[CanonicalWriter] Wrote {Count} adjustment rows to '{Sheet}'", row - dataStartRow, ws.Name);
        }

        private void WriteClaimsSheet(XLWorkbook workbook, Edi835DataModel model)
        {
            var ws = FindWorksheet(workbook, "claims", "claim");
            if (ws == null)
            {
                Log.Debug("[CanonicalWriter] No claims sheet found. Skipping.");
                return;
            }

            var colMap = BuildColumnMap(ws, 1);
            int dataStartRow = 2;

            // Update existing claim rows
            int row = dataStartRow;
            foreach (var claim in model.Claims)
            {
                SetCell(ws, row, colMap, "ClaimID_Payer", claim.ClaimIdPayer);
                SetCell(ws, row, colMap, "ClaimID_Provider", claim.ClaimIdProvider);
                SetCell(ws, row, colMap, "ClaimBilledAmount", claim.ClaimBilledAmount.ToString("F2"));
                SetCell(ws, row, colMap, "ClaimPaidAmount", claim.ClaimPaidAmount.ToString("F2"));
                // This is the key update: write back the validated/corrected PR amount
                SetCell(ws, row, colMap, "PatientResponsibilityAmount", claim.PatientResponsibilityAmount?.ToString("F2") ?? "");
                row++;
            }

            Log.Information("[CanonicalWriter] Updated {Count} claim rows in '{Sheet}'", row - dataStartRow, ws.Name);
        }

        private void WriteServiceLinesSheet(XLWorkbook workbook, Edi835DataModel model)
        {
            var ws = FindWorksheet(workbook, "service_lines", "servicelines", "svc", "service");
            if (ws == null)
            {
                Log.Debug("[CanonicalWriter] No service_lines sheet found. Skipping.");
                return;
            }

            var colMap = BuildColumnMap(ws, 1);
            int dataStartRow = 2;

            // Don't clear service lines — just update existing rows with clean values
            int row = dataStartRow;
            foreach (var claim in model.Claims)
            {
                foreach (var line in claim.ServiceLines)
                {
                    SetCell(ws, row, colMap, "ServiceLine_ID", line.ServiceLineId);
                    SetCell(ws, row, colMap, "CptCode", line.CptCode);
                    SetCell(ws, row, colMap, "Modifier1", line.Modifier1);
                    SetCell(ws, row, colMap, "Modifier2", line.Modifier2);
                    SetCell(ws, row, colMap, "LineBilledAmount", line.LineBilledAmount.ToString("F2"));
                    SetCell(ws, row, colMap, "LinePaidAmount", line.LinePaidAmount.ToString("F2"));
                    SetCell(ws, row, colMap, "LineAllowedAmount", line.LineAllowedAmount?.ToString("F2") ?? "");
                    SetCell(ws, row, colMap, "LinePatientResponsibilityAmount", line.LinePatientResponsibilityAmount?.ToString("F2") ?? "");
                    SetCell(ws, row, colMap, "LineRemarkCodes", line.LineRemarkCodes);
                    SetCell(ws, row, colMap, "LineExplanationCodes", line.LineExplanationCodes);
                    row++;
                }
            }

            Log.Information("[CanonicalWriter] Updated {Count} service line rows in '{Sheet}'", row - dataStartRow, ws.Name);
        }

        /// <summary>
        /// Finds a worksheet by trying multiple possible names (case-insensitive).
        /// </summary>
        private static IXLWorksheet? FindWorksheet(XLWorkbook workbook, params string[] possibleNames)
        {
            foreach (var name in possibleNames)
            {
                var ws = workbook.Worksheets.FirstOrDefault(w =>
                    w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (ws != null) return ws;
            }
            return null;
        }

        /// <summary>
        /// Builds a column name → column number map from the header row.
        /// </summary>
        private static System.Collections.Generic.Dictionary<string, int> BuildColumnMap(IXLWorksheet ws, int headerRow)
        {
            var map = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

            for (int col = 1; col <= lastCol; col++)
            {
                var value = ws.Cell(headerRow, col).GetString()?.Trim();
                if (!string.IsNullOrEmpty(value) && !map.ContainsKey(value!))
                {
                    map[value!] = col;
                }
            }

            return map;
        }

        /// <summary>
        /// Sets a cell value by column name. No-op if column doesn't exist.
        /// </summary>
        private static void SetCell(IXLWorksheet ws, int row, System.Collections.Generic.Dictionary<string, int> colMap, string colName, string value)
        {
            if (colMap.TryGetValue(colName, out int col))
            {
                ws.Cell(row, col).SetValue(value ?? string.Empty);
            }
        }
    }
}

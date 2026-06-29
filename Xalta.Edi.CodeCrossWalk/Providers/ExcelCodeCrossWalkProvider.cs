using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Xalta.Edi.CodeCrossWalk.Interfaces;

namespace Xalta.Edi.CodeCrossWalk.Providers
{
    /// <summary>
    /// Excel-based provider for code cross walk mappings.
    /// Supports two formats:
    /// 1. Single sheet with "Lookup_Table" column
    /// 2. Multiple sheets where each sheet name is the table name
    /// </summary>
    public class ExcelCodeCrossWalkProvider : ICodeCrossWalkProvider
    {
        private readonly string _filePath;
        private readonly bool _isMultiSheet;
        private readonly bool _caseSensitive;

        /// <summary>
        /// Constructor for Excel provider.
        /// </summary>
        /// <param name="filePath">Path to the Excel file.</param>
        /// <param name="isMultiSheet">If true, treats each sheet as a separate table. If false, expects a single sheet with "Lookup_Table" column.</param>
        /// <param name="caseSensitive">If true, lookups will be case-sensitive.</param>
        public ExcelCodeCrossWalkProvider(string filePath, bool isMultiSheet = false, bool caseSensitive = false)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Excel file not found: {filePath}");

            _filePath = filePath;
            _isMultiSheet = isMultiSheet;
            _caseSensitive = caseSensitive;
        }

        public Dictionary<string, Dictionary<string, string>> LoadMappings()
        {
            var mappings = new Dictionary<string, Dictionary<string, string>>(
                _caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

            using (var workbook = new XLWorkbook(_filePath))
            {
                if (_isMultiSheet)
                {
                    LoadMultiSheetMappings(workbook, mappings);
                }
                else
                {
                    LoadSingleSheetMappings(workbook, mappings);
                }
            }

            return mappings;
        }

        private void LoadMultiSheetMappings(XLWorkbook workbook, Dictionary<string, Dictionary<string, string>> mappings)
        {
            foreach (var worksheet in workbook.Worksheets)
            {
                var tableName = worksheet.Name;

                // Skip empty sheets
                if (worksheet.RangeUsed() == null)
                    continue;

                var tableData = new Dictionary<string, string>(
                    _caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

                var range = worksheet.RangeUsed();
                var firstRow = range.FirstRow();

                // Find column indices
                int inputColIndex = -1;
                int outputColIndex = -1;

                for (int col = 1; col <= firstRow.CellCount(); col++)
                {
                    var header = firstRow.Cell(col).GetString().Trim();

                    // Match column names flexibly - handle "Extracted_Value (Input)" or just "Extracted_Value"
                    if (header.StartsWith("Extracted_Value", StringComparison.OrdinalIgnoreCase) ||
                        header.Equals("Input", StringComparison.OrdinalIgnoreCase) ||
                        header.Equals("Reason Code", StringComparison.OrdinalIgnoreCase) ||
                        header.Equals("ReasonCode", StringComparison.OrdinalIgnoreCase) ||
                        header.Equals("CARC", StringComparison.OrdinalIgnoreCase))
                    {
                        inputColIndex = col;
                    }
                    else if (header.StartsWith("EDI_Code", StringComparison.OrdinalIgnoreCase) ||
                             header.Equals("Output", StringComparison.OrdinalIgnoreCase) ||
                             header.Equals("Group Code", StringComparison.OrdinalIgnoreCase) ||
                             header.Equals("GroupCode", StringComparison.OrdinalIgnoreCase) ||
                             header.Equals("GAGC", StringComparison.OrdinalIgnoreCase))
                    {
                        outputColIndex = col;
                    }
                }

                if (inputColIndex == -1 || outputColIndex == -1)
                {
                    // Skip sheet if it doesn't look like a mapping table
                    continue;
                }

                // Read data rows
                for (int row = 2; row <= range.RowCount(); row++)
                {
                    var inputValue = range.Cell(row, inputColIndex).GetString().Trim();
                    var outputCode = range.Cell(row, outputColIndex).GetString().Trim();

                    if (!string.IsNullOrWhiteSpace(inputValue) && !string.IsNullOrWhiteSpace(outputCode))
                    {
                        // Use last occurrence if duplicates exist
                        tableData[inputValue] = outputCode;
                    }
                }

                if (tableData.Count > 0)
                {
                    mappings[tableName] = tableData;
                }
            }
        }

        private void LoadSingleSheetMappings(XLWorkbook workbook, Dictionary<string, Dictionary<string, string>> mappings)
        {
            var worksheet = workbook.Worksheet(1);

            if (worksheet.RangeUsed() == null)
                throw new InvalidOperationException("The Excel file is empty.");

            var range = worksheet.RangeUsed();
            var firstRow = range.FirstRow();

            // Find column indices
            int tableColIndex = -1;
            int inputColIndex = -1;
            int outputColIndex = -1;

            for (int col = 1; col <= firstRow.CellCount(); col++)
            {
                var header = firstRow.Cell(col).GetString().Trim();

                // Match column names flexibly - handle "Lookup_Table" or just "Table"
                if (header.StartsWith("Lookup_Table", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("Table", StringComparison.OrdinalIgnoreCase))
                {
                    tableColIndex = col;
                }
                else if (header.StartsWith("Extracted_Value", StringComparison.OrdinalIgnoreCase) ||
                         header.Equals("Input", StringComparison.OrdinalIgnoreCase))
                {
                    inputColIndex = col;
                }
                else if (header.StartsWith("EDI_Code", StringComparison.OrdinalIgnoreCase) ||
                         header.Equals("Output", StringComparison.OrdinalIgnoreCase))
                {
                    outputColIndex = col;
                }
            }

            if (tableColIndex == -1 || inputColIndex == -1 || outputColIndex == -1)
            {
                throw new InvalidOperationException(
                    "Single sheet mode requires 'Lookup_Table' (or 'Table'), 'Extracted_Value' (or 'Input'), and 'EDI_Code' (or 'Output') columns.");
            }

            // Read data rows
            for (int row = 2; row <= range.RowCount(); row++)
            {
                var tableName = range.Cell(row, tableColIndex).GetString().Trim();
                var inputValue = range.Cell(row, inputColIndex).GetString().Trim();
                var outputCode = range.Cell(row, outputColIndex).GetString().Trim();

                if (string.IsNullOrWhiteSpace(tableName) ||
                    string.IsNullOrWhiteSpace(inputValue) ||
                    string.IsNullOrWhiteSpace(outputCode))
                {
                    continue;
                }

                if (!mappings.ContainsKey(tableName))
                {
                    mappings[tableName] = new Dictionary<string, string>(
                        _caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
                }

                // Use last occurrence if duplicates exist
                mappings[tableName][inputValue] = outputCode;
            }
        }
    }
}

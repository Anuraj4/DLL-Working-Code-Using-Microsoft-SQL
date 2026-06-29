using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Xalta.Edi.AddressParser.Interfaces;
using Xalta.Edi.AddressParser.Models;

namespace Xalta.Edi.AddressParser.Providers
{
    public class ExcelAddressDataProvider : IAddressDataProvider
    {
        private readonly string _filePath;

        public ExcelAddressDataProvider(string filePath = "output/AddressData.xlsx")
        {
            _filePath = filePath;
        }

        public Task<IEnumerable<StateData>> GetStatesAsync()
        {
            if (!File.Exists(_filePath))
                return Task.FromResult(Enumerable.Empty<StateData>());

            using (var workbook = new XLWorkbook(_filePath))
            {
                var worksheet = workbook.Worksheet("States");
                if (worksheet == null) return Task.FromResult(Enumerable.Empty<StateData>());

                var states = worksheet.RowsUsed().Skip(1) // Skip header
                    .Select(row => new StateData
                    {
                        Name = row.Cell(1).GetString(),
                        StateCode = row.Cell(2).GetString()
                    }).ToList();

                return Task.FromResult<IEnumerable<StateData>>(states);
            }
        }

        public Task<IEnumerable<string>> GetCitiesAsync(string state)
        {
            if (!File.Exists(_filePath))
                return Task.FromResult(Enumerable.Empty<string>());

            using (var workbook = new XLWorkbook(_filePath))
            {
                var worksheet = workbook.Worksheet("Cities");
                if (worksheet == null) return Task.FromResult(Enumerable.Empty<string>());

                // If state is provided, we might want to filter, but user said store all cities.
                // For now, let's just return all cities from the sheet.
                var cities = worksheet.RowsUsed().Skip(1)
                    .Select(row => row.Cell(1).GetString())
                    .ToList();

                return Task.FromResult<IEnumerable<string>>(cities);
            }
        }

        public void SaveData(IEnumerable<StateData> states, IEnumerable<string> cities)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var workbook = new XLWorkbook())
            {
                var stateSheet = workbook.Worksheets.Add("States");
                stateSheet.Cell(1, 1).Value = "Name";
                stateSheet.Cell(1, 2).Value = "StateCode";

                int stateRow = 2;
                foreach (var state in states)
                {
                    stateSheet.Cell(stateRow, 1).Value = state.Name;
                    stateSheet.Cell(stateRow, 2).Value = state.StateCode;
                    stateRow++;
                }

                var citySheet = workbook.Worksheets.Add("Cities");
                citySheet.Cell(1, 1).Value = "CityName";

                int cityRow = 2;
                foreach (var city in cities)
                {
                    citySheet.Cell(cityRow, 1).Value = city;
                    cityRow++;
                }

                workbook.SaveAs(_filePath);
            }
        }
    }
}

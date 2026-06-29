using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xalta.Edi.AddressParser.Models;
using Xalta.Edi.AddressParser.Providers;

namespace Xalta.Edi.AddressParser.Services
{
    public class AddressDataService
    {
        private readonly CountriesNowApiProvider _apiProvider;
        private readonly ExcelAddressDataProvider _excelProvider;

        private IEnumerable<StateData>? _states;
        private IEnumerable<string>? _cities;

        public AddressDataService(CountriesNowApiProvider apiProvider, ExcelAddressDataProvider excelProvider)
        {
            _apiProvider = apiProvider;
            _excelProvider = excelProvider;
        }

        public async Task InitializeAsync()
        {
            // Try loading from Excel first
            _states = await _excelProvider.GetStatesAsync();
            _cities = await _excelProvider.GetCitiesAsync("United States");

            if (!_states.Any() || !_cities.Any())
            {
                // If excel is empty, fetch from API
                _states = await _apiProvider.GetStatesAsync();
                _cities = await _apiProvider.GetCitiesAsync("United States");

                // Save to Excel for future use
                _excelProvider.SaveData(_states, _cities);
            }
        }

        public IEnumerable<StateData> GetStates()
        {
            return _states ?? Enumerable.Empty<StateData>();
        }

        public IEnumerable<string> GetCities()
        {
            return _cities ?? Enumerable.Empty<string>();
        }
    }
}

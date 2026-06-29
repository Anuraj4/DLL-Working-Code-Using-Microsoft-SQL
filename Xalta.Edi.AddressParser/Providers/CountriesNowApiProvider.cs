using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xalta.Edi.AddressParser.Interfaces;
using Xalta.Edi.AddressParser.Models;

namespace Xalta.Edi.AddressParser.Providers
{
    public class CountriesNowApiProvider : IAddressDataProvider
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://countriesnow.space/api/v0.1/countries/";

        public CountriesNowApiProvider(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<IEnumerable<StateData>> GetStatesAsync()
        {
            var payload = new { country = "United States" };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(BaseUrl + "states", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(json);

            if (result["error"]?.Value<bool>() == true)
                throw new Exception($"API Error: {result["msg"]}");

            return result["data"]?["states"]?.Select(s => new StateData
            {
                Name = s["name"]?.ToString() ?? string.Empty,
                StateCode = s["state_code"]?.ToString() ?? string.Empty
            }) ?? Enumerable.Empty<StateData>();
        }

        public async Task<IEnumerable<string>> GetCitiesAsync(string state)
        {
            // The API documentation for countriesnow suggests cities can be fetched by country OR country and state.
            // Based on user request, they provided a payload with country.
            // If they want cities for a specific state, we might need a different endpoint or payload.
            // Let's assume fetching all cities for US first as per their example output.

            var payload = new { country = "United States" };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(BaseUrl + "cities", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(json);

            if (result["error"]?.Value<bool>() == true)
                throw new Exception($"API Error: {result["msg"]}");

            return result["data"]?.Select(c => c.ToString()) ?? Enumerable.Empty<string>();
        }
    }
}

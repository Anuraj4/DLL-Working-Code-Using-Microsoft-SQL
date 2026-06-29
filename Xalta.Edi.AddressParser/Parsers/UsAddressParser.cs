using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xalta.Edi.AddressParser.Interfaces;
using Xalta.Edi.AddressParser.Models;
using Xalta.Edi.AddressParser.Services;
using Xalta.Edi.AddressParser.Utilities;

namespace Xalta.Edi.AddressParser.Parsers
{
    public class UsAddressParser : IAddressParser
    {
        private readonly AddressDataService _dataService;
        private Regex? _stateRegex;
        private Dictionary<string, string>? _cityMap;
        private List<(string Original, string Normalized)>? _normalizedCities;
        private bool _isInitialized = false;
        private const double FuzzyThreshold = 0.90;

        public UsAddressParser(AddressDataService dataService)
        {
            _dataService = dataService;
        }

        private void Initialize()
        {
            if (_isInitialized) return;

            var states = _dataService.GetStates().ToList();
            if (states.Any())
            {
                // Build a combined regex for state codes and names (e.g., \b(NY|New York|CA|California)\b$)
                var patterns = states.Select(s => Regex.Escape(s.StateCode))
                    .Concat(states.Select(s => Regex.Escape(s.Name)))
                    .OrderByDescending(p => p.Length);

                var combinedPattern = $@"\b({string.Join("|", patterns)})\b$";
                _stateRegex = new Regex(combinedPattern, RegexOptions.IgnoreCase);
            }

            var cities = _dataService.GetCities();
            if (cities.Any())
            {
                _cityMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _normalizedCities = new List<(string Original, string Normalized)>();
                foreach (var city in cities)
                {
                    if (!_cityMap.ContainsKey(city))
                    {
                        _cityMap[city] = city;
                        _normalizedCities.Add((city, AddressNormalization.NormalizeForComparison(city)));
                    }
                }
            }

            _isInitialized = true;
        }

        public ParsedAddress Parse(string rawAddress)
        {
            if (string.IsNullOrWhiteSpace(rawAddress))
                return new ParsedAddress();

            Initialize();

            var result = new ParsedAddress();
            var input = rawAddress.Trim();

            // 0. Remove Country Suffixes (e.g., USA, United States)
            var countrySuffixes = new[] { "USA", "U.S.A.", "United States", "U.S.", "U S A" };
            foreach (var suffix in countrySuffixes)
            {
                var suffixRegex = new Regex($@"\b{Regex.Escape(suffix)}\b\s*$", RegexOptions.IgnoreCase);
                if (suffixRegex.IsMatch(input))
                {
                    input = suffixRegex.Replace(input, "").Trim().TrimEnd(',', ' ');
                    break;
                }
            }

            // 1. Extract ZIP (5 or 9 digits)
            // Matches 12345, 12345-6789, or 123456789 at the end
            var zipRegex = new Regex(@"(?:\b(\d{5}-\d{4})\b|\b(\d{9})\b|\b(\d{5})\b)\s*$", RegexOptions.IgnoreCase);
            var zipMatch = zipRegex.Match(input);
            if (zipMatch.Success)
            {
                result.Zip = zipMatch.Value.Trim();
                input = input.Substring(0, zipMatch.Index).Trim().TrimEnd(',', ' ');
            }

            // 2. Extract State
            if (_stateRegex != null)
            {
                var stateMatch = _stateRegex.Match(input);
                if (stateMatch.Success)
                {
                    var matchedState = stateMatch.Groups[1].Value;
                    // Map back to State Code if it was a full name
                    var stateInfo = _dataService.GetStates()
                        .FirstOrDefault(s => s.StateCode.Equals(matchedState, StringComparison.OrdinalIgnoreCase) ||
                                             s.Name.Equals(matchedState, StringComparison.OrdinalIgnoreCase));

                    result.State = stateInfo?.StateCode ?? matchedState.ToUpper();
                    input = input.Substring(0, stateMatch.Index).Trim().TrimEnd(',', ' ');
                }
            }

            // 3. Extract City
            // Strategy: US Addresses often have City before State. 
            // We'll look at the last few words of the remaining input.
            if (_cityMap != null && !string.IsNullOrWhiteSpace(input))
            {
                // Split by common delimiters including any whitespace character
                var parts = input.Split(new[] { ' ', ',', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                // Try matching last 3, 2, then 1 word(s) as a city
                bool cityFound = false;
                // Pass 1: Exact Match
                for (int i = Math.Min(3, parts.Length); i >= 1; i--)
                {
                    var potentialCity = string.Join(" ", parts.Skip(parts.Length - i));
                    if (_cityMap.TryGetValue(potentialCity, out var canonicalCity))
                    {
                        if (TryTruncateCity(input, potentialCity, out var truncatedInput))
                        {
                            result.City = canonicalCity;
                            input = truncatedInput;
                            cityFound = true;
                            break;
                        }
                    }
                }

                // Pass 2: Normalized & Fuzzy Match (if Pass 1 failed)
                if (!cityFound)
                {
                    for (int i = Math.Min(3, parts.Length); i >= 1; i--)
                    {
                        var potentialCity = string.Join(" ", parts.Skip(parts.Length - i));
                        var normalizedPotential = AddressNormalization.NormalizeForComparison(potentialCity);

                        // Try Normalized Match
                        var matchedCity = _normalizedCities?
                            .FirstOrDefault(c => c.Normalized.Equals(normalizedPotential, StringComparison.OrdinalIgnoreCase));
                        
                        if (matchedCity != null && matchedCity.Value.Original != null)
                        {
                            if (TryTruncateCity(input, potentialCity, out var truncatedInput))
                            {
                                result.City = matchedCity.Value.Original;
                                input = truncatedInput;
                                cityFound = true;
                                break;
                            }
                        }

                        // Try Fuzzy Match (only for significant lengths)
                        if (potentialCity.Length > 3)
                        {
                            var bestMatch = _normalizedCities?
                                .Select(c => new { City = c, Score = normalizedPotential.JaroWinklerSimilarity(c.Normalized) })
                                .Where(x => x.Score >= FuzzyThreshold)
                                .OrderByDescending(x => x.Score)
                                .FirstOrDefault();

                            if (bestMatch != null)
                            {
                                if (TryTruncateCity(input, potentialCity, out var truncatedInput))
                                {
                                    result.City = bestMatch.City.Original;
                                    input = truncatedInput;
                                    cityFound = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                // Fallback: If no city matched from our list, but there's a comma, 
                // the part after the last comma might be the city
                if (!cityFound && input.Contains(","))
                {
                    var lastCommaIndex = input.LastIndexOf(',');
                    var lastPart = input.Substring(lastCommaIndex + 1).Trim();
                    if (!string.IsNullOrEmpty(lastPart) && lastPart.Length > 2)
                    {
                        result.City = lastPart;
                        input = input.Substring(0, lastCommaIndex).Trim();
                        cityFound = true;
                    }
                }
            }

            // 4. Remaining is AddressLine1
            result.AddressLine1 = input;

            return result;
        }

        private bool TryTruncateCity(string input, string potentialCity, out string result)
        {
            result = input;
            var cityPattern = Regex.Escape(potentialCity).Replace(@"\ ", @"\s+");
            var cityMatch = Regex.Match(input, $@"{cityPattern}\s*$", RegexOptions.IgnoreCase);

            if (cityMatch.Success)
            {
                result = input.Substring(0, cityMatch.Index).Trim().TrimEnd(',', ' ');
                return true;
            }
            return false;
        }
    }
}

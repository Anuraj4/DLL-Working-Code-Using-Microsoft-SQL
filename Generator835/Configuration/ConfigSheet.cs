using System;
using System.Collections.Generic;
using System.Linq;

namespace Edi.Generator835.Configuration
{
    public class ConfigSheet : IConfigSheet
    {
        private readonly Dictionary<string, string> _keyValueCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, IReadOnlyDictionary<string, string>>> _searchIndexCache = new Dictionary<string, Dictionary<string, IReadOnlyDictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

        public string Name { get; }
        public IReadOnlyList<IReadOnlyDictionary<string, string>> AllRecords { get; }

        public ConfigSheet(string name, List<Dictionary<string, string>> records)
        {
            Name = name;
            // Converting to IReadOnlyList<IReadOnlyDictionary> for safety and slight memory benefit
            AllRecords = records.Select(r => (IReadOnlyDictionary<string, string>)r).ToList();
        }

        public string? GetValue(string key)
        {
            if (_keyValueCache.TryGetValue(key, out var cached)) return cached;

            // Simple O(N) lookup for the first time, assuming column 1 is key and column 2 is value
            if (AllRecords.Count == 0) return null;
            var firstRow = AllRecords[0];
            if (firstRow.Count < 2) return null;

            string keyCol = firstRow.Keys.First();
            string valCol = firstRow.Keys.Skip(1).First();

            var row = AllRecords.FirstOrDefault(r => r.TryGetValue(keyCol, out var v) && v?.Equals(key, StringComparison.OrdinalIgnoreCase) == true);
            if (row != null && row.TryGetValue(valCol, out var result))
            {
                _keyValueCache[key] = result;
                return result;
            }

            return null;
        }

        public string? GetValue(string searchColumn, string searchValue, string targetColumn)
        {
            if (string.IsNullOrEmpty(searchColumn) || string.IsNullOrEmpty(targetColumn)) return null;

            // Check index cache
            string cacheKey = $"{searchColumn}_{targetColumn}";
            if (!_searchIndexCache.TryGetValue(cacheKey, out var index))
            {
                index = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                _searchIndexCache[cacheKey] = index;
            }

            if (index.TryGetValue(searchValue, out var cachedRow))
            {
                return cachedRow.TryGetValue(targetColumn, out var val) ? val : null;
            }

            // Linear search and populate index for this searchColumn
            var row = AllRecords.FirstOrDefault(r => r.TryGetValue(searchColumn, out var v) && v?.Equals(searchValue, StringComparison.OrdinalIgnoreCase) == true);

            if (row != null)
            {
                // We index the whole row for this value in this column to make subsequent targetColumn requests for the same searchVal instant
                index[searchValue] = row;
                return row.TryGetValue(targetColumn, out var result) ? result : null;
            }

            return null;
        }

        public IEnumerable<IReadOnlyDictionary<string, string>> Filter(Func<IReadOnlyDictionary<string, string>, bool> predicate)
        {
            return AllRecords.Where(predicate);
        }
    }
}

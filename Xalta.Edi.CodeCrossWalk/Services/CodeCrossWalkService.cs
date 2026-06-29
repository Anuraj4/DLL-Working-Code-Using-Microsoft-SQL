using System;
using System.Collections.Generic;
using Xalta.Edi.CodeCrossWalk.Interfaces;

namespace Xalta.Edi.CodeCrossWalk.Services
{
    /// <summary>
    /// Thread-safe, cached implementation of ICodeCrossWalkService.
    /// Loads data once from the provider and caches it in memory for fast lookups.
    /// </summary>
    public class CodeCrossWalkService : ICodeCrossWalkService
    {
        private readonly Dictionary<string, Dictionary<string, string>> _mappings;
        private readonly object _lock = new object();
        private readonly bool _returnInputOnMiss;

        /// <summary>
        /// Constructor that initializes the service with a provider.
        /// </summary>
        /// <param name="provider">The provider to load mappings from.</param>
        /// <param name="returnInputOnMiss">If true, returns the input value when no mapping is found. If false, returns null.</param>
        public CodeCrossWalkService(ICodeCrossWalkProvider provider, bool returnInputOnMiss = false)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            _returnInputOnMiss = returnInputOnMiss;

            // Load mappings once during initialization
            _mappings = provider.LoadMappings();
        }

        public string Lookup(string tableName, string input)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentNullException(nameof(tableName));

            if (string.IsNullOrWhiteSpace(input))
                return _returnInputOnMiss ? input : null;

            lock (_lock)
            {
                if (_mappings.TryGetValue(tableName, out var table))
                {
                    if (table.TryGetValue(input, out var output))
                    {
                        return output;
                    }
                }
            }

            // Not found
            return _returnInputOnMiss ? input : null;
        }

        public bool TryLookup(string tableName, string input, out string output)
        {
            output = null;

            if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(input))
                return false;

            lock (_lock)
            {
                if (_mappings.TryGetValue(tableName, out var table))
                {
                    return table.TryGetValue(input, out output);
                }
            }

            return false;
        }

        /// <summary>
        /// Gets all available table names.
        /// </summary>
        public IEnumerable<string> GetTableNames()
        {
            lock (_lock)
            {
                return new List<string>(_mappings.Keys);
            }
        }

        /// <summary>
        /// Gets the count of mappings in a specific table.
        /// </summary>
        public int GetMappingCount(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return 0;

            lock (_lock)
            {
                if (_mappings.TryGetValue(tableName, out var table))
                {
                    return table.Count;
                }
            }

            return 0;
        }

        /// <summary>
        /// Gets the total number of tables loaded.
        /// </summary>
        public int GetTableCount()
        {
            lock (_lock)
            {
                return _mappings.Count;
            }
        }
    }
}

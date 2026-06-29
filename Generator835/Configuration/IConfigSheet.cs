using System.Collections.Generic;

namespace Edi.Generator835.Configuration
{
    /// <summary>
    /// Represents a single configuration sheet with multiple access patterns.
    /// </summary>
    public interface IConfigSheet
    {
        string Name { get; }

        /// <summary>
        /// Gets all records as a list of dictionaries (one dictionary per row).
        /// </summary>
        IReadOnlyList<IReadOnlyDictionary<string, string>> AllRecords { get; }

        /// <summary>
        /// Provides O(1) lookup for simple key-value sheets (first column as key, second as value).
        /// </summary>
        string? GetValue(string key);

        /// <summary>
        /// Performs a lookup based on a specific column.
        /// </summary>
        string? GetValue(string searchColumn, string searchValue, string targetColumn);

        /// <summary>
        /// Filters records based on a predicate.
        /// </summary>
        IEnumerable<IReadOnlyDictionary<string, string>> Filter(System.Func<IReadOnlyDictionary<string, string>, bool> predicate);
    }
}

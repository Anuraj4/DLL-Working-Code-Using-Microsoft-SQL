using System;
using System.Collections.Generic;

namespace Edi.Generator835.Configuration
{
    /// <summary>
    /// Centralized registry for all configuration sheets.
    /// Provides easy access to data by sheet name.
    /// </summary>
    public class ConfigStore
    {
        private readonly Dictionary<string, IConfigSheet> _sheets = new Dictionary<string, IConfigSheet>(StringComparer.OrdinalIgnoreCase);

        public void AddSheet(IConfigSheet sheet)
        {
            _sheets[sheet.Name] = sheet;
        }

        public IConfigSheet? GetSheet(string sheetName)
        {
            return _sheets.TryGetValue(sheetName, out var sheet) ? sheet : null;
        }

        public bool HasSheet(string sheetName)
        {
            return _sheets.ContainsKey(sheetName);
        }

        public IEnumerable<IConfigSheet> AllSheets => _sheets.Values;
    }
}

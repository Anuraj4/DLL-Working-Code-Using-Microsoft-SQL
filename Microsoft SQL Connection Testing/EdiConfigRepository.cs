using Microsoft.Data.SqlClient;
using System.Data;

namespace SqlDataRetriever;

/// <summary>
/// Singleton wrapper that maintains a single database connection
/// to the 835 Config database. Use this class from any other file
/// to fetch values by table name and key — no need to manage
/// connections or SQL queries yourself.
///
/// Usage:
///   var repo = EdiConfigRepository.Instance;
///   string value = repo.GetValue("edi_settings$", "SomeKey");
///   Dictionary&lt;string, string&gt; all = repo.GetAll("edi_settings$");
/// </summary>
public sealed class EdiConfigRepository : IDisposable
{
    private static readonly Lazy<EdiConfigRepository> _lazy = new(() => new EdiConfigRepository());
    public static EdiConfigRepository Instance => _lazy.Value;

    private readonly SqlConnection _connection;
    private bool _disposed;

    private EdiConfigRepository()
    {
        string connectionString = @"Server=Anuraj_PC\SQLEXPRESS;
                                    Database=835 Config;
                                    Trusted_Connection=True;
                                    TrustServerCertificate=True;";

        _connection = new SqlConnection(connectionString);
        _connection.Open();
    }

    // ------------------------------------------------------------------
    //  Get a single value by key from the first (key) column.
    //  Returns the value in the second column, or null if not found.
    // ------------------------------------------------------------------
    public string? GetValue(string tableName, string key)
    {
        EnsureConnection();

        List<string> columns = GetOperationColumns(tableName);
        if (columns.Count < 2)
            throw new InvalidOperationException($"Table [{tableName}] does not have at least 2 columns.");

        string keyCol = columns[0];
        string valueCol = columns[1];

        string query = $"SELECT [{valueCol}] FROM [{tableName}] WHERE [{keyCol}] = @key";

        using SqlCommand cmd = new(query, _connection);
        object convertedValue = ConvertValue(key.Trim(), GetSqlDbType(tableName, keyCol));
        cmd.Parameters.AddWithValue("@key", convertedValue);
        object? result = cmd.ExecuteScalar();

        return result?.ToString();
    }

    // ------------------------------------------------------------------
    //  Get a single value by key from a specific value column.
    //  Useful when the table has more than 2 data columns.
    // ------------------------------------------------------------------
    public string? GetValue(string tableName, string key, string valueColumnName)
    {
        EnsureConnection();

        List<string> columns = GetOperationColumns(tableName);
        string keyCol = columns[0];

        if (!columns.Contains(valueColumnName, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Column [{valueColumnName}] not found in table [{tableName}].");

        string query = $"SELECT [{valueColumnName}] FROM [{tableName}] WHERE [{keyCol}] = @key";

        using SqlCommand cmd = new(query, _connection);
        object convertedValue = ConvertValue(key.Trim(), GetSqlDbType(tableName, keyCol));
        cmd.Parameters.AddWithValue("@key", convertedValue);
        object? result = cmd.ExecuteScalar();

        return result?.ToString();
    }

    // ------------------------------------------------------------------
    //  Get all rows from a table as a dictionary (key -> value).
    //  Uses the first column as key and second column as value.
    // ------------------------------------------------------------------
    public Dictionary<string, string> GetAll(string tableName)
    {
        EnsureConnection();

        List<string> columns = GetOperationColumns(tableName);
        if (columns.Count < 2)
            throw new InvalidOperationException($"Table [{tableName}] does not have at least 2 columns.");

        string keyCol = columns[0];
        string valueCol = columns[1];

        string query = $"SELECT [{keyCol}], [{valueCol}] FROM [{tableName}]";

        using SqlCommand cmd = new(query, _connection);
        using SqlDataReader reader = cmd.ExecuteReader();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            string k = reader.IsDBNull(0) ? "" : reader.GetValue(0)?.ToString() ?? "";
            string v = reader.IsDBNull(1) ? "" : reader.GetValue(1)?.ToString() ?? "";
            result[k] = v;
        }

        return result;
    }

    // ------------------------------------------------------------------
    //  Get all rows from a table as a list of dictionaries (per row).
    //  Useful when you need all columns.
    // ------------------------------------------------------------------
    public List<Dictionary<string, object?>> GetAllRows(string tableName)
    {
        EnsureConnection();

        List<string> columns = GetOperationColumns(tableName);
        string colList = string.Join(", ", columns.Select(c => $"[{c}]"));
        string query = $"SELECT {colList} FROM [{tableName}]";

        using SqlCommand cmd = new(query, _connection);
        using SqlDataReader reader = cmd.ExecuteReader();

        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }

        return rows;
    }

    // ------------------------------------------------------------------
    //  Search by any column with a LIKE query.
    // ------------------------------------------------------------------
    public List<Dictionary<string, object?>> Search(string tableName, string columnName, string searchValue)
    {
        EnsureConnection();

        List<string> columns = GetOperationColumns(tableName);
        if (!columns.Contains(columnName, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Column [{columnName}] not found in table [{tableName}].");

        string colList = string.Join(", ", columns.Select(c => $"[{c}]"));
        string query = $"SELECT {colList} FROM [{tableName}] WHERE [{columnName}] LIKE @val";

        using SqlCommand cmd = new(query, _connection);
        cmd.Parameters.AddWithValue("@val", $"%{searchValue.Trim()}%");
        using SqlDataReader reader = cmd.ExecuteReader();

        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }

        return rows;
    }

    // ------------------------------------------------------------------
    //  List all table names in the database.
    // ------------------------------------------------------------------
    public List<string> GetTableNames()
    {
        EnsureConnection();

        var tables = new List<string>();
        string query = @"
            SELECT TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
              AND TABLE_SCHEMA = 'dbo'
            ORDER BY TABLE_NAME";

        using SqlCommand cmd = new(query, _connection);
        using SqlDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    // ------------------------------------------------------------------
    //  Get column names for a table (excludes Description columns).
    // ------------------------------------------------------------------
    public List<string> GetColumns(string tableName)
    {
        return GetOperationColumns(tableName);
    }

    // ------------------------------------------------------------------
    //  Search for a key across ALL tables in the database.
    //  Iterates every table, uses the first column as the key column
    //  and the second column as the value column.
    //  Returns a tuple (tableName, value) or null if not found.
    // ------------------------------------------------------------------
    public (string tableName, string value)? GetValueAcrossAllTables(string key)
    {
        EnsureConnection();

        List<string> tables = GetTableNames();

        foreach (string table in tables)
        {
            try
            {
                List<string> columns = GetOperationColumns(table);
                if (columns.Count < 2)
                    continue;

                string keyCol = columns[0];
                string valueCol = columns[1];

                string query = $"SELECT [{valueCol}] FROM [{table}] WHERE [{keyCol}] = @key";

                using SqlCommand cmd = new(query, _connection);
                object convertedValue = ConvertValue(key.Trim(), GetSqlDbType(table, keyCol));
                cmd.Parameters.AddWithValue("@key", convertedValue);
                object? result = cmd.ExecuteScalar();

                if (result != null && result != DBNull.Value)
                {
                    string value = result.ToString() ?? "";
                    if (!string.IsNullOrEmpty(value))
                        return (table, value);
                }
            }
            catch
            {
                // Skip tables that cause errors and continue searching
                continue;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    //  STATIC helper — pass only a key name, get the value back.
    //  Usage: string? val = EdiConfigRepository.GetValue("SomeKey");
    //  Iterates all tables automatically. Returns null if not found.
    // ------------------------------------------------------------------
    public static string? GetValue(string key)
    {
        var repo = Instance;
        var result = repo.GetValueAcrossAllTables(key);
        return result?.value;
    }

    // ------------------------------------------------------------------
    //  Check if the connection is alive.
    // ------------------------------------------------------------------
    public bool IsConnected => !_disposed && _connection.State == System.Data.ConnectionState.Open;

    // ------------------------------------------------------------------
    //  Dispose the shared connection.
    // ------------------------------------------------------------------
    public void Dispose()
    {
        if (!_disposed)
        {
            _connection?.Close();
            _connection?.Dispose();
            _disposed = true;
        }
    }

    // ======================== Private Helpers ========================

    private void EnsureConnection()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EdiConfigRepository));
        if (_connection.State != System.Data.ConnectionState.Open)
            _connection.Open();
    }

    /// <summary>
    /// Returns column names excluding any column named "Description".
    /// </summary>
    private List<string> GetOperationColumns(string tableName)
    {
        var columns = new List<string>();
        string query = @"
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @tableName
              AND COLUMN_NAME <> 'Description'
            ORDER BY ORDINAL_POSITION";

        using SqlCommand cmd = new(query, _connection);
        cmd.Parameters.AddWithValue("@tableName", tableName);
        using SqlDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    /// <summary>
    /// Maps a column's DATA_TYPE to the corresponding SqlDbType for parameter binding.
    /// </summary>
    /// <summary>
    /// Converts a string value to the appropriate type for the given SqlDbType.
    /// </summary>
    private static object ConvertValue(string value, System.Data.SqlDbType dbType)
    {
        try
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return dbType switch
            {
                System.Data.SqlDbType.Int => int.Parse(value, culture),
                System.Data.SqlDbType.BigInt => long.Parse(value, culture),
                System.Data.SqlDbType.SmallInt => short.Parse(value, culture),
                System.Data.SqlDbType.TinyInt => byte.Parse(value, culture),
                System.Data.SqlDbType.Bit => bool.Parse(value),
                System.Data.SqlDbType.Float => double.Parse(value, culture),
                System.Data.SqlDbType.Real => float.Parse(value, culture),
                System.Data.SqlDbType.Decimal => decimal.Parse(value, culture),
                System.Data.SqlDbType.Money => decimal.Parse(value, culture),
                System.Data.SqlDbType.SmallMoney => decimal.Parse(value, culture),
                System.Data.SqlDbType.UniqueIdentifier => Guid.Parse(value),
                _ => value
            };
        }
        catch
        {
            return value;
        }
    }

    private System.Data.SqlDbType GetSqlDbType(string tableName, string columnName)
    {
        string query = @"
            SELECT DATA_TYPE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @tableName AND COLUMN_NAME = @colName";

        using SqlCommand cmd = new(query, _connection);
        cmd.Parameters.AddWithValue("@tableName", tableName);
        cmd.Parameters.AddWithValue("@colName", columnName);
        object? result = cmd.ExecuteScalar();

        string dataType = result?.ToString()?.ToLowerInvariant() ?? "nvarchar";

        return dataType switch
        {
            "int" => System.Data.SqlDbType.Int,
            "bigint" => System.Data.SqlDbType.BigInt,
            "smallint" => System.Data.SqlDbType.SmallInt,
            "tinyint" => System.Data.SqlDbType.TinyInt,
            "bit" => System.Data.SqlDbType.Bit,
            "float" => System.Data.SqlDbType.Float,
            "real" => System.Data.SqlDbType.Real,
            "decimal" => System.Data.SqlDbType.Decimal,
            "numeric" => System.Data.SqlDbType.Decimal,
            "money" => System.Data.SqlDbType.Money,
            "smallmoney" => System.Data.SqlDbType.SmallMoney,
            "date" => System.Data.SqlDbType.Date,
            "datetime" => System.Data.SqlDbType.DateTime,
            "datetime2" => System.Data.SqlDbType.DateTime2,
            "smalldatetime" => System.Data.SqlDbType.SmallDateTime,
            "time" => System.Data.SqlDbType.Time,
            "uniqueidentifier" => System.Data.SqlDbType.UniqueIdentifier,
            "varbinary" => System.Data.SqlDbType.VarBinary,
            "binary" => System.Data.SqlDbType.Binary,
            "image" => System.Data.SqlDbType.Image,
            _ => System.Data.SqlDbType.NVarChar
        };
    }
}

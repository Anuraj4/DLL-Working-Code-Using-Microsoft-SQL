using Microsoft.Data.SqlClient;

// ============================================================
//  Usage: Provide server name and database name to fetch data
//  from SQL and pass it to DetermineValue as a dictionary.
// ============================================================

// === Configuration: Set your SQL Server and Database name here ===
string serverName = @"Anuraj_PC\SQLEXPRESS";
string databaseName = "835 Config";

// === Build connection string ===
string connectionString = $@"Server={serverName};
                            Database={databaseName};
                            Trusted_Connection=True;
                            TrustServerCertificate=True;";

// === Fetch all data from SQL as a dictionary (key -> value) ===
Dictionary<string, string> sqlData = FetchAllDataFromSql(connectionString);

// === Display fetched data ===
Console.WriteLine($"Fetched {sqlData.Count} records from SQL Server: {serverName}, Database: {databaseName}");
Console.WriteLine("--------------------------------------------------");

// === Pass the SQL data dictionary to DetermineValue for a specific field ===
string targetField = "ISA_AuthorizationInformationQualifier";
string result = DetermineValue(targetField, sqlData);
Console.WriteLine($"DetermineValue result for '{targetField}': '{result}'");

// ============================================================
//  DetermineValue: Looks up the target field in the provided
//  dictionary and returns the value, or empty if not found.
//  (Mirrors the pattern used in Edi835Generator.DetermineValue
//  where rowData dictionary is used to resolve field values.)
// ============================================================
static string DetermineValue(string targetField, Dictionary<string, string> data)
{
    if (data.TryGetValue(targetField, out string? value))
    {
        return value ?? string.Empty;
    }
    return string.Empty;
}

// ============================================================
//  Helper: Fetch all data from all tables in the database
//  as a flat dictionary (key -> value).
// ============================================================
static Dictionary<string, string> FetchAllDataFromSql(string connString)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    using SqlConnection connection = new SqlConnection(connString);
    connection.Open();

    // Get all table names
    List<string> tableNames = new List<string>();
    string tableQuery = @"
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_TYPE = 'BASE TABLE'
          AND TABLE_SCHEMA = 'dbo'
        ORDER BY TABLE_NAME";

    using (SqlCommand cmd = new SqlCommand(tableQuery, connection))
    using (SqlDataReader reader = cmd.ExecuteReader())
    {
        while (reader.Read())
        {
            tableNames.Add(reader.GetString(0));
        }
    }

    // For each table, get data using first column as key and second column as value
    foreach (string tableName in tableNames)
    {
        try
        {
            // Get column names (excluding Description columns)
            List<string> columns = new List<string>();
            string colQuery = @"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @tableName
                  AND COLUMN_NAME <> 'Description'
                ORDER BY ORDINAL_POSITION";

            using (SqlCommand cmd = new SqlCommand(colQuery, connection))
            {
                cmd.Parameters.AddWithValue("@tableName", tableName);
                using SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    columns.Add(reader.GetString(0));
                }
            }

            if (columns.Count < 2)
                continue;

            string keyCol = columns[0];
            string valueCol = columns[1];

            // Fetch all rows from the table
            string dataQuery = $"SELECT [{keyCol}], [{valueCol}] FROM [{tableName}]";
            using SqlCommand dataCmd = new SqlCommand(dataQuery, connection);
            using SqlDataReader dataReader = dataCmd.ExecuteReader();

            while (dataReader.Read())
            {
                string key = dataReader.IsDBNull(0) ? "" : dataReader.GetValue(0)?.ToString() ?? "";
                string val = dataReader.IsDBNull(1) ? "" : dataReader.GetValue(1)?.ToString() ?? "";

                if (!string.IsNullOrEmpty(key))
                {
                    result[key] = val;
                }
            }
        }
        catch
        {
            // Skip tables that cause errors and continue
            continue;
        }
    }

    return result;
}

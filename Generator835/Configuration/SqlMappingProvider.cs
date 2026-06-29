using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Edi.Generator835.Configuration
{
    /// <summary>
    /// Loads configuration from a Microsoft SQL Server database.
    /// Each table in the database corresponds to a configuration sheet.
    /// Uses the first column as the key and remaining columns as values.
    /// </summary>
    public class SqlMappingProvider : IMappingProvider
    {
        private readonly string _serverName;
        private readonly string _databaseName;

        public SqlMappingProvider(string serverName, string databaseName)
        {
            _serverName = serverName;
            _databaseName = databaseName;
        }

        public MappingConfiguration LoadMappings(string configPath)
        {
            // configPath is ignored for SQL provider - we use server name and database name instead
            var connectionString = $@"Server={_serverName};
                                        Database={_databaseName};
                                        Trusted_Connection=True;
                                        TrustServerCertificate=True;";

            return LoadMappingsFromSql(connectionString);
        }

        /// <summary>
        /// Load mappings directly from SQL using server and database names.
        /// </summary>
        public MappingConfiguration LoadMappingsFromSql(string connectionString)
        {
            var config = new MappingConfiguration();

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            // Get all table names from the database
            List<string> tableNames = GetTableNames(connection);

            foreach (string tableName in tableNames)
            {
                try
                {
                    // Get column names for the table (excluding Description columns)
                    List<string> columns = GetOperationColumns(connection, tableName);

                    if (columns.Count < 2)
                    {
                        Log.Debug("Skipping table {TableName}: needs at least 2 columns, found {Count}", tableName, columns.Count);
                        continue;
                    }

                    // Read all records from the table
                    var records = ReadTableRecords(connection, tableName, columns);

                    if (records.Count > 0)
                    {
                        // Strip trailing '$' from table names (Excel-linked SQL tables often have '$' suffix)
                        string normalizedTableName = tableName.TrimEnd('$');
                        // Use MappingLoader to route records into the correct buckets
                        MappingLoader.LoadTable(config, normalizedTableName, records);
                        Log.Information("Loaded config from SQL table: {TableName} (as {NormalizedName}) with {Count} records", tableName, normalizedTableName, records.Count);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error loading table {TableName} from SQL, skipping.", tableName);
                    continue;
                }
            }

            return config;
        }

        /// <summary>
        /// Get all user table names from the database.
        /// </summary>
        private List<string> GetTableNames(SqlConnection connection)
        {
            var tables = new List<string>();
            const string query = @"
                SELECT TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                  AND TABLE_SCHEMA = 'dbo'
                ORDER BY TABLE_NAME";

            using var cmd = new SqlCommand(query, connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }

            return tables;
        }

        /// <summary>
        /// Get column names for a table, excluding any column named "Description".
        /// </summary>
        private List<string> GetOperationColumns(SqlConnection connection, string tableName)
        {
            var columns = new List<string>();
            const string query = @"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @tableName
                  AND COLUMN_NAME <> 'Description'
                ORDER BY ORDINAL_POSITION";

            using var cmd = new SqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@tableName", tableName);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
            }

            return columns;
        }

        /// <summary>
        /// Read all records from a table as a list of dictionaries.
        /// First column is used as the key column, remaining columns are the values.
        /// </summary>
        private List<Dictionary<string, string>> ReadTableRecords(SqlConnection connection, string tableName, List<string> columns)
        {
            var records = new List<Dictionary<string, string>>();
            string colList = string.Join(", ", columns.Select(c => $"[{c}]"));
            string query = $"SELECT {colList} FROM [{tableName}]";

            using var cmd = new SqlCommand(query, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool hasData = false;

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    string columnName = reader.GetName(i);
                    var value = reader.IsDBNull(i) ? string.Empty : reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;
                    record[columnName] = value;

                    if (!string.IsNullOrWhiteSpace(value))
                        hasData = true;
                }

                // Skip empty rows
                if (!hasData) continue;

                records.Add(record);
            }

            return records;
        }
    }
}

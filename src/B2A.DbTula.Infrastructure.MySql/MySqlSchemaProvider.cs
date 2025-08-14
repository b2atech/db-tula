using B2a.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using System.Data;

namespace B2A.DbTula.Infrastructure.MySql
{
    public class MySqlSchemaProvider : IDatabaseSchemaProvider
    {
        private readonly SchemaFetcher _fetcher;
        private readonly DatabaseConnection _connection;

        public MySqlSchemaProvider(
            string connectionString,
            Action<int, int, string, bool> logger,
            bool verbose,
            LogLevel logLevel = LogLevel.Basic)
        {
            _connection = new DatabaseConnection(connectionString, logger, verbose, logLevel);
            _fetcher = new SchemaFetcher(_connection, logger, verbose, logLevel);
        }

        public async Task<IList<string>> GetTablesAsync()
        {
            var tableRows = await _fetcher.GetTablesAsync();
            var tableNames = new List<string>();

            if (tableRows.Columns.Count == 0)
                return tableNames;

            var tableNameColumn = tableRows.Columns[0].ColumnName;

            foreach (System.Data.DataRow row in tableRows.Rows)
            {
                var tableName = row[tableNameColumn]?.ToString();
                if (!string.IsNullOrEmpty(tableName))
                {
                    tableNames.Add(tableName);
                }
            }

            return tableNames;
        }

        public async Task<TableDefinition> GetTableDefinitionAsync(string tableName)
        {
            return await _fetcher.GetTableDefinitionAsync(tableName);
        }

        public async Task<IList<ColumnDefinition>> GetColumnsAsync(string tableName)
        {
            return await _fetcher.GetColumnsListAsync(tableName);
        }

        public async Task<IList<PrimaryKeyDefinition>> GetPrimaryKeysAsync(string tableName)
        {
            return await _fetcher.GetPrimaryKeysListAsync(tableName);
        }

        public async Task<string?> GetPrimaryKeyCreateScriptAsync(string tableName)
        {
            return await _fetcher.GetPrimaryKeyCreateScriptAsync(tableName);
        }

        public async Task<string?> GetForeignKeyCreateScriptAsync(string tableName, string foreignKeyName)
        {
            return await _fetcher.GetForeignKeyCreateScriptAsync(tableName, foreignKeyName);
        }

        public async Task<IList<ForeignKeyDefinition>> GetForeignKeysAsync(string tableName)
        {
            return await _fetcher.GetForeignKeysListAsync(tableName);
        }

        public async Task<IList<IndexDefinition>> GetIndexesAsync(string tableName)
        {
            return await _fetcher.GetIndexesListAsync(tableName);
        }

        public async Task<string?> GetIndexCreateScriptAsync(string indexName)
        {
            return await _fetcher.GetIndexDefinitionAsync(indexName);
        }

        public async Task<IList<DbFunctionDefinition>> GetFunctionsAsync()
        {
            var table = await _fetcher.GetFunctionsAsync();
            return ParseFunctionOrProcedureList(table);
        }

        public async Task<IList<DbFunctionDefinition>> GetProceduresAsync()
        {
            var table = await _fetcher.GetProceduresAsync();
            return ParseFunctionOrProcedureList(table);
        }

        public async Task<string> GetFunctionDefinitionAsync(string functionName)
        {
            return await _fetcher.GetFunctionDefinitionAsync(functionName) ?? string.Empty;
        }

        public async Task<string> GetProcedureDefinitionAsync(string procedureName)
        {
            return await _fetcher.GetProcedureDefinitionAsync(procedureName) ?? string.Empty;
        }

        public async Task<string> GetCreateTableScriptAsync(string tableName)
        {
            return await _fetcher.GetCreateTableScriptAsync(tableName) ?? string.Empty;
        }

        private List<DbFunctionDefinition> ParseFunctionOrProcedureList(DataTable table)
        {
            var list = new List<DbFunctionDefinition>();

            foreach (DataRow row in table.Rows)
            {
                var name = row["ROUTINE_NAME"].ToString();

                list.Add(new DbFunctionDefinition
                {
                    Name = name,
                    Arguments = row["DTD_IDENTIFIER"]?.ToString(), // Not very useful in MySQL; may be null
                    Definition = null // Set later using SHOW CREATE ...
                });
            }

            return list;
        }


        #region Views
        public async Task<IList<DbViewDefinition>> GetViewsAsync()
        {
            string query = "SELECT table_name AS VIEW_NAME FROM information_schema.views WHERE table_schema = DATABASE();";
            var table = await _connection.ExecuteQueryAsync(query);

            return table.AsEnumerable()
                .Select(row => new DbViewDefinition
                {
                    Name = row["VIEW_NAME"].ToString(),
                    Definition = null // Will fetch with SHOW CREATE VIEW
                })
                .ToList();
        }

        public async Task<string> GetViewDefinitionAsync(string viewName)
        {
            string query = $"SHOW CREATE VIEW `{viewName}`;";
            var result = await _connection.ExecuteQueryAsync(query);
            return result.Rows.Count > 0 ? result.Rows[0]["Create View"]?.ToString() ?? string.Empty : string.Empty;
        }
        #endregion

        #region Triggers
        public async Task<IList<DbTriggerDefinition>> GetTriggersAsync()
        {
            string query = "SELECT TRIGGER_NAME, EVENT_MANIPULATION, EVENT_OBJECT_TABLE, ACTION_TIMING FROM information_schema.triggers WHERE trigger_schema = DATABASE();";
            var table = await _connection.ExecuteQueryAsync(query);

            return table.AsEnumerable()
                .Select(row => new DbTriggerDefinition
                {
                    Name = row["TRIGGER_NAME"].ToString(),
                    Event = row["EVENT_MANIPULATION"].ToString(),
                    Table = row["EVENT_OBJECT_TABLE"].ToString(),
                    Timing = row["ACTION_TIMING"].ToString(),
                    Definition = null // Will fetch with SHOW CREATE TRIGGER
                })
                .ToList();
        }

        public async Task<string> GetTriggerDefinitionAsync(string triggerName)
        {
            string query = $"SHOW CREATE TRIGGER `{triggerName}`;";
            var result = await _connection.ExecuteQueryAsync(query);
            return result.Rows.Count > 0 ? result.Rows[0]["SQL Original Statement"]?.ToString() ?? string.Empty : string.Empty;
        }
        #endregion
    }
}

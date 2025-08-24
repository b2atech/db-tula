using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using System.Data;

namespace B2A.DbTula.Infrastructure.MySql;

public class SchemaFetcher
{
    private readonly DatabaseConnection _connection;
    private readonly Action<int, int, string, bool> _logger;
    private readonly LogLevel _logLevel;

    public SchemaFetcher(DatabaseConnection connection, Action<int, int, string, bool> logger, object verbose, LogLevel logLevel = LogLevel.Basic)
    {
        _connection = connection;
        _logger = logger;
        _logLevel = logLevel;
    }

    private void Log(string message, LogLevel level = LogLevel.Basic)
    {
        if (_logLevel >= level)
        {
            _logger?.Invoke(0, 0, message, false);
        }
    }

    #region Schema Object Queries
    public async Task<DataTable> GetTablesAsync()
    {
        string query = "SHOW FULL TABLES WHERE Table_type = 'BASE TABLE'";
        return await ExecuteQueryAsync(query);
    }

    public async Task<DataTable> GetFunctionsAsync()
    {
        string query = "SELECT * FROM information_schema.routines WHERE routine_type = 'FUNCTION' AND routine_schema = DATABASE()";
        return await ExecuteQueryAsync(query);
    }

    public async Task<DataTable> GetProceduresAsync()
    {
        string query = "SELECT * FROM information_schema.routines WHERE routine_type = 'PROCEDURE' AND routine_schema = DATABASE()";
        return await ExecuteQueryAsync(query);
    }

    public async Task<DataTable> GetSequencesAsync()
    {
        string query = "SELECT table_name FROM information_schema.tables WHERE table_name LIKE '%seq%' AND table_schema = DATABASE()";
        return await ExecuteQueryAsync(query);
    }


    public async Task<DataTable> GetViewsAsync()
    {
        string query = "SHOW FULL TABLES WHERE Table_type = 'VIEW'";
        return await ExecuteQueryAsync(query);
    }

    public async Task<DataTable> GetTriggersAsync()
    {
        string query = @"
        SELECT 
            TRIGGER_NAME, 
            EVENT_MANIPULATION, 
            EVENT_OBJECT_TABLE, 
            ACTION_STATEMENT, 
            ACTION_TIMING 
        FROM INFORMATION_SCHEMA.TRIGGERS
        WHERE TRIGGER_SCHEMA = DATABASE();";
        return await ExecuteQueryAsync(query);
    }

    #endregion

    #region Table Definition

    public async Task<TableDefinition> GetTableDefinitionAsync(string tableName)
    {
        var columnsTask = GetColumnsListAsync(tableName);
        var pkTask = GetPrimaryKeysListAsync(tableName);
        var fkTask = GetForeignKeysListAsync(tableName);
        var indexTask = GetIndexesListAsync(tableName);
        var scriptTask = GetCreateTableScriptAsync(tableName);

        await Task.WhenAll(columnsTask, pkTask, fkTask, indexTask, scriptTask);

        return new TableDefinition
        {
            Name = tableName,
            Columns = columnsTask.Result,
            PrimaryKeys = pkTask.Result,
            ForeignKeys = fkTask.Result,
            Indexes = indexTask.Result,
            CreateScript = scriptTask.Result ?? string.Empty
        };
    }

    public async Task<List<ColumnDefinition>> GetColumnsListAsync(string tableName)
    {
        string query = $"SHOW COLUMNS FROM `{tableName}`;";
        var table = await ExecuteQueryAsync(query);

        return table.AsEnumerable()
            .Select(row => new ColumnDefinition
            {
                Name = row["Field"]?.ToString() ?? string.Empty,
                DataType = row["Type"]?.ToString() ?? string.Empty,
                IsNullable = row["Null"]?.ToString() == "YES",
                DefaultValue = row["Default"]?.ToString()
            })
            .ToList();
    }

    public async Task<List<PrimaryKeyDefinition>> GetPrimaryKeysListAsync(string tableName)
    {
        string query = $@"
            SELECT CONSTRAINT_NAME, COLUMN_NAME
            FROM information_schema.key_column_usage
            WHERE table_name = '{tableName}'
              AND constraint_schema = DATABASE()
              AND constraint_name = 'PRIMARY';";
        var table = await ExecuteQueryAsync(query);
        return table.Rows.Count > 0
            ? [new PrimaryKeyDefinition
                {
                    Name = "PRIMARY",
                    Columns = table.AsEnumerable().Select(r => r["COLUMN_NAME"]?.ToString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToList()
                }]
            : [];
    }

    public async Task<List<ForeignKeyDefinition>> GetForeignKeysListAsync(string tableName)
    {
        string query = $@"
            SELECT constraint_name, column_name, referenced_table_name, referenced_column_name
            FROM information_schema.key_column_usage
            WHERE table_name = '{tableName}'
              AND referenced_table_name IS NOT NULL
              AND constraint_schema = DATABASE();";
        var table = await ExecuteQueryAsync(query);

        return table.AsEnumerable()
            .Select(row => new ForeignKeyDefinition
            {
                Name = row["constraint_name"]?.ToString() ?? string.Empty,
                ColumnName = row["column_name"]?.ToString() ?? string.Empty,
                ReferencedTable = row["referenced_table_name"]?.ToString() ?? string.Empty,
                ReferencedColumn = row["referenced_column_name"]?.ToString() ?? string.Empty
            }).ToList();
    }

    public async Task<List<IndexDefinition>> GetIndexesListAsync(string tableName)
    {
        string query = $"SHOW INDEX FROM `{tableName}`;";
        var table = await ExecuteQueryAsync(query);

        return table.AsEnumerable()
            .GroupBy(r => r["Key_name"]?.ToString() ?? string.Empty)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => new IndexDefinition
            {
                Name = g.Key,
                Columns = g.Select(r => r["Column_name"]?.ToString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToList(),
                IsUnique = g.All(r => r["Non_unique"]?.ToString() == "0"),
                IndexType = g.First()["Index_type"]?.ToString()
            })
            .ToList();
    }

    public async Task<string?> GetCreateTableScriptAsync(string tableName)
    {
        string query = $"SHOW CREATE TABLE `{tableName}`;";
        var result = await ExecuteQueryAsync(query);
        return result.Rows.Count > 0 ? result.Rows[0]["Create Table"]?.ToString() : null;
    }

    public async Task<string?> GetFunctionDefinitionAsync(string functionName)
    {
        string query = $"SHOW CREATE FUNCTION `{functionName}`;";
        var result = await ExecuteQueryAsync(query);
        return result.Rows.Count > 0 ? result.Rows[0]["Create Function"]?.ToString() : null;
    }

    public async Task<string?> GetProcedureDefinitionAsync(string procedureName)
    {
        string query = $"SHOW CREATE PROCEDURE `{procedureName}`;";
        var result = await ExecuteQueryAsync(query);
        return result.Rows.Count > 0 ? result.Rows[0]["Create Procedure"]?.ToString() : null;
    }

    public async Task<string?> GetSequenceDefinitionAsync(string sequenceName)
    {
        // MySQL uses AUTO_INCREMENT rather than standalone sequences
        return $"-- MySQL does not use sequences like PostgreSQL. Check if `{sequenceName}` is a table with AUTO_INCREMENT.";
    }

    public async Task<string?> GetIndexDefinitionAsync(string indexName)
    {
        return $"-- MySQL does not support 'SHOW CREATE INDEX'; you may infer index from SHOW CREATE TABLE.";
    }


    public async Task<string?> GetPrimaryKeyCreateScriptAsync(string tableName)
    {
        var ddl = await GetCreateTableScriptAsync(tableName);
        if (string.IsNullOrWhiteSpace(ddl))
            return null;

        // Extract PRIMARY KEY line
        var lines = ddl.Split('\n');
        var pkLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
                                               l.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase));

        return pkLine != null
            ? $"ALTER TABLE `{tableName}` ADD {pkLine.Trim().TrimEnd(',')};"
            : null;
    }


    public async Task<string?> GetForeignKeyCreateScriptAsync(string tableName, string foreignKeyName)
    {
        var ddl = await GetCreateTableScriptAsync(tableName);
        if (string.IsNullOrWhiteSpace(ddl))
            return null;

        // Extract FK line matching constraint name
        var lines = ddl.Split('\n');
        var fkLine = lines.FirstOrDefault(l =>
            l.Contains($"CONSTRAINT `{foreignKeyName}`", StringComparison.OrdinalIgnoreCase));

        return fkLine != null
            ? $"ALTER TABLE `{tableName}` ADD {fkLine.Trim().TrimEnd(',')};"
            : null;
    }

  
    public async Task<string?> GetTriggerDefinitionAsync(string triggerName)
    {
        string query = $"SHOW CREATE TRIGGER `{triggerName}`;";
        var result = await ExecuteQueryAsync(query);
        if (result.Rows.Count == 0) return null;
        // find column: prefer "SQL Original Statement" or anything with "Create" or "SQL"
        var colName = result.Columns.Cast<DataColumn>()
            .Select(c => c.ColumnName)
            .FirstOrDefault(n => n.Contains("SQL", StringComparison.OrdinalIgnoreCase) || n.Contains("Create", StringComparison.OrdinalIgnoreCase));
        return colName != null ? result.Rows[0][colName].ToString() : null;
    }

    #endregion

    #region Utility

    private async Task<DataTable> ExecuteQueryAsync(string query)
    {
        var result = await _connection.ExecuteQueryAsync(query);
        Log($"Query: {query}", LogLevel.Verbose);
        Log($"Rows returned: {result.Rows.Count}", LogLevel.Verbose);
        return result;
    }

    #endregion
}


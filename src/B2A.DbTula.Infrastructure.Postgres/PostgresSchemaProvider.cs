using B2a.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using B2A.DbTula.Infrastructure.Postgres;
using System.Data;

namespace B2a.DbTula.Infrastructure.Postgres;


public class PostgresSchemaProvider : IDatabaseSchemaProvider
{
    private readonly SchemaFetcher _fetcher;
    private readonly DatabaseConnection _connection;
    public PostgresSchemaProvider(
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

        foreach (System.Data.DataRow row in tableRows.Rows)
        {
            var tableName = row["table_name"].ToString();
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
        return await _fetcher.GetIndexCreateScriptAsync(indexName);
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

    private List<DbFunctionDefinition> ParseFunctionOrProcedureList(System.Data.DataTable table)
    {
        var list = new List<DbFunctionDefinition>();

        foreach (System.Data.DataRow row in table.Rows)
        {
            list.Add(new DbFunctionDefinition
            {
                Name = row["routine_name"].ToString(),
                Arguments = row["arguments"]?.ToString(),
                Definition = row["definition"]?.ToString(),
            });
        }

        return list;
    }

    public async Task<string> GetViewDefinitionAsync(string viewName)
    {
        return await _fetcher.GetViewDefinitionAsync(viewName) ?? string.Empty;
    }

    public async Task<string> GetTriggerDefinitionAsync(string triggerName)
    {
        return await _fetcher.GetTriggerDefinitionAsync(triggerName) ?? string.Empty;
    }

    public async Task<IList<DbViewDefinition>> GetViewsAsync()
    {
        const string sql = @"
        SELECT 
            table_name AS view_name, 
            view_definition 
        FROM information_schema.views 
        WHERE table_schema = 'public';
    ";

        var dataTable = await _connection.ExecuteQueryAsync(sql);
        var list = new List<DbViewDefinition>();

        foreach (DataRow row in dataTable.Rows)
        {
            list.Add(new DbViewDefinition
            {
                Name = row["view_name"].ToString(),
                Definition = row["view_definition"]?.ToString()
            });
        }

        return list;
    }

    public async Task<IList<DbTriggerDefinition>> GetTriggersAsync()
    {
        const string sql = @"
        SELECT 
            trg.tgname AS trigger_name,
            tbl.relname AS table_name,
            pg_get_triggerdef(trg.oid, true) AS definition
        FROM pg_trigger trg
        JOIN pg_class tbl ON tbl.oid = trg.tgrelid
        JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
        WHERE ns.nspname = 'public'
          AND NOT trg.tgisinternal;
    ";

        var dataTable = await _connection.ExecuteQueryAsync(sql);
        var list = new List<DbTriggerDefinition>();

        foreach (DataRow row in dataTable.Rows)
        {
            list.Add(new DbTriggerDefinition
            {
                Name = row["trigger_name"].ToString(),
                Table = row["table_name"].ToString(),
                Definition = row["definition"]?.ToString()
            });
        }

        return list;
    }

}



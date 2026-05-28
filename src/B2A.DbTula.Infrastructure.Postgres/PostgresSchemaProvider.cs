using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using B2A.DbTula.Infrastructure.Postgres;
using B2a.DbTula.Core.Abstractions;
using Npgsql;
using System.Data;

namespace B2a.DbTula.Infrastructure.Postgres;
 

public class PostgresSchemaProvider : IDatabaseSchemaProvider, IDatabaseIdentityProvider
{
    private readonly string _connectionString;
    private readonly SchemaFetcher _fetcher;
    private readonly DatabaseConnection _connection;

    public PostgresSchemaProvider(
        string connectionString,
        Action<int, int, string, bool> logger,
        bool verbose,
        LogLevel logLevel = LogLevel.Basic)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("PostgreSQL connection string cannot be empty.", nameof(connectionString));

        _connectionString = connectionString;
        _connection = new DatabaseConnection(connectionString, logger, verbose, logLevel);
        _fetcher = new SchemaFetcher(_connection, logger, verbose, logLevel);
    }

    public async Task<IList<string>> GetTablesAsync()
    {
        var tableRows = await _fetcher.GetTablesAsync();
        var tableNames = new List<string>();

        foreach (DataRow row in tableRows.Rows)
        {
            var schemaName = row["table_schema"]?.ToString();
            var tableName = row["table_name"]?.ToString();

            if (!string.IsNullOrWhiteSpace(schemaName) &&
                !string.IsNullOrWhiteSpace(tableName))
            {
                tableNames.Add($"{schemaName}.{tableName}");
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
        return await _fetcher.GetFunctionDefinitionAsync(functionName);
    }

    public async Task<string> GetProcedureDefinitionAsync(string procedureName)
    {
        return await _fetcher.GetProcedureDefinitionAsync(procedureName);
    }

    public async Task<string> GetCreateTableScriptAsync(string tableName)
    {
        return await _fetcher.GetCreateTableScriptAsync(tableName);
    }

    public async Task<IList<DbViewDefinition>> GetViewsAsync()
    {
        var dataTable = await _fetcher.GetViewsAsync();
        var list = new List<DbViewDefinition>();

        foreach (DataRow row in dataTable.Rows)
        {
            var schemaName = row["table_schema"]?.ToString();
            var viewName = row["view_name"]?.ToString();

            if (string.IsNullOrWhiteSpace(schemaName) ||
                string.IsNullOrWhiteSpace(viewName))
            {
                continue;
            }

            list.Add(new DbViewDefinition
            {
                Name = $"{schemaName}.{viewName}",
                Definition = row["view_definition"]?.ToString()
            });
        }

        return list;
    }

    public async Task<string?> GetViewDefinitionAsync(string viewName)
    {
        return await _fetcher.GetViewDefinitionAsync(viewName);
    }

    public async Task<IList<DbTriggerDefinition>> GetTriggersAsync()
    {
        var dataTable = await _fetcher.GetTriggersAsync();
        var list = new List<DbTriggerDefinition>();

        foreach (DataRow row in dataTable.Rows)
        {
            var schemaName = row["table_schema"]?.ToString();
            var tableName = row["table_name"]?.ToString();
            var triggerName = row["trigger_name"]?.ToString();

            if (string.IsNullOrWhiteSpace(schemaName) ||
                string.IsNullOrWhiteSpace(tableName) ||
                string.IsNullOrWhiteSpace(triggerName))
            {
                continue;
            }

            list.Add(new DbTriggerDefinition
            {
                Name = triggerName,
                Table = $"{schemaName}.{tableName}",
                Definition = row["definition"]?.ToString()
            });
        }

        return list;
    }

    public async Task<string?> GetTriggerDefinitionAsync(string triggerName)
    {
        return await _fetcher.GetTriggerDefinitionAsync(triggerName);
    }

    public async Task<DatabaseIdentity> GetDatabaseIdentityAsync(CancellationToken cancellationToken = default)
    {
        var builder = new NpgsqlConnectionStringBuilder(_connectionString);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                inet_server_addr()::text AS server_address,
                inet_server_port() AS server_port,
                current_database() AS database_name,
                current_user AS user_name,
                version() AS version;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Unable to read PostgreSQL database identity.");
        }

        return new DatabaseIdentity
        {
            ProviderName = "PostgreSQL",
            ConfiguredHost = builder.Host,
            ConfiguredPort = builder.Port,
            ConfiguredDatabase = builder.Database,
            ConfiguredUsername = builder.Username,
            ServerAddress = reader["server_address"] == DBNull.Value ? null : reader["server_address"]?.ToString(),
            ServerPort = reader["server_port"] == DBNull.Value ? null : Convert.ToInt32(reader["server_port"]),
            CurrentDatabase = reader["database_name"]?.ToString() ?? string.Empty,
            CurrentUser = reader["user_name"]?.ToString() ?? string.Empty,
            Version = reader["version"]?.ToString() ?? string.Empty
        };
    }

    private static List<DbFunctionDefinition> ParseFunctionOrProcedureList(DataTable table)
    {
        var list = new List<DbFunctionDefinition>();

        foreach (DataRow row in table.Rows)
        {
            var schemaName = row["routine_schema"]?.ToString();
            var routineName = row["routine_name"]?.ToString();

            if (string.IsNullOrWhiteSpace(schemaName) ||
                string.IsNullOrWhiteSpace(routineName))
            {
                continue;
            }

            list.Add(new DbFunctionDefinition
            {
                Name = $"{schemaName}.{routineName}",
                Arguments = row["arguments"]?.ToString(),
                Definition = row["definition"]?.ToString()
            });
        }

        return list;
    }
}
//old 
//public class PostgresSchemaProvider : IDatabaseSchemaProvider, IDatabaseIdentityProvider
//{
//    private readonly string _connectionString;
//    private readonly SchemaFetcher _fetcher;
//    private readonly DatabaseConnection _connection;
//    public PostgresSchemaProvider(
//        string connectionString,
//        Action<int, int, string, bool> logger,
//        bool verbose,
//        LogLevel logLevel = LogLevel.Basic)
//    {
//        if (string.IsNullOrWhiteSpace(connectionString))
//            throw new ArgumentException("PostgreSQL connection string cannot be empty.", nameof(connectionString));

//        _connectionString = connectionString;
//        _connection = new DatabaseConnection(connectionString, logger, verbose, logLevel);
//        _fetcher = new SchemaFetcher(_connection, logger, verbose, logLevel);
//    }


//    public async Task<IList<string>> GetTablesAsync()
//    {
//        var tableRows = await _fetcher.GetTablesAsync();
//        var tableNames = new List<string>();

//        foreach (System.Data.DataRow row in tableRows.Rows)
//        {
//            var tableName = row["table_name"].ToString();
//            if (!string.IsNullOrEmpty(tableName))
//            {
//                tableNames.Add(tableName);
//            }
//        }

//        return tableNames;
//    }

//    public async Task<TableDefinition> GetTableDefinitionAsync(string tableName)
//    {
//        return await _fetcher.GetTableDefinitionAsync(tableName);
//    }

//    public async Task<IList<ColumnDefinition>> GetColumnsAsync(string tableName)
//    {
//        return await _fetcher.GetColumnsListAsync(tableName);
//    }

//    public async Task<IList<PrimaryKeyDefinition>> GetPrimaryKeysAsync(string tableName)
//    {
//        return await _fetcher.GetPrimaryKeysListAsync(tableName);
//    }
//    public async Task<string?> GetPrimaryKeyCreateScriptAsync(string tableName)
//    {
//        return await _fetcher.GetPrimaryKeyCreateScriptAsync(tableName);
//    }
//    public async Task<string?> GetForeignKeyCreateScriptAsync(string tableName, string foreignKeyName)
//    {
//        return await _fetcher.GetForeignKeyCreateScriptAsync(tableName, foreignKeyName);
//    }

//    public async Task<IList<ForeignKeyDefinition>> GetForeignKeysAsync(string tableName)
//    {
//        return await _fetcher.GetForeignKeysListAsync(tableName);
//    }

//    public async Task<IList<IndexDefinition>> GetIndexesAsync(string tableName)
//    {
//        return await _fetcher.GetIndexesListAsync(tableName);
//    }
//    public async Task<string?> GetIndexCreateScriptAsync(string indexName)
//    {
//        return await _fetcher.GetIndexCreateScriptAsync(indexName);
//    }
//    public async Task<IList<DbFunctionDefinition>> GetFunctionsAsync()
//    {
//        var table = await _fetcher.GetFunctionsAsync();
//        return ParseFunctionOrProcedureList(table);
//    }

//    public async Task<IList<DbFunctionDefinition>> GetProceduresAsync()
//    {
//        var table = await _fetcher.GetProceduresAsync();
//        return ParseFunctionOrProcedureList(table);
//    }

//    public async Task<string?> GetFunctionDefinitionAsync(string functionName, string? arguments = null)
//    {
//        return await _fetcher.GetFunctionDefinitionAsync(functionName, arguments);
//    }

//    public async Task<string?> GetProcedureDefinitionAsync(string procedureName, string? arguments = null)
//    {
//        return await _fetcher.GetProcedureDefinitionAsync(procedureName, arguments);
//    }

//    public async Task<IList<UniqueConstraintDefinition>> GetUniqueConstraintsAsync(string tableName)
//    {
//        return await _fetcher.GetUniqueConstraintsListAsync(tableName);
//    }

//    public async Task<string?> GetUniqueConstraintCreateScriptAsync(string tableName, string constraintName)
//    {
//        return await _fetcher.GetUniqueConstraintCreateScriptAsync(tableName, constraintName);
//    }

//    public async Task<IList<string>> GetSequencesAsync()
//    {
//        return await _fetcher.GetSequenceNamesAsync();
//    }

//    public async Task<string?> GetSequenceDefinitionAsync(string sequenceName)
//    {
//        return await _fetcher.GetSequenceDefinitionAsync(sequenceName);
//    }

//    public async Task<string> GetCreateTableScriptAsync(string tableName)
//    {
//        return await _fetcher.GetCreateTableScriptAsync(tableName);
//    }

//    private List<DbFunctionDefinition> ParseFunctionOrProcedureList(System.Data.DataTable table)
//    {
//        var list = new List<DbFunctionDefinition>();

//        foreach (System.Data.DataRow row in table.Rows)
//        {
//            list.Add(new DbFunctionDefinition
//            {
//                Name = row["routine_name"].ToString(),
//                Arguments = row["arguments"]?.ToString(),
//                Definition = row["definition"]?.ToString(),
//            });
//        }

//        return list;
//    }

//    public async Task<string?> GetViewDefinitionAsync(string viewName)
//    {
//        return await _fetcher.GetViewDefinitionAsync(viewName);
//    }

//    public async Task<string?> GetTriggerDefinitionAsync(string triggerName)
//    {
//        return await _fetcher.GetTriggerDefinitionAsync(triggerName);
//    }

//    public async Task<IList<DbViewDefinition>> GetViewsAsync()
//    {
//        const string sql = @"
//        SELECT 
//            table_name AS view_name, 
//            view_definition 
//        FROM information_schema.views 
//        WHERE table_schema = 'public';
//    ";

//        var dataTable = await _connection.ExecuteQueryAsync(sql);
//        var list = new List<DbViewDefinition>();

//        foreach (DataRow row in dataTable.Rows)
//        {
//            list.Add(new DbViewDefinition
//            {
//                Name = row["view_name"].ToString(),
//                Definition = row["view_definition"]?.ToString()
//            });
//        }

//        return list;
//    }

//    public async Task<IList<DbTriggerDefinition>> GetTriggersAsync()
//    {
//        const string sql = @"
//        SELECT 
//            trg.tgname AS trigger_name,
//            tbl.relname AS table_name,
//            pg_get_triggerdef(trg.oid, true) AS definition
//        FROM pg_trigger trg
//        JOIN pg_class tbl ON tbl.oid = trg.tgrelid
//        JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
//        WHERE ns.nspname = 'public'
//          AND NOT trg.tgisinternal;
//    ";

//        var dataTable = await _connection.ExecuteQueryAsync(sql);
//        var list = new List<DbTriggerDefinition>();

//        foreach (DataRow row in dataTable.Rows)
//        {
//            list.Add(new DbTriggerDefinition
//            {
//                Name = row["trigger_name"].ToString(),
//                Table = row["table_name"].ToString(),
//                Definition = row["definition"]?.ToString()
//            });
//        }

//        return list;
//    }

//    public async Task<DatabaseIdentity> GetDatabaseIdentityAsync(CancellationToken cancellationToken = default)
//    {
//        var builder = new NpgsqlConnectionStringBuilder(_connectionString);

//        await using var connection = new NpgsqlConnection(_connectionString);
//        await connection.OpenAsync(cancellationToken);

//        const string sql = """
//        SELECT
//            inet_server_addr()::text AS server_address,
//            inet_server_port() AS server_port,
//            current_database() AS database_name,
//            current_user AS user_name,
//            version() AS version;
//        """;

//        await using var command = new NpgsqlCommand(sql, connection);
//        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

//        if (!await reader.ReadAsync(cancellationToken))
//        {
//            throw new InvalidOperationException("Unable to read PostgreSQL database identity.");
//        }

//        return new DatabaseIdentity
//        {
//            ProviderName = "PostgreSQL",
//            ConfiguredHost = builder.Host,
//            ConfiguredPort = builder.Port,
//            ConfiguredDatabase = builder.Database,
//            ConfiguredUsername = builder.Username,
//            ServerAddress = reader["server_address"]?.ToString(),
//            ServerPort = reader["server_port"] == DBNull.Value ? null : Convert.ToInt32(reader["server_port"]),
//            CurrentDatabase = reader["database_name"]?.ToString() ?? string.Empty,
//            CurrentUser = reader["user_name"]?.ToString() ?? string.Empty,
//            Version = reader["version"]?.ToString() ?? string.Empty
//        };
//    }
//}



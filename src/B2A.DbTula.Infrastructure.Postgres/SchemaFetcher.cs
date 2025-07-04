using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using System.Data;

namespace B2A.DbTula.Infrastructure.Postgres;

public class SchemaFetcher
{
    private readonly DatabaseConnection _connection;
    private readonly Action<string> _log;
    private readonly LogLevel _logLevel;

    public SchemaFetcher(DatabaseConnection connection, Action<string> log, object verbose, LogLevel logLevel = LogLevel.Basic)
    {
        _connection = connection;
        _log = log;
        _logLevel = logLevel;
    }

    #region Get All Schema Objects Tables, Functions, Procedures, Sequences
    public async Task<DataTable> GetTablesAsync()
    {
        string query = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'";
        return await ExecuteQueryAsync(query);
    }

    public async Task<DataTable> GetFunctionsAsync()
    {
        var query = @"
                        SELECT 
                            p.oid,
                            p.proname AS routine_name,
                            pg_get_function_identity_arguments(p.oid) AS arguments 
                        FROM pg_proc p 
                        JOIN pg_namespace n ON p.pronamespace = n.oid 
                        WHERE n.nspname = 'public' 
                          AND p.prokind = 'f';";
        return await ExecuteQueryAsync(query);
    }

    public async Task<DataTable> GetProceduresAsync()
    {
        var query = @"
                        SELECT 
                            p.oid,
                            p.proname AS routine_name,
                            pg_get_function_identity_arguments(p.oid) AS arguments 
                        FROM pg_proc p 
                        JOIN pg_namespace n ON p.pronamespace = n.oid 
                        WHERE n.nspname = 'public' 
                          AND p.prokind = 'p';"; // 'p' indicates procedures
        return await ExecuteQueryAsync(query);
    }

    public async Task<DataTable> GetSequencesAsync()
    {
        string query = @"
                        SELECT sequence_name 
                        FROM information_schema.sequences 
                        WHERE sequence_schema = 'public';";
        return await ExecuteQueryAsync(query);
    }

    #endregion

    #region Get Details of a specific table
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
            CreateScript = scriptTask.Result
        };
    }

    public async Task<DataTable> GetColumnsAsync(string tableName)
    {
        string query = $@"
        SELECT 
            column_name, 
            data_type, 
            character_maximum_length, 
            is_nullable,
            column_default
        FROM 
            information_schema.columns 
        WHERE 
            table_name = '{tableName}' 
            AND table_schema = 'public'";

        var result = await ExecuteQueryAsync(query);

        if (result.Columns.Contains("column_name"))
        {
            result.PrimaryKey = new DataColumn[] { result.Columns["column_name"] };
        }

        return result;
    }

    public async Task<List<ColumnDefinition>> GetColumnsListAsync(string tableName)
    {
        var columnList = new List<ColumnDefinition>();
        var columnDataTable = await GetColumnsAsync(tableName);

        foreach (DataRow row in columnDataTable.Rows)
        {
            columnList.Add(new ColumnDefinition
            {
                Name = row["column_name"].ToString() ?? string.Empty,
                DataType = row["data_type"].ToString() ?? string.Empty,
                IsNullable = row["is_nullable"].ToString() == "YES",
                Length = row["character_maximum_length"] != DBNull.Value
                         ? Convert.ToInt32(row["character_maximum_length"])
                         : null,
                DefaultValue = row.Table.Columns.Contains("column_default") && row["column_default"] != DBNull.Value
                               ? row["column_default"].ToString()
                               : null
            });
        }

        return columnList;
    }

    public async Task<List<string>> GetPrimaryKeysListAsync(string tableName)
    {
        var primaryKeyList = new List<string>();
        var primaryKeyDataTable = await GetPrimaryKeysAsync(tableName);

        foreach (DataRow row in primaryKeyDataTable.Rows)
        {
            primaryKeyList.Add(row["column_name"].ToString());
        }

        return primaryKeyList;
    }

    public async Task<List<ForeignKeyDefinition>> GetForeignKeysListAsync(string tableName)
    {
        var foreignKeyList = new List<ForeignKeyDefinition>();
        var foreignKeyDataTable = await GetForeignKeysAsync(tableName);

        foreach (DataRow row in foreignKeyDataTable.Rows)
        {
            var newFk = new ForeignKeyDefinition
            {
                Name = row["foreign_key_name"].ToString(),
                ColumnName = row["column_name"].ToString(),
                ReferencedTable = row["foreign_table_name"].ToString(),
                ReferencedColumn = row["foreign_column_name"].ToString()
            };

            if (!foreignKeyList.Any(fk =>
                fk.ColumnName == newFk.ColumnName &&
                fk.ReferencedTable == newFk.ReferencedTable &&
                fk.ReferencedColumn == newFk.ReferencedColumn))
            {
                foreignKeyList.Add(newFk);
            }
        }

        return foreignKeyList;
    }

    public async Task<string?> GetCreateTableScriptAsync(string tableName)
    {
        var query = $"SELECT pg_get_tabledef('{tableName}')"; // Replace with real query if needed
        var result = await _connection.ExecuteQueryAsync(query);
        return result.Rows.Count > 0 ? result.Rows[0][0].ToString() : null;
    }

    public async Task<List<IndexDefinition>> GetIndexesListAsync(string tableName)
    {
        var indexDataTable = await GetIndexesAsync(tableName);

        var grouped = indexDataTable.AsEnumerable()
            .GroupBy(row => row["indexname"].ToString())
            .Select(group =>
            {
                var firstRow = group.First();
                return new IndexDefinition
                {
                    Name = group.Key ?? string.Empty,
                    Columns = group.Select(r => r["columnname"].ToString() ?? string.Empty).ToList(),
                    IsUnique = firstRow["is_unique"] != DBNull.Value && (bool)firstRow["is_unique"],
                    IndexType = firstRow["index_type"]?.ToString() ?? string.Empty
                };
            });

        return grouped.ToList();
    }

    public async Task<DataTable> GetIndexesAsync(string tableName)
    {
        string query = $@"
                            SELECT
                                i.relname AS indexname,
                                t.relname AS tablename,
                                a.attname AS columnname,
                                ix.indisunique AS is_unique,
                                am.amname AS index_type
                            FROM
                                pg_class t
                                JOIN pg_index ix ON t.oid = ix.indrelid
                                JOIN pg_class i ON i.oid = ix.indexrelid
                                JOIN pg_am am ON i.relam = am.oid
                                JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
                            WHERE
                                t.relname = '{tableName}'
                                AND a.attnum > 0
                            ORDER BY
                                i.relname, a.attnum;
                        ";

        return await ExecuteQueryAsync(query);
    }

    public async Task<DataTable> GetPrimaryKeysAsync(string tableName)
    {
        string query = @"
                    SELECT 
                        tc.constraint_name,
                        kcu.column_name
                    FROM 
                        information_schema.table_constraints tc
                    JOIN 
                        information_schema.key_column_usage kcu 
                        ON tc.constraint_name = kcu.constraint_name
                        AND tc.table_schema = kcu.table_schema
                        AND tc.table_name = kcu.table_name
                    WHERE 
                        tc.table_name = @tableName
                        AND tc.constraint_type = 'PRIMARY KEY'
                        AND tc.table_schema = 'public'
                    ORDER BY 
                        kcu.ordinal_position;";

        var parameters = new Dictionary<string, object>
                {
                    { "@tableName", tableName }
                };

        return await ExecuteQueryAsync(query, parameters);
    }

    public async Task<DataTable> GetForeignKeysAsync(string tableName)
    {
        string query = $@"
                SELECT
                    tc.constraint_name AS foreign_key_name,
                    kcu.column_name,
                    ccu.table_name AS foreign_table_name,
                    ccu.column_name AS foreign_column_name
                FROM information_schema.table_constraints AS tc
                JOIN information_schema.key_column_usage AS kcu
                    ON tc.constraint_name = kcu.constraint_name
                JOIN information_schema.constraint_column_usage AS ccu
                    ON ccu.constraint_name = tc.constraint_name
                WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_name = '{tableName}' AND tc.table_schema = 'public'";

        return await ExecuteQueryAsync(query);
    }

    #endregion

    public async Task<string> GetFunctionDefinitionAsync(string functionName)
    {
        var query = @"
                        SELECT pg_get_functiondef(p.oid) AS definition
                        FROM pg_proc p
                        JOIN pg_namespace n ON p.pronamespace = n.oid
                        WHERE p.proname = @name AND n.nspname = 'public'
                        LIMIT 1;
                        ";
        var parameters = new Dictionary<string, object> { { "name", functionName } };
        var result = await ExecuteQueryAsync(query, parameters);
        return result.Rows.Count > 0 ? result.Rows[0]["definition"].ToString() : null;
    }

    public async Task<string> GetProcedureDefinitionAsync(string procedureName)
    {
        // PostgreSQL treats procedures and functions similarly under the hood
        return await GetFunctionDefinitionAsync(procedureName);
    }

    #region Get Indexe and Sequence Definitions
    public async Task<string> GetIndexDefinitionAsync(string indexName)
    {
        var query = @"
                        SELECT indexdef 
                        FROM pg_indexes 
                        WHERE indexname = @indexName AND schemaname = 'public';";

        var parameters = new Dictionary<string, object>
            {
                { "@indexName", indexName }
            };

        var result = await ExecuteQueryAsync(query, parameters);
        return result.Rows.Count > 0 ? result.Rows[0]["indexdef"].ToString() : null;
    }

    public async Task<string> GetSequenceDefinitionAsync(string sequenceName)
    {
        var query = @"
                        SELECT 'CREATE SEQUENCE ' || quote_ident(sequence_name) ||
                               ' START WITH ' || start_value ||
                               ' INCREMENT BY ' || increment_by ||
                               ' MINVALUE ' || min_value ||
                               ' MAXVALUE ' || max_value ||
                               ' CACHE ' || cache_size ||
                               CASE WHEN is_cycled THEN ' CYCLE' ELSE '' END AS definition
                        FROM information_schema.sequences
                        WHERE sequence_schema = 'public' AND sequence_name = @name;
                    ";

        var parameters = new Dictionary<string, object>
                {
                    { "@name", sequenceName }
                };

        var result = await ExecuteQueryAsync(query, parameters);
        return result.Rows.Count > 0 ? result.Rows[0]["definition"].ToString() : null;
    }

    #endregion

    public async Task EnsurePgGetTableDefFunctionExistsAsync()
    {
        string checkFunctionQuery = @"
                            SELECT EXISTS (
                                SELECT 1 
                                FROM pg_proc 
                                JOIN pg_namespace n ON n.oid = pg_proc.pronamespace
                                WHERE proname = 'pg_get_tabledef' AND n.nspname = 'public'
                            );";

        var result = await _connection.ExecuteQueryAsync(checkFunctionQuery);
        bool exists = result.Rows.Count > 0 && (bool)result.Rows[0][0];

        if (!exists)
        {
            string createFunctionSql = @"
                            CREATE OR REPLACE FUNCTION pg_get_tabledef(p_table_name TEXT)
                            RETURNS TEXT LANGUAGE plpgsql AS $$
                            DECLARE
                                col RECORD;
                                col_defs TEXT := '';
                                pk_cols TEXT := '';
                                result TEXT;
                            BEGIN
                                FOR col IN
                                    SELECT 
                                        column_name,
                                        data_type,
                                        character_maximum_length,
                                        numeric_precision,
                                        numeric_scale,
                                        is_nullable,
                                        column_default
                                    FROM information_schema.columns
                                    WHERE table_schema = 'public' AND table_name = p_table_name
                                    ORDER BY ordinal_position
                                LOOP
                                    col_defs := col_defs || 
                                        format('""%s"" %s%s%s%s, ',
                                            col.column_name,
                                            CASE 
                                                WHEN col.data_type = 'character varying' THEN format('varchar(%s)', col.character_maximum_length)
                                                WHEN col.data_type = 'numeric' THEN format('numeric(%s,%s)', col.numeric_precision, col.numeric_scale)
                                                ELSE col.data_type
                                            END,
                                            CASE WHEN col.column_default IS NOT NULL THEN ' DEFAULT ' || col.column_default ELSE '' END,
                                            CASE WHEN col.is_nullable = 'NO' THEN ' NOT NULL' ELSE '' END,
                                            ''
                                        );
                                END LOOP;

                                -- Get primary key columns
                                SELECT string_agg(format('""%s""', kcu.column_name), ', ')
                                INTO pk_cols
                                FROM information_schema.table_constraints tc
                                JOIN information_schema.key_column_usage kcu 
                                  ON tc.constraint_name = kcu.constraint_name
                                WHERE tc.table_schema = 'public' 
                                  AND tc.table_name = p_table_name 
                                  AND tc.constraint_type = 'PRIMARY KEY';

                                IF pk_cols IS NOT NULL THEN
                                    col_defs := col_defs || format('PRIMARY KEY (%s), ', pk_cols);
                                END IF;

                                col_defs := left(col_defs, length(col_defs) - 2);
                                result := format('CREATE TABLE ""%s"" (%s);', p_table_name, col_defs);
                                RETURN result;
                            END;
                            $$;
                            ";
            await _connection.ExecuteCommandAsync(createFunctionSql);
        }
    }

    private void Log(string message, LogLevel level = LogLevel.Basic)
    {
        if (_logLevel >= level)
        {
            _log?.Invoke(message);
        }
    }


    #region Execute Commands and Queries
    public async Task<DataTable> ExecuteQueryAsync(string query)
    {
        var result = await _connection.ExecuteQueryAsync(query);
        //Log($"Query: {query}", LogLevel.Verbose);
        //Log($"Rows returned: {result.Rows.Count}", LogLevel.Verbose);
        return result;
    }

    public async Task<DataTable> ExecuteQueryAsync(string query, Dictionary<string, object> parameters)
    {
        var result = await _connection.ExecuteQueryAsync(query, parameters);
        //Log($"Query: {query}", LogLevel.Verbose);
        //Log($"Rows returned: {result.Rows.Count}", LogLevel.Verbose);
        return result;
    }

    #endregion

}
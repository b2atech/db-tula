using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using System.Data;

namespace B2A.DbTula.Infrastructure.Postgres;

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
                            pg_get_function_identity_arguments(p.oid) AS arguments, 
                            pg_get_functiondef(p.oid) AS definition
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
                            pg_get_function_identity_arguments(p.oid) AS arguments,
                           pg_get_functiondef(p.oid) AS definition
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

    public async Task<DataTable> GetViewsAsync()
    {
        var query = @"
        SELECT 
            table_name,
            view_definition
        FROM information_schema.views
        WHERE table_schema = 'public';";
        return await ExecuteQueryAsync(query);
    }


    public async Task<DataTable> GetTriggersAsync()
    {
        var query = @"
        SELECT 
            trg.tgname AS trigger_name,
            tbl.relname AS table_name,
            pg_get_triggerdef(trg.oid, true) AS definition
        FROM pg_trigger trg
        JOIN pg_class tbl ON tbl.oid = trg.tgrelid
        JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
        WHERE ns.nspname = 'public'
          AND NOT trg.tgisinternal;";
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
        var uniqueTask = GetUniqueConstraintsListAsync(tableName);

        await Task.WhenAll(columnsTask, pkTask, fkTask, indexTask, uniqueTask);

        return new TableDefinition
        {
            Name = tableName,
            Columns = columnsTask.Result,
            PrimaryKeys = pkTask.Result,
            ForeignKeys = fkTask.Result,
            Indexes = indexTask.Result,
            UniqueConstraints = uniqueTask.Result,
        };
    }

    public async Task<DataTable> GetColumnsAsync(string tableName)
    {
        const string query = @"
        SELECT
            column_name,
            data_type,
            character_maximum_length,
            numeric_precision,
            numeric_scale,
            datetime_precision,
            is_nullable,
            column_default,
            is_generated,
            identity_generation
        FROM information_schema.columns
        WHERE table_name = @tableName
          AND table_schema = 'public'
        ORDER BY ordinal_position";

        var parameters = new Dictionary<string, object> { { "@tableName", tableName } };
        var result = await ExecuteQueryAsync(query, parameters);

        if (result.Columns.Contains("column_name"))
            result.PrimaryKey = [result.Columns["column_name"]!];

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
                         ? Convert.ToInt32(row["character_maximum_length"]) : null,
                NumericPrecision = row["numeric_precision"] != DBNull.Value
                         ? Convert.ToInt32(row["numeric_precision"]) : null,
                NumericScale = row["numeric_scale"] != DBNull.Value
                         ? Convert.ToInt32(row["numeric_scale"]) : null,
                DateTimePrecision = row["datetime_precision"] != DBNull.Value
                         ? Convert.ToInt32(row["datetime_precision"]) : null,
                DefaultValue = row["column_default"] != DBNull.Value
                               ? row["column_default"].ToString() : null,
                IsIdentity = row["identity_generation"] != DBNull.Value
                             && !string.IsNullOrEmpty(row["identity_generation"].ToString()),
                IsComputed = row["is_generated"].ToString() == "ALWAYS",
            });
        }

        return columnList;
    }

    public async Task<List<PrimaryKeyDefinition>> GetPrimaryKeysListAsync(string tableName)
    {
        var dataTable = await GetPrimaryKeysAsync(tableName);

        // Group by constraint_name to build one PK with all its columns in ordinal order
        var primaryKeys = dataTable.AsEnumerable()
            .GroupBy(row => row["constraint_name"].ToString() ?? string.Empty)
            .Select(g => new PrimaryKeyDefinition
            {
                Name = g.Key,
                Columns = g
                    .Select(r => r["column_name"].ToString() ?? string.Empty)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToList()
            })
            .ToList();

        return primaryKeys;
    }

    public async Task<List<ForeignKeyDefinition>> GetForeignKeysListAsync(string tableName)
    {
        var foreignKeyDataTable = await GetForeignKeysAsync(tableName);

        // Group by constraint name to collapse multi-column FKs (each column is one row)
        return foreignKeyDataTable.AsEnumerable()
            .GroupBy(row => row["foreign_key_name"].ToString() ?? string.Empty)
            .Select(g =>
            {
                var first = g.First();
                return new ForeignKeyDefinition
                {
                    Name = g.Key,
                    ColumnName = string.Join(",", g.Select(r => r["column_name"].ToString())),
                    ReferencedTable = first["foreign_table_name"].ToString() ?? string.Empty,
                    ReferencedColumn = string.Join(",", g.Select(r => r["foreign_column_name"].ToString())),
                    OnUpdate = first["on_update"].ToString() ?? "NO ACTION",
                    OnDelete = first["on_delete"].ToString() ?? "NO ACTION",
                };
            })
            .ToList();
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
                    // Rows are already ordered by cols.ord from the query — column order is preserved
                    Columns = group.Select(r => r["columnname"].ToString() ?? string.Empty).ToList(),
                    IsUnique = firstRow["is_unique"] != DBNull.Value && (bool)firstRow["is_unique"],
                    IndexType = firstRow["index_type"]?.ToString() ?? string.Empty,
                    Predicate = firstRow["predicate"] != DBNull.Value ? firstRow["predicate"]?.ToString() : null,
                };
            });

        return grouped.ToList();
    }

    public async Task<DataTable> GetIndexesAsync(string tableName)
    {
        const string query = @"
            SELECT
                i.relname AS indexname,
                t.relname AS tablename,
                a.attname AS columnname,
                ix.indisunique AS is_unique,
                ix.indisvalid AS is_valid,
                am.amname AS index_type,
                pg_get_expr(ix.indpred, ix.indrelid, true) AS predicate
            FROM pg_class t
            JOIN pg_index ix ON t.oid = ix.indrelid
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_am am ON i.relam = am.oid
            JOIN pg_namespace ns ON ns.oid = t.relnamespace
            CROSS JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS cols(attnum, ord)
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = cols.attnum
            WHERE t.relname = @tableName
              AND ns.nspname = 'public'
              AND ix.indisvalid = true
              AND ix.indisprimary = false
              AND a.attnum > 0
            ORDER BY i.relname, cols.ord;";

        var parameters = new Dictionary<string, object> { { "@tableName", tableName } };
        return await ExecuteQueryAsync(query, parameters);
    }

    public async Task<string?> GetIndexCreateScriptAsync(string indexName)
    {
        const string sql = @"
        SELECT pg_get_indexdef(indexrelid) AS create_script
        FROM pg_index idx
        JOIN pg_class cls ON cls.oid = idx.indexrelid
        WHERE cls.relname = @indexName
        LIMIT 1;";

        var parameters = new Dictionary<string, object>
    {
        { "@indexName", indexName }
    };

        var result = await ExecuteQueryAsync(sql, parameters);

        return result.Rows.Count > 0
            ? result.Rows[0]["create_script"]?.ToString()
            : null;
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

    public async Task<string?> GetPrimaryKeyCreateScriptAsync(string tableName)
    {
        const string sql = @"
        SELECT
            'ALTER TABLE ""' || rel.relname || '"" ADD CONSTRAINT ""' || con.conname || '"" ' || 
            pg_get_constraintdef(con.oid, true) || ';' AS create_script
        FROM pg_constraint con
        JOIN pg_class rel ON rel.oid = con.conrelid
        WHERE con.contype = 'p'
          AND rel.relname = @tableName
          AND rel.relnamespace = 'public'::regnamespace
        LIMIT 1;";

        var parameters = new Dictionary<string, object>
    {
        { "@tableName", tableName }
    };

        var result = await ExecuteQueryAsync(sql, parameters);
        return result.Rows.Count > 0
            ? result.Rows[0]["create_script"]?.ToString()
            : null;
    }

    public async Task<DataTable> GetForeignKeysAsync(string tableName)
    {
        const string query = @"
            SELECT
                con.conname AS foreign_key_name,
                src_att.attname AS column_name,
                ref_rel.relname AS foreign_table_name,
                ref_att.attname AS foreign_column_name,
                CASE con.confupdtype
                    WHEN 'a' THEN 'NO ACTION'  WHEN 'r' THEN 'RESTRICT'
                    WHEN 'c' THEN 'CASCADE'    WHEN 'n' THEN 'SET NULL'
                    WHEN 'd' THEN 'SET DEFAULT' END AS on_update,
                CASE con.confdeltype
                    WHEN 'a' THEN 'NO ACTION'  WHEN 'r' THEN 'RESTRICT'
                    WHEN 'c' THEN 'CASCADE'    WHEN 'n' THEN 'SET NULL'
                    WHEN 'd' THEN 'SET DEFAULT' END AS on_delete
            FROM pg_constraint con
            JOIN pg_class rel ON rel.oid = con.conrelid
            JOIN pg_namespace ns ON ns.oid = rel.relnamespace
            JOIN pg_class ref_rel ON ref_rel.oid = con.confrelid
            JOIN LATERAL unnest(con.conkey)  WITH ORDINALITY AS src(attnum, ord) ON true
            JOIN LATERAL unnest(con.confkey) WITH ORDINALITY AS ref(attnum, ord) ON ref.ord = src.ord
            JOIN pg_attribute src_att ON src_att.attrelid = con.conrelid  AND src_att.attnum = src.attnum
            JOIN pg_attribute ref_att ON ref_att.attrelid = con.confrelid AND ref_att.attnum = ref.attnum
            WHERE con.contype = 'f'
              AND rel.relname = @tableName
              AND ns.nspname = 'public'
            ORDER BY con.conname, src.ord;";

        var parameters = new Dictionary<string, object> { { "@tableName", tableName } };
        return await ExecuteQueryAsync(query, parameters);
    }

    public async Task<string?> GetForeignKeyCreateScriptAsync(string tableName, string foreignKeyName)
    {
        const string sql = @"
    SELECT
        'ALTER TABLE ""' || rel.relname || '"" ADD CONSTRAINT ""' || con.conname || '"" ' ||
        pg_get_constraintdef(con.oid, true) || ';' AS create_script
    FROM pg_constraint con
    JOIN pg_class rel ON rel.oid = con.conrelid
    WHERE con.contype = 'f'  -- 'f' means foreign key
      AND rel.relname = @tableName
      AND con.conname = @foreignKeyName
      AND rel.relnamespace = 'public'::regnamespace
    LIMIT 1;";

        var parameters = new Dictionary<string, object>
    {
        { "@tableName", tableName },
        { "@foreignKeyName", foreignKeyName }
    };

        var result = await ExecuteQueryAsync(sql, parameters);
        return result.Rows.Count > 0
            ? result.Rows[0]["create_script"]?.ToString()
            : null;
    }


    public async Task<List<UniqueConstraintDefinition>> GetUniqueConstraintsListAsync(string tableName)
    {
        const string sql = @"
            SELECT
                con.conname AS constraint_name,
                att.attname AS column_name
            FROM pg_constraint con
            JOIN pg_class rel ON rel.oid = con.conrelid
            JOIN pg_namespace ns ON ns.oid = rel.relnamespace
            JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = ANY(con.conkey)
            WHERE con.contype = 'u'
              AND ns.nspname = 'public'
              AND rel.relname = @tableName
            ORDER BY con.conname, att.attnum;";

        var parameters = new Dictionary<string, object> { { "@tableName", tableName } };
        var dataTable = await ExecuteQueryAsync(sql, parameters);

        return dataTable.AsEnumerable()
            .GroupBy(row => row["constraint_name"].ToString() ?? string.Empty)
            .Select(g => new UniqueConstraintDefinition
            {
                Name = g.Key,
                Columns = g
                    .Select(r => r["column_name"].ToString() ?? string.Empty)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToList()
            })
            .ToList();
    }

    public async Task<string?> GetUniqueConstraintCreateScriptAsync(string tableName, string constraintName)
    {
        const string sql = @"
            SELECT 'ALTER TABLE ""' || rel.relname || '"" ADD CONSTRAINT ""' || con.conname || '"" ' ||
                   pg_get_constraintdef(con.oid, true) || ';' AS create_script
            FROM pg_constraint con
            JOIN pg_class rel ON rel.oid = con.conrelid
            WHERE con.contype = 'u'
              AND rel.relname = @tableName
              AND con.conname = @constraintName
              AND rel.relnamespace = 'public'::regnamespace
            LIMIT 1;";

        var parameters = new Dictionary<string, object>
        {
            { "@tableName", tableName },
            { "@constraintName", constraintName }
        };

        var result = await ExecuteQueryAsync(sql, parameters);
        return result.Rows.Count > 0 ? result.Rows[0]["create_script"]?.ToString() : null;
    }

    public async Task<HashSet<string>> GetMaterializedViewNamesAsync()
    {
        const string sql = @"
            SELECT relname
            FROM pg_class
            JOIN pg_namespace n ON n.oid = relnamespace
            WHERE relkind = 'm'
              AND n.nspname = 'public';";

        var dataTable = await ExecuteQueryAsync(sql);
        return dataTable.AsEnumerable()
            .Select(row => row["relname"].ToString() ?? string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IList<string>> GetSequenceNamesAsync()
    {
        const string sql = @"
            SELECT sequence_name
            FROM information_schema.sequences
            WHERE sequence_schema = 'public'
            ORDER BY sequence_name;";

        var dataTable = await ExecuteQueryAsync(sql);
        return dataTable.AsEnumerable()
            .Select(row => row["sequence_name"].ToString() ?? string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
    }

    #endregion

    public async Task<string?> GetFunctionDefinitionAsync(string functionName, string? arguments = null)
    {
        string query;
        Dictionary<string, object> parameters;

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            query = @"
                SELECT pg_get_functiondef(p.oid) AS definition
                FROM pg_proc p
                JOIN pg_namespace n ON p.pronamespace = n.oid
                WHERE p.proname = @name
                  AND n.nspname = 'public'
                  AND pg_get_function_identity_arguments(p.oid) = @arguments
                LIMIT 1;";
            parameters = new Dictionary<string, object> { { "name", functionName }, { "arguments", arguments } };
        }
        else
        {
            query = @"
                SELECT pg_get_functiondef(p.oid) AS definition
                FROM pg_proc p
                JOIN pg_namespace n ON p.pronamespace = n.oid
                WHERE p.proname = @name AND n.nspname = 'public'
                LIMIT 1;";
            parameters = new Dictionary<string, object> { { "name", functionName } };
        }

        var result = await ExecuteQueryAsync(query, parameters);
        return result.Rows.Count > 0 ? result.Rows[0]["definition"].ToString() : null;
    }

    public async Task<string?> GetProcedureDefinitionAsync(string procedureName, string? arguments = null)
    {
        return await GetFunctionDefinitionAsync(procedureName, arguments);
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
                        SELECT 'CREATE SEQUENCE ' || quote_ident(sequencename) ||
                               ' START WITH ' || start_value ||
                               ' INCREMENT BY ' || increment_by ||
                               ' MINVALUE ' || min_value ||
                               ' MAXVALUE ' || max_value ||
                               ' CACHE ' || cache_size ||
                               CASE WHEN cycle THEN ' CYCLE' ELSE '' END AS definition
                        FROM pg_sequences
                        WHERE schemaname = 'public' AND sequencename = @name;
                    ";

        var parameters = new Dictionary<string, object>
                {
                    { "@name", sequenceName }
                };

        var result = await ExecuteQueryAsync(query, parameters);
        return result.Rows.Count > 0 ? result.Rows[0]["definition"].ToString() : null;
    }

    #endregion


    private void Log(string message, LogLevel level = LogLevel.Basic)
    {
        if (_logLevel >= level)
        {
            _logger?.Invoke(0, 0, message, false);
        }
    }


    #region Execute Commands and Queries
    public async Task<DataTable> ExecuteQueryAsync(string query)
    {
        var result = await _connection.ExecuteQueryAsync(query);
        Log($"Query: {query}", LogLevel.Verbose);
        Log($"Rows returned: {result.Rows.Count}", LogLevel.Verbose);
        return result;
    }

    public async Task<DataTable> ExecuteQueryAsync(string query, Dictionary<string, object> parameters)
    {
        var result = await _connection.ExecuteQueryAsync(query, parameters);
        Log($"Query: {query}", LogLevel.Verbose);
        Log($"Rows returned: {result.Rows.Count}", LogLevel.Verbose);
        return result;
    }

    internal async Task<string?> GetViewDefinitionAsync(string viewName)
    {
        const string sql = @"
        SELECT pg_get_viewdef(c.oid, true)
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind = 'v'
          AND n.nspname = 'public'
          AND c.relname = @viewName
        LIMIT 1;";

        var parameters = new Dictionary<string, object>
    {
        { "viewName", viewName }
    };

        var result = await _connection.ExecuteQueryAsync(sql, parameters);
        if (result.Rows.Count > 0 && result.Rows[0][0] != DBNull.Value)
            return result.Rows[0][0].ToString();

        return null;
    }


    internal async Task<string?> GetTriggerDefinitionAsync(string triggerName)
    {
        const string sql = @"
        SELECT pg_get_triggerdef(t.oid, true)
        FROM pg_trigger t
        JOIN pg_class c ON c.oid = t.tgrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE t.tgname = @triggerName
          AND n.nspname = 'public'
        LIMIT 1;";

        var parameters = new Dictionary<string, object>
    {
        { "triggerName", triggerName }
    };

        var result = await _connection.ExecuteQueryAsync(sql, parameters);
        if (result.Rows.Count > 0 && result.Rows[0][0] != DBNull.Value)
            return result.Rows[0][0].ToString();

        return null;
    }


    #endregion

}
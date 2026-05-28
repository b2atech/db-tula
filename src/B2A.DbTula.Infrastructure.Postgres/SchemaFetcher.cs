using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using System.Data;
using System.Reflection;
using System.Text;

namespace B2A.DbTula.Infrastructure.Postgres;
 

public class SchemaFetcher
{
    private readonly DatabaseConnection _connection;
    private readonly Action<int, int, string, bool>? _logger;
    private readonly bool _verbose;
    private readonly LogLevel _logLevel;

    public SchemaFetcher(
        DatabaseConnection connection,
        Action<int, int, string, bool>? logger,
        bool verbose,
        LogLevel logLevel = LogLevel.Basic)
    {
        _connection = connection;
        _logger = logger;
        _verbose = verbose;
        _logLevel = logLevel;
    }

    public async Task<DataTable> GetTablesAsync()
    {
        const string sql = """
            SELECT
                table_schema,
                table_name,
                table_schema || '.' || table_name AS full_name
            FROM information_schema.tables
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
              AND table_type = 'BASE TABLE'
            ORDER BY table_schema, table_name;
            """;

        return await _connection.ExecuteQueryAsync(sql);
    }

    public async Task<TableDefinition> GetTableDefinitionAsync(string tableName)
    {
        var objectName = PostgresObjectName.Parse(tableName);

        var tableDefinition = new TableDefinition
        {
            Name = objectName.FullName,
            Columns = (await GetColumnsListAsync(objectName.FullName)).ToList(),
            PrimaryKeys = (await GetPrimaryKeysListAsync(objectName.FullName)).ToList(),
            ForeignKeys = (await GetForeignKeysListAsync(objectName.FullName)).ToList(),
            Indexes = (await GetIndexesListAsync(objectName.FullName)).ToList(),
            CreateScript = await GetCreateTableScriptAsync(objectName.FullName)
        };

        return tableDefinition;
    }

    public async Task<IList<ColumnDefinition>> GetColumnsListAsync(string tableName)
    {
        var objectName = PostgresObjectName.Parse(tableName);

        const string sql = """
            SELECT
                column_name,
                data_type,
                udt_name,
                character_maximum_length,
                numeric_precision,
                numeric_scale,
                is_nullable,
                column_default,
                is_identity
            FROM information_schema.columns
            WHERE table_schema = @schemaName
              AND table_name = @tableName
            ORDER BY ordinal_position;
            """;

        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["schemaName"] = objectName.Schema,
            ["tableName"] = objectName.Name
        });

        var columns = new List<ColumnDefinition>();

        foreach (DataRow row in table.Rows)
        {
            var column = new ColumnDefinition
            {
                Name = row["column_name"]?.ToString() ?? string.Empty,
                DataType = BuildPostgresDataType(row),
                IsNullable = string.Equals(row["is_nullable"]?.ToString(), "YES", StringComparison.OrdinalIgnoreCase)
            };

            TrySetProperty(column, "DefaultValue", row["column_default"]?.ToString());
            TrySetProperty(column, "Default", row["column_default"]?.ToString());
            TrySetProperty(column, "ColumnDefault", row["column_default"]?.ToString());
            TrySetProperty(column, "MaxLength", row["character_maximum_length"]?.ToString());
            TrySetProperty(column, "Length", row["character_maximum_length"]?.ToString());
            TrySetProperty(column, "CharacterMaximumLength", row["character_maximum_length"]?.ToString());
            TrySetProperty(column, "IsIdentity", string.Equals(row["is_identity"]?.ToString(), "YES", StringComparison.OrdinalIgnoreCase));

            columns.Add(column);
        }

        return columns;
    }

    public async Task<IList<PrimaryKeyDefinition>> GetPrimaryKeysListAsync(string tableName)
    {
        var objectName = PostgresObjectName.Parse(tableName);

        const string sql = """
            SELECT
                tc.constraint_name,
                kcu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_schema = kcu.table_schema
             AND tc.table_name = kcu.table_name
            WHERE tc.constraint_type = 'PRIMARY KEY'
              AND tc.table_schema = @schemaName
              AND tc.table_name = @tableName
            ORDER BY kcu.ordinal_position;
            """;

        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["schemaName"] = objectName.Schema,
            ["tableName"] = objectName.Name
        });

        var grouped = table.Rows.Cast<DataRow>()
            .GroupBy(r => r["constraint_name"]?.ToString() ?? string.Empty)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key));

        var result = new List<PrimaryKeyDefinition>();

        foreach (var group in grouped)
        {
            result.Add(new PrimaryKeyDefinition
            {
                Name = group.Key,
                Columns = group.Select(r => r["column_name"]?.ToString() ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList()
            });
        }

        return result;
    }

    public async Task<string?> GetPrimaryKeyCreateScriptAsync(string tableName)
    {
        var objectName = PostgresObjectName.Parse(tableName);

        const string sql = """
            SELECT
                'ALTER TABLE "' || n.nspname || '"."' || c.relname || '" ADD CONSTRAINT "' ||
                con.conname || '" ' || pg_get_constraintdef(con.oid, true) || ';' AS create_script
            FROM pg_constraint con
            JOIN pg_class c ON c.oid = con.conrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE con.contype = 'p'
              AND n.nspname = @schemaName
              AND c.relname = @tableName
            LIMIT 1;
            """;

        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["schemaName"] = objectName.Schema,
            ["tableName"] = objectName.Name
        });

        return table.Rows.Count == 0
            ? null
            : table.Rows[0]["create_script"]?.ToString();
    }

    public async Task<IList<ForeignKeyDefinition>> GetForeignKeysListAsync(string tableName)
    {
        var objectName = PostgresObjectName.Parse(tableName);

        const string sql = """
            SELECT
                con.conname AS constraint_name,
                src_ns.nspname AS source_schema,
                src_tbl.relname AS source_table,
                src_col.attname AS source_column,
                ref_ns.nspname AS referenced_schema,
                ref_tbl.relname AS referenced_table,
                ref_col.attname AS referenced_column,
                pg_get_constraintdef(con.oid, true) AS definition
            FROM pg_constraint con
            JOIN pg_class src_tbl ON src_tbl.oid = con.conrelid
            JOIN pg_namespace src_ns ON src_ns.oid = src_tbl.relnamespace
            JOIN pg_class ref_tbl ON ref_tbl.oid = con.confrelid
            JOIN pg_namespace ref_ns ON ref_ns.oid = ref_tbl.relnamespace
            JOIN unnest(con.conkey) WITH ORDINALITY AS src_cols(attnum, ord) ON true
            JOIN unnest(con.confkey) WITH ORDINALITY AS ref_cols(attnum, ord) ON src_cols.ord = ref_cols.ord
            JOIN pg_attribute src_col ON src_col.attrelid = src_tbl.oid AND src_col.attnum = src_cols.attnum
            JOIN pg_attribute ref_col ON ref_col.attrelid = ref_tbl.oid AND ref_col.attnum = ref_cols.attnum
            WHERE con.contype = 'f'
              AND src_ns.nspname = @schemaName
              AND src_tbl.relname = @tableName
            ORDER BY con.conname, src_cols.ord;
            """;

        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["schemaName"] = objectName.Schema,
            ["tableName"] = objectName.Name
        });

        var result = new List<ForeignKeyDefinition>();

        var groups = table.Rows.Cast<DataRow>()
            .GroupBy(r => r["constraint_name"]?.ToString() ?? string.Empty)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key));

        foreach (var group in groups)
        {
            var first = group.First();

            var sourceColumns = group
                .Select(r => r["source_column"]?.ToString() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var referencedColumns = group
                .Select(r => r["referenced_column"]?.ToString() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var referencedTable =
                $"{first["referenced_schema"]}.{first["referenced_table"]}";

            var foreignKey = new ForeignKeyDefinition
            {
                Name = group.Key,
                ReferencedTable = referencedTable
            };

            // Your actual ForeignKeyDefinition model does not have Columns / ReferencedColumns.
            // So we map safely to whichever property names actually exist.
            TrySetProperty(foreignKey, "Columns", sourceColumns);
            TrySetProperty(foreignKey, "ColumnNames", sourceColumns);
            TrySetProperty(foreignKey, "SourceColumns", sourceColumns);
            TrySetProperty(foreignKey, "ForeignKeyColumns", sourceColumns);

            TrySetProperty(foreignKey, "Column", sourceColumns.FirstOrDefault());
            TrySetProperty(foreignKey, "ColumnName", sourceColumns.FirstOrDefault());
            TrySetProperty(foreignKey, "SourceColumn", sourceColumns.FirstOrDefault());
            TrySetProperty(foreignKey, "ForeignKeyColumn", sourceColumns.FirstOrDefault());

            TrySetProperty(foreignKey, "ReferencedColumns", referencedColumns);
            TrySetProperty(foreignKey, "ReferencedColumnNames", referencedColumns);
            TrySetProperty(foreignKey, "ReferenceColumns", referencedColumns);
            TrySetProperty(foreignKey, "PrincipalColumns", referencedColumns);

            TrySetProperty(foreignKey, "ReferencedColumn", referencedColumns.FirstOrDefault());
            TrySetProperty(foreignKey, "ReferencedColumnName", referencedColumns.FirstOrDefault());
            TrySetProperty(foreignKey, "ReferenceColumn", referencedColumns.FirstOrDefault());
            TrySetProperty(foreignKey, "PrincipalColumn", referencedColumns.FirstOrDefault());

            TrySetProperty(foreignKey, "Definition", first["definition"]?.ToString());

            result.Add(foreignKey);
        }

        return result;
    }

    public async Task<string?> GetForeignKeyCreateScriptAsync(string tableName, string foreignKeyName)
    {
        var objectName = PostgresObjectName.Parse(tableName);

        const string sql = """
            SELECT
                'ALTER TABLE "' || src_ns.nspname || '"."' || src_tbl.relname || '" ADD CONSTRAINT "' ||
                con.conname || '" ' || pg_get_constraintdef(con.oid, true) || ';' AS create_script
            FROM pg_constraint con
            JOIN pg_class src_tbl ON src_tbl.oid = con.conrelid
            JOIN pg_namespace src_ns ON src_ns.oid = src_tbl.relnamespace
            WHERE con.contype = 'f'
              AND src_ns.nspname = @schemaName
              AND src_tbl.relname = @tableName
              AND con.conname = @foreignKeyName
            LIMIT 1;
            """;

        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["schemaName"] = objectName.Schema,
            ["tableName"] = objectName.Name,
            ["foreignKeyName"] = foreignKeyName
        });

        return table.Rows.Count == 0
            ? null
            : table.Rows[0]["create_script"]?.ToString();
    }

    public async Task<IList<IndexDefinition>> GetIndexesListAsync(string tableName)
    {
        var objectName = PostgresObjectName.Parse(tableName);

        const string sql = """
            SELECT
                ns.nspname AS schema_name,
                tbl.relname AS table_name,
                idx.relname AS index_name,
                am.amname AS index_type,
                ix.indisunique AS is_unique,
                ix.indisprimary AS is_primary,
                pg_get_indexdef(ix.indexrelid) AS definition,
                array_remove(array_agg(att.attname ORDER BY arr.ordinality), NULL) AS columns
            FROM pg_index ix
            JOIN pg_class tbl ON tbl.oid = ix.indrelid
            JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
            JOIN pg_class idx ON idx.oid = ix.indexrelid
            JOIN pg_am am ON am.oid = idx.relam
            LEFT JOIN unnest(ix.indkey) WITH ORDINALITY AS arr(attnum, ordinality) ON true
            LEFT JOIN pg_attribute att ON att.attrelid = tbl.oid AND att.attnum = arr.attnum
            WHERE ns.nspname = @schemaName
              AND tbl.relname = @tableName
              AND ix.indisprimary = false
            GROUP BY ns.nspname, tbl.relname, idx.relname, am.amname, ix.indisunique, ix.indisprimary, ix.indexrelid
            ORDER BY idx.relname;
            """;

        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["schemaName"] = objectName.Schema,
            ["tableName"] = objectName.Name
        });

        var result = new List<IndexDefinition>();

        foreach (DataRow row in table.Rows)
        {
            var index = new IndexDefinition
            {
                Name = row["index_name"]?.ToString() ?? string.Empty,
                Columns = ParsePostgresArray(row["columns"]?.ToString()),
                IsUnique = row["is_unique"] != DBNull.Value && Convert.ToBoolean(row["is_unique"])
            };

            TrySetProperty(index, "Type", row["index_type"]?.ToString() ?? "btree");
            TrySetProperty(index, "IndexType", row["index_type"]?.ToString() ?? "btree");
            TrySetProperty(index, "Definition", row["definition"]?.ToString());

            result.Add(index);
        }

        return result;
    }

    public async Task<string?> GetIndexCreateScriptAsync(string indexName)
    {
        const string sql = """
            SELECT pg_get_indexdef(idx.oid) AS create_script
            FROM pg_class idx
            JOIN pg_namespace ns ON ns.oid = idx.relnamespace
            WHERE idx.relkind = 'i'
              AND idx.relname = @indexName
            ORDER BY ns.nspname
            LIMIT 1;
            """;

        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["indexName"] = indexName
        });

        return table.Rows.Count == 0
            ? null
            : table.Rows[0]["create_script"]?.ToString();
    }

    public async Task<DataTable> GetFunctionsAsync()
    {
        const string sql = """
            SELECT
                n.nspname AS routine_schema,
                p.oid,
                p.proname AS routine_name,
                pg_get_function_identity_arguments(p.oid) AS arguments,
                pg_get_functiondef(p.oid) AS definition
            FROM pg_proc p
            JOIN pg_namespace n ON p.pronamespace = n.oid
            LEFT JOIN pg_depend d
                ON d.objid = p.oid
               AND d.deptype = 'e'
            LEFT JOIN pg_extension e
                ON e.oid = d.refobjid
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND p.prokind = 'f'
              AND e.oid IS NULL
            ORDER BY n.nspname, p.proname;
            """;

        return await _connection.ExecuteQueryAsync(sql);
    }

    public async Task<DataTable> GetProceduresAsync()
    {
        const string sql = """
            SELECT
                n.nspname AS routine_schema,
                p.oid,
                p.proname AS routine_name,
                pg_get_function_identity_arguments(p.oid) AS arguments,
                pg_get_functiondef(p.oid) AS definition
            FROM pg_proc p
            JOIN pg_namespace n ON p.pronamespace = n.oid
            LEFT JOIN pg_depend d
                ON d.objid = p.oid
               AND d.deptype = 'e'
            LEFT JOIN pg_extension e
                ON e.oid = d.refobjid
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND p.prokind = 'p'
              AND e.oid IS NULL
            ORDER BY n.nspname, p.proname;
            """;

        return await _connection.ExecuteQueryAsync(sql);
    }

    public async Task<string> GetFunctionDefinitionAsync(string functionName)
    {
        var objectName = PostgresObjectName.ParseRoutine(functionName);

        const string sql = """
            SELECT pg_get_functiondef(p.oid) AS definition
            FROM pg_proc p
            JOIN pg_namespace n ON p.pronamespace = n.oid
            WHERE n.nspname = @schemaName
              AND p.proname = @routineName
              AND p.prokind = 'f'
            ORDER BY p.oid
            LIMIT 1;
            """;

        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["schemaName"] = objectName.Schema,
            ["routineName"] = objectName.Name
        });

        return table.Rows.Count == 0
            ? string.Empty
            : table.Rows[0]["definition"]?.ToString() ?? string.Empty;
    }

    public async Task<string> GetProcedureDefinitionAsync(string procedureName)
    {
        var objectName = PostgresObjectName.ParseRoutine(procedureName);

        const string sql = """
            SELECT pg_get_functiondef(p.oid) AS definition
            FROM pg_proc p
            JOIN pg_namespace n ON p.pronamespace = n.oid
            WHERE n.nspname = @schemaName
              AND p.proname = @routineName
              AND p.prokind = 'p'
            ORDER BY p.oid
            LIMIT 1;
            """;

        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["schemaName"] = objectName.Schema,
            ["routineName"] = objectName.Name
        });

        return table.Rows.Count == 0
            ? string.Empty
            : table.Rows[0]["definition"]?.ToString() ?? string.Empty;
    }

    public async Task<DataTable> GetViewsAsync()
    {
        const string sql = """
            SELECT
                table_schema,
                table_name AS view_name,
                view_definition
            FROM information_schema.views
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY table_schema, table_name;
            """;

        return await _connection.ExecuteQueryAsync(sql);
    }

    public async Task<string?> GetViewDefinitionAsync(string viewName)
    {
        var objectName = PostgresObjectName.Parse(viewName);

        const string sql = """
            SELECT view_definition
            FROM information_schema.views
            WHERE table_schema = @schemaName
              AND table_name = @viewName
            LIMIT 1;
            """;

        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["schemaName"] = objectName.Schema,
            ["viewName"] = objectName.Name
        });

        return table.Rows.Count == 0
            ? null
            : table.Rows[0]["view_definition"]?.ToString();
    }

    public async Task<DataTable> GetTriggersAsync()
    {
        const string sql = """
            SELECT 
                ns.nspname AS table_schema,
                trg.tgname AS trigger_name,
                tbl.relname AS table_name,
                pg_get_triggerdef(trg.oid, true) AS definition
            FROM pg_trigger trg
            JOIN pg_class tbl ON tbl.oid = trg.tgrelid
            JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
            WHERE ns.nspname NOT IN ('pg_catalog', 'information_schema')
              AND NOT trg.tgisinternal
            ORDER BY ns.nspname, tbl.relname, trg.tgname;
            """;

        return await _connection.ExecuteQueryAsync(sql);
    }

    public async Task<string?> GetTriggerDefinitionAsync(string triggerName)
    {
        const string sql = """
            SELECT pg_get_triggerdef(trg.oid, true) AS definition
            FROM pg_trigger trg
            JOIN pg_class tbl ON tbl.oid = trg.tgrelid
            JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
            WHERE trg.tgname = @triggerName
              AND NOT trg.tgisinternal
            ORDER BY ns.nspname, tbl.relname
            LIMIT 1;
            """;

        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["triggerName"] = triggerName
        });

        return table.Rows.Count == 0
            ? null
            : table.Rows[0]["definition"]?.ToString();
    }

    public async Task<string> GetCreateTableScriptAsync(string tableName)
    {
        var objectName = PostgresObjectName.Parse(tableName);

        var columns = await GetColumnsListAsync(objectName.FullName);
        var primaryKeys = await GetPrimaryKeysListAsync(objectName.FullName);

        var sb = new StringBuilder();

        sb.Append($"CREATE TABLE \"{objectName.Schema}\".\"{objectName.Name}\" (");

        var definitions = new List<string>();

        foreach (var column in columns)
        {
            var columnDefinition = new StringBuilder();

            columnDefinition.Append($"\"{column.Name}\" {column.DataType}");

            var defaultValue = GetColumnPropertyValue(column, "DefaultValue", "Default", "ColumnDefault");

            if (!string.IsNullOrWhiteSpace(defaultValue))
            {
                columnDefinition.Append($" DEFAULT {defaultValue}");
            }

            if (!column.IsNullable)
            {
                columnDefinition.Append(" NOT NULL");
            }

            definitions.Add(columnDefinition.ToString());
        }

        var pk = primaryKeys.FirstOrDefault();

        if (pk != null && pk.Columns.Any())
        {
            definitions.Add($"PRIMARY KEY ({string.Join(", ", pk.Columns.Select(c => $"\"{c}\""))})");
        }

        sb.Append(string.Join(", ", definitions));
        sb.Append(");");

        return sb.ToString();
    }

    private static string BuildPostgresDataType(DataRow row)
    {
        var dataType = row["data_type"]?.ToString() ?? string.Empty;
        var udtName = row["udt_name"]?.ToString() ?? string.Empty;

        if (dataType == "USER-DEFINED" && !string.IsNullOrWhiteSpace(udtName))
        {
            return udtName;
        }

        if (dataType is "character varying" or "character")
        {
            var length = row["character_maximum_length"]?.ToString();

            if (!string.IsNullOrWhiteSpace(length))
            {
                return $"{dataType}({length})";
            }
        }

        if (dataType == "numeric")
        {
            var precision = row["numeric_precision"]?.ToString();
            var scale = row["numeric_scale"]?.ToString();

            if (!string.IsNullOrWhiteSpace(precision) &&
                !string.IsNullOrWhiteSpace(scale))
            {
                return $"numeric({precision},{scale})";
            }
        }

        if (dataType == "timestamp without time zone")
            return "timestamp without time zone";

        if (dataType == "timestamp with time zone")
            return "timestamp with time zone";

        return dataType;
    }

    private static List<string> ParsePostgresArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new List<string>();
        }

        return value
            .Trim('{', '}')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim('"'))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static string GetColumnPropertyValue(
        ColumnDefinition column,
        params string[] propertyNames)
    {
        var type = column.GetType();

        foreach (var propertyName in propertyNames)
        {
            var property = type.GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property == null)
            {
                continue;
            }

            var value = property.GetValue(column);

            return value?.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static void TrySetProperty<T>(
        object target,
        string propertyName,
        T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property == null || !property.CanWrite)
        {
            return;
        }

        if (value == null)
        {
            if (!property.PropertyType.IsValueType ||
                Nullable.GetUnderlyingType(property.PropertyType) != null)
            {
                property.SetValue(target, null);
            }

            return;
        }

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        try
        {
            if (targetType == typeof(string))
            {
                property.SetValue(target, value.ToString());
                return;
            }

            if (targetType == typeof(bool))
            {
                if (value is bool boolValue)
                {
                    property.SetValue(target, boolValue);
                    return;
                }

                if (bool.TryParse(value.ToString(), out var parsedBool))
                {
                    property.SetValue(target, parsedBool);
                }

                return;
            }

            if (targetType == typeof(int))
            {
                if (int.TryParse(value.ToString(), out var parsedInt))
                {
                    property.SetValue(target, parsedInt);
                }

                return;
            }

            if (targetType == typeof(List<string>) && value is List<string> stringList)
            {
                property.SetValue(target, stringList);
                return;
            }

            if (targetType == typeof(IList<string>) && value is IList<string> stringIList)
            {
                property.SetValue(target, stringIList);
                return;
            }

            if (targetType == typeof(string[]) && value is IEnumerable<string> stringEnumerable)
            {
                property.SetValue(target, stringEnumerable.ToArray());
                return;
            }

            property.SetValue(target, Convert.ChangeType(value, targetType));
        }
        catch
        {
            // Ignore optional model-property assignment failures.
        }
    }
}

// fk issue 
//public class SchemaFetcher
//{
//    private readonly DatabaseConnection _connection;
//    private readonly Action<int, int, string, bool>? _logger;
//    private readonly bool _verbose;
//    private readonly LogLevel _logLevel;

//    public SchemaFetcher(
//        DatabaseConnection connection,
//        Action<int, int, string, bool>? logger,
//        bool verbose,
//        LogLevel logLevel = LogLevel.Basic)
//    {
//        _connection = connection;
//        _logger = logger;
//        _verbose = verbose;
//        _logLevel = logLevel;
//    }

//    public async Task<DataTable> GetTablesAsync()
//    {
//        const string sql = """
//            SELECT
//                table_schema,
//                table_name,
//                table_schema || '.' || table_name AS full_name
//            FROM information_schema.tables
//            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
//              AND table_type = 'BASE TABLE'
//            ORDER BY table_schema, table_name;
//            """;

//        return await _connection.ExecuteQueryAsync(sql);
//    }

//    public async Task<TableDefinition> GetTableDefinitionAsync(string tableName)
//    {
//        var objectName = PostgresObjectName.Parse(tableName);

//        var tableDefinition = new TableDefinition
//        {
//            Name = objectName.FullName,
//            Columns = (await GetColumnsListAsync(objectName.FullName)).ToList(),
//            PrimaryKeys = (await GetPrimaryKeysListAsync(objectName.FullName)).ToList(),
//            ForeignKeys = (await GetForeignKeysListAsync(objectName.FullName)).ToList(),
//            Indexes = (await GetIndexesListAsync(objectName.FullName)).ToList(),
//            CreateScript = await GetCreateTableScriptAsync(objectName.FullName)
//        };

//        return tableDefinition;
//    }

//    public async Task<IList<ColumnDefinition>> GetColumnsListAsync(string tableName)
//    {
//        var objectName = PostgresObjectName.Parse(tableName);

//        const string sql = """
//            SELECT
//                column_name,
//                data_type,
//                udt_name,
//                character_maximum_length,
//                numeric_precision,
//                numeric_scale,
//                is_nullable,
//                column_default,
//                is_identity
//            FROM information_schema.columns
//            WHERE table_schema = @schemaName
//              AND table_name = @tableName
//            ORDER BY ordinal_position;
//            """;

//        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
//        {
//            ["schemaName"] = objectName.Schema,
//            ["tableName"] = objectName.Name
//        });

//        var columns = new List<ColumnDefinition>();

//        foreach (DataRow row in table.Rows)
//        {
//            var column = new ColumnDefinition
//            {
//                Name = row["column_name"]?.ToString() ?? string.Empty,
//                DataType = BuildPostgresDataType(row),
//                IsNullable = string.Equals(row["is_nullable"]?.ToString(), "YES", StringComparison.OrdinalIgnoreCase)
//            };

//            TrySetProperty(column, "DefaultValue", row["column_default"]?.ToString());
//            TrySetProperty(column, "Default", row["column_default"]?.ToString());
//            TrySetProperty(column, "ColumnDefault", row["column_default"]?.ToString());
//            TrySetProperty(column, "MaxLength", row["character_maximum_length"]?.ToString());
//            TrySetProperty(column, "Length", row["character_maximum_length"]?.ToString());
//            TrySetProperty(column, "CharacterMaximumLength", row["character_maximum_length"]?.ToString());
//            TrySetProperty(column, "IsIdentity", string.Equals(row["is_identity"]?.ToString(), "YES", StringComparison.OrdinalIgnoreCase));

//            columns.Add(column);
//        }

//        return columns;
//    }

//    public async Task<IList<PrimaryKeyDefinition>> GetPrimaryKeysListAsync(string tableName)
//    {
//        var objectName = PostgresObjectName.Parse(tableName);

//        const string sql = """
//            SELECT
//                tc.constraint_name,
//                kcu.column_name
//            FROM information_schema.table_constraints tc
//            JOIN information_schema.key_column_usage kcu
//              ON tc.constraint_name = kcu.constraint_name
//             AND tc.table_schema = kcu.table_schema
//             AND tc.table_name = kcu.table_name
//            WHERE tc.constraint_type = 'PRIMARY KEY'
//              AND tc.table_schema = @schemaName
//              AND tc.table_name = @tableName
//            ORDER BY kcu.ordinal_position;
//            """;

//        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
//        {
//            ["schemaName"] = objectName.Schema,
//            ["tableName"] = objectName.Name
//        });

//        var grouped = table.Rows.Cast<DataRow>()
//            .GroupBy(r => r["constraint_name"]?.ToString() ?? string.Empty)
//            .Where(g => !string.IsNullOrWhiteSpace(g.Key));

//        var result = new List<PrimaryKeyDefinition>();

//        foreach (var group in grouped)
//        {
//            result.Add(new PrimaryKeyDefinition
//            {
//                Name = group.Key,
//                Columns = group.Select(r => r["column_name"]?.ToString() ?? string.Empty)
//                    .Where(x => !string.IsNullOrWhiteSpace(x))
//                    .ToList()
//            });
//        }

//        return result;
//    }

//    public async Task<string?> GetPrimaryKeyCreateScriptAsync(string tableName)
//    {
//        var objectName = PostgresObjectName.Parse(tableName);

//        const string sql = """
//            SELECT
//                'ALTER TABLE "' || n.nspname || '"."' || c.relname || '" ADD CONSTRAINT "' ||
//                con.conname || '" ' || pg_get_constraintdef(con.oid, true) || ';' AS create_script
//            FROM pg_constraint con
//            JOIN pg_class c ON c.oid = con.conrelid
//            JOIN pg_namespace n ON n.oid = c.relnamespace
//            WHERE con.contype = 'p'
//              AND n.nspname = @schemaName
//              AND c.relname = @tableName
//            LIMIT 1;
//            """;

//        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
//        {
//            ["schemaName"] = objectName.Schema,
//            ["tableName"] = objectName.Name
//        });

//        return table.Rows.Count == 0
//            ? null
//            : table.Rows[0]["create_script"]?.ToString();
//    }

//    public async Task<IList<ForeignKeyDefinition>> GetForeignKeysListAsync(string tableName)
//    {
//        var objectName = PostgresObjectName.Parse(tableName);

//        const string sql = """
//            SELECT
//                con.conname AS constraint_name,
//                src_ns.nspname AS source_schema,
//                src_tbl.relname AS source_table,
//                src_col.attname AS source_column,
//                ref_ns.nspname AS referenced_schema,
//                ref_tbl.relname AS referenced_table,
//                ref_col.attname AS referenced_column,
//                pg_get_constraintdef(con.oid, true) AS definition
//            FROM pg_constraint con
//            JOIN pg_class src_tbl ON src_tbl.oid = con.conrelid
//            JOIN pg_namespace src_ns ON src_ns.oid = src_tbl.relnamespace
//            JOIN pg_class ref_tbl ON ref_tbl.oid = con.confrelid
//            JOIN pg_namespace ref_ns ON ref_ns.oid = ref_tbl.relnamespace
//            JOIN unnest(con.conkey) WITH ORDINALITY AS src_cols(attnum, ord) ON true
//            JOIN unnest(con.confkey) WITH ORDINALITY AS ref_cols(attnum, ord) ON src_cols.ord = ref_cols.ord
//            JOIN pg_attribute src_col ON src_col.attrelid = src_tbl.oid AND src_col.attnum = src_cols.attnum
//            JOIN pg_attribute ref_col ON ref_col.attrelid = ref_tbl.oid AND ref_col.attnum = ref_cols.attnum
//            WHERE con.contype = 'f'
//              AND src_ns.nspname = @schemaName
//              AND src_tbl.relname = @tableName
//            ORDER BY con.conname, src_cols.ord;
//            """;

//        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
//        {
//            ["schemaName"] = objectName.Schema,
//            ["tableName"] = objectName.Name
//        });

//        var result = new List<ForeignKeyDefinition>();

//        var groups = table.Rows.Cast<DataRow>()
//            .GroupBy(r => r["constraint_name"]?.ToString() ?? string.Empty)
//            .Where(g => !string.IsNullOrWhiteSpace(g.Key));

//        foreach (var group in groups)
//        {
//            var first = group.First();

//            var foreignKey = new ForeignKeyDefinition
//            {
//                Name = group.Key,
//                Columns = group.Select(r => r["source_column"]?.ToString() ?? string.Empty)
//                    .Where(x => !string.IsNullOrWhiteSpace(x))
//                    .ToList(),
//                ReferencedTable = $"{first["referenced_schema"]}.{first["referenced_table"]}",
//                ReferencedColumns = group.Select(r => r["referenced_column"]?.ToString() ?? string.Empty)
//                    .Where(x => !string.IsNullOrWhiteSpace(x))
//                    .ToList()
//            };

//            TrySetProperty(foreignKey, "Definition", first["definition"]?.ToString());

//            result.Add(foreignKey);
//        }

//        return result;
//    }

//    public async Task<string?> GetForeignKeyCreateScriptAsync(string tableName, string foreignKeyName)
//    {
//        var objectName = PostgresObjectName.Parse(tableName);

//        const string sql = """
//            SELECT
//                'ALTER TABLE "' || src_ns.nspname || '"."' || src_tbl.relname || '" ADD CONSTRAINT "' ||
//                con.conname || '" ' || pg_get_constraintdef(con.oid, true) || ';' AS create_script
//            FROM pg_constraint con
//            JOIN pg_class src_tbl ON src_tbl.oid = con.conrelid
//            JOIN pg_namespace src_ns ON src_ns.oid = src_tbl.relnamespace
//            WHERE con.contype = 'f'
//              AND src_ns.nspname = @schemaName
//              AND src_tbl.relname = @tableName
//              AND con.conname = @foreignKeyName
//            LIMIT 1;
//            """;

//        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
//        {
//            ["schemaName"] = objectName.Schema,
//            ["tableName"] = objectName.Name,
//            ["foreignKeyName"] = foreignKeyName
//        });

//        return table.Rows.Count == 0
//            ? null
//            : table.Rows[0]["create_script"]?.ToString();
//    }

//    public async Task<IList<IndexDefinition>> GetIndexesListAsync(string tableName)
//    {
//        var objectName = PostgresObjectName.Parse(tableName);

//        const string sql = """
//            SELECT
//                ns.nspname AS schema_name,
//                tbl.relname AS table_name,
//                idx.relname AS index_name,
//                am.amname AS index_type,
//                ix.indisunique AS is_unique,
//                ix.indisprimary AS is_primary,
//                pg_get_indexdef(ix.indexrelid) AS definition,
//                array_remove(array_agg(att.attname ORDER BY arr.ordinality), NULL) AS columns
//            FROM pg_index ix
//            JOIN pg_class tbl ON tbl.oid = ix.indrelid
//            JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
//            JOIN pg_class idx ON idx.oid = ix.indexrelid
//            JOIN pg_am am ON am.oid = idx.relam
//            LEFT JOIN unnest(ix.indkey) WITH ORDINALITY AS arr(attnum, ordinality) ON true
//            LEFT JOIN pg_attribute att ON att.attrelid = tbl.oid AND att.attnum = arr.attnum
//            WHERE ns.nspname = @schemaName
//              AND tbl.relname = @tableName
//              AND ix.indisprimary = false
//            GROUP BY ns.nspname, tbl.relname, idx.relname, am.amname, ix.indisunique, ix.indisprimary, ix.indexrelid
//            ORDER BY idx.relname;
//            """;

//        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
//        {
//            ["schemaName"] = objectName.Schema,
//            ["tableName"] = objectName.Name
//        });

//        var result = new List<IndexDefinition>();

//        foreach (DataRow row in table.Rows)
//        {
//            var index = new IndexDefinition
//            {
//                Name = row["index_name"]?.ToString() ?? string.Empty,
//                Columns = ParsePostgresArray(row["columns"]?.ToString()),
//                IsUnique = row["is_unique"] != DBNull.Value && Convert.ToBoolean(row["is_unique"])
//            };

//            TrySetProperty(index, "Type", row["index_type"]?.ToString() ?? "btree");
//            TrySetProperty(index, "IndexType", row["index_type"]?.ToString() ?? "btree");
//            TrySetProperty(index, "Definition", row["definition"]?.ToString());

//            result.Add(index);
//        }

//        return result;
//    }

//    public async Task<string?> GetIndexCreateScriptAsync(string indexName)
//    {
//        const string sql = """
//            SELECT pg_get_indexdef(idx.oid) AS create_script
//            FROM pg_class idx
//            JOIN pg_namespace ns ON ns.oid = idx.relnamespace
//            WHERE idx.relkind = 'i'
//              AND idx.relname = @indexName
//            ORDER BY ns.nspname
//            LIMIT 1;
//            """;

//        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
//        {
//            ["indexName"] = indexName
//        });

//        return table.Rows.Count == 0
//            ? null
//            : table.Rows[0]["create_script"]?.ToString();
//    }

//    public async Task<DataTable> GetFunctionsAsync()
//    {
//        const string sql = """
//            SELECT
//                n.nspname AS routine_schema,
//                p.oid,
//                p.proname AS routine_name,
//                pg_get_function_identity_arguments(p.oid) AS arguments,
//                pg_get_functiondef(p.oid) AS definition
//            FROM pg_proc p
//            JOIN pg_namespace n ON p.pronamespace = n.oid
//            LEFT JOIN pg_depend d
//                ON d.objid = p.oid
//               AND d.deptype = 'e'
//            LEFT JOIN pg_extension e
//                ON e.oid = d.refobjid
//            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
//              AND p.prokind = 'f'
//              AND e.oid IS NULL
//            ORDER BY n.nspname, p.proname;
//            """;

//        return await _connection.ExecuteQueryAsync(sql);
//    }

//    public async Task<DataTable> GetProceduresAsync()
//    {
//        const string sql = """
//            SELECT
//                n.nspname AS routine_schema,
//                p.oid,
//                p.proname AS routine_name,
//                pg_get_function_identity_arguments(p.oid) AS arguments,
//                pg_get_functiondef(p.oid) AS definition
//            FROM pg_proc p
//            JOIN pg_namespace n ON p.pronamespace = n.oid
//            LEFT JOIN pg_depend d
//                ON d.objid = p.oid
//               AND d.deptype = 'e'
//            LEFT JOIN pg_extension e
//                ON e.oid = d.refobjid
//            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
//              AND p.prokind = 'p'
//              AND e.oid IS NULL
//            ORDER BY n.nspname, p.proname;
//            """;

//        return await _connection.ExecuteQueryAsync(sql);
//    }

//    public async Task<string> GetFunctionDefinitionAsync(string functionName)
//    {
//        var objectName = PostgresObjectName.ParseRoutine(functionName);

//        const string sql = """
//            SELECT pg_get_functiondef(p.oid) AS definition
//            FROM pg_proc p
//            JOIN pg_namespace n ON p.pronamespace = n.oid
//            WHERE n.nspname = @schemaName
//              AND p.proname = @routineName
//              AND p.prokind = 'f'
//            ORDER BY p.oid
//            LIMIT 1;
//            """;

//        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
//        {
//            ["schemaName"] = objectName.Schema,
//            ["routineName"] = objectName.Name
//        });

//        return table.Rows.Count == 0
//            ? string.Empty
//            : table.Rows[0]["definition"]?.ToString() ?? string.Empty;
//    }

//    public async Task<string> GetProcedureDefinitionAsync(string procedureName)
//    {
//        var objectName = PostgresObjectName.ParseRoutine(procedureName);

//        const string sql = """
//            SELECT pg_get_functiondef(p.oid) AS definition
//            FROM pg_proc p
//            JOIN pg_namespace n ON p.pronamespace = n.oid
//            WHERE n.nspname = @schemaName
//              AND p.proname = @routineName
//              AND p.prokind = 'p'
//            ORDER BY p.oid
//            LIMIT 1;
//            """;

//        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
//        {
//            ["schemaName"] = objectName.Schema,
//            ["routineName"] = objectName.Name
//        });

//        return table.Rows.Count == 0
//            ? string.Empty
//            : table.Rows[0]["definition"]?.ToString() ?? string.Empty;
//    }

//    public async Task<DataTable> GetViewsAsync()
//    {
//        const string sql = """
//            SELECT
//                table_schema,
//                table_name AS view_name,
//                view_definition
//            FROM information_schema.views
//            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
//            ORDER BY table_schema, table_name;
//            """;

//        return await _connection.ExecuteQueryAsync(sql);
//    }

//    public async Task<string?> GetViewDefinitionAsync(string viewName)
//    {
//        var objectName = PostgresObjectName.Parse(viewName);

//        const string sql = """
//            SELECT view_definition
//            FROM information_schema.views
//            WHERE table_schema = @schemaName
//              AND table_name = @viewName
//            LIMIT 1;
//            """;

//        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
//        {
//            ["schemaName"] = objectName.Schema,
//            ["viewName"] = objectName.Name
//        });

//        return table.Rows.Count == 0
//            ? null
//            : table.Rows[0]["view_definition"]?.ToString();
//    }

//    public async Task<DataTable> GetTriggersAsync()
//    {
//        const string sql = """
//            SELECT 
//                ns.nspname AS table_schema,
//                trg.tgname AS trigger_name,
//                tbl.relname AS table_name,
//                pg_get_triggerdef(trg.oid, true) AS definition
//            FROM pg_trigger trg
//            JOIN pg_class tbl ON tbl.oid = trg.tgrelid
//            JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
//            WHERE ns.nspname NOT IN ('pg_catalog', 'information_schema')
//              AND NOT trg.tgisinternal
//            ORDER BY ns.nspname, tbl.relname, trg.tgname;
//            """;

//        return await _connection.ExecuteQueryAsync(sql);
//    }

//    public async Task<string?> GetTriggerDefinitionAsync(string triggerName)
//    {
//        const string sql = """
//            SELECT pg_get_triggerdef(trg.oid, true) AS definition
//            FROM pg_trigger trg
//            JOIN pg_class tbl ON tbl.oid = trg.tgrelid
//            JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
//            WHERE trg.tgname = @triggerName
//              AND NOT trg.tgisinternal
//            ORDER BY ns.nspname, tbl.relname
//            LIMIT 1;
//            """;

//        var table = await _connection.ExecuteQueryAsync(sql, new Dictionary<string, object>
//        {
//            ["triggerName"] = triggerName
//        });

//        return table.Rows.Count == 0
//            ? null
//            : table.Rows[0]["definition"]?.ToString();
//    }

//    public async Task<string> GetCreateTableScriptAsync(string tableName)
//    {
//        var objectName = PostgresObjectName.Parse(tableName);

//        var columns = await GetColumnsListAsync(objectName.FullName);
//        var primaryKeys = await GetPrimaryKeysListAsync(objectName.FullName);

//        var sb = new StringBuilder();

//        sb.Append($"CREATE TABLE \"{objectName.Schema}\".\"{objectName.Name}\" (");

//        var definitions = new List<string>();

//        foreach (var column in columns)
//        {
//            var columnDefinition = new StringBuilder();

//            columnDefinition.Append($"\"{column.Name}\" {column.DataType}");

//            var defaultValue = GetColumnPropertyValue(column, "DefaultValue", "Default", "ColumnDefault");

//            if (!string.IsNullOrWhiteSpace(defaultValue))
//            {
//                columnDefinition.Append($" DEFAULT {defaultValue}");
//            }

//            if (!column.IsNullable)
//            {
//                columnDefinition.Append(" NOT NULL");
//            }

//            definitions.Add(columnDefinition.ToString());
//        }

//        var pk = primaryKeys.FirstOrDefault();

//        if (pk != null && pk.Columns.Any())
//        {
//            definitions.Add($"PRIMARY KEY ({string.Join(", ", pk.Columns.Select(c => $"\"{c}\""))})");
//        }

//        sb.Append(string.Join(", ", definitions));
//        sb.Append(");");

//        return sb.ToString();
//    }

//    private static string BuildPostgresDataType(DataRow row)
//    {
//        var dataType = row["data_type"]?.ToString() ?? string.Empty;
//        var udtName = row["udt_name"]?.ToString() ?? string.Empty;

//        if (dataType == "USER-DEFINED" && !string.IsNullOrWhiteSpace(udtName))
//        {
//            return udtName;
//        }

//        if (dataType is "character varying" or "character")
//        {
//            var length = row["character_maximum_length"]?.ToString();

//            if (!string.IsNullOrWhiteSpace(length))
//                return $"{dataType}({length})";
//        }

//        if (dataType == "numeric")
//        {
//            var precision = row["numeric_precision"]?.ToString();
//            var scale = row["numeric_scale"]?.ToString();

//            if (!string.IsNullOrWhiteSpace(precision) &&
//                !string.IsNullOrWhiteSpace(scale))
//            {
//                return $"numeric({precision},{scale})";
//            }
//        }

//        if (dataType == "timestamp without time zone")
//            return "timestamp without time zone";

//        if (dataType == "timestamp with time zone")
//            return "timestamp with time zone";

//        return dataType;
//    }

//    private static List<string> ParsePostgresArray(string? value)
//    {
//        if (string.IsNullOrWhiteSpace(value))
//            return new List<string>();

//        return value
//            .Trim('{', '}')
//            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
//            .Select(x => x.Trim('"'))
//            .Where(x => !string.IsNullOrWhiteSpace(x))
//            .ToList();
//    }

//    private static string GetColumnPropertyValue(
//        ColumnDefinition column,
//        params string[] propertyNames)
//    {
//        var type = column.GetType();

//        foreach (var propertyName in propertyNames)
//        {
//            var property = type.GetProperty(
//                propertyName,
//                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

//            if (property == null)
//                continue;

//            var value = property.GetValue(column);

//            return value?.ToString() ?? string.Empty;
//        }

//        return string.Empty;
//    }

//    private static void TrySetProperty<T>(
//        object target,
//        string propertyName,
//        T value)
//    {
//        var property = target.GetType().GetProperty(
//            propertyName,
//            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

//        if (property == null || !property.CanWrite)
//            return;

//        if (value == null)
//        {
//            if (!property.PropertyType.IsValueType ||
//                Nullable.GetUnderlyingType(property.PropertyType) != null)
//            {
//                property.SetValue(target, null);
//            }

//            return;
//        }

//        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

//        try
//        {
//            if (targetType == typeof(string))
//            {
//                property.SetValue(target, value.ToString());
//                return;
//            }

//            if (targetType == typeof(bool))
//            {
//                if (value is bool boolValue)
//                {
//                    property.SetValue(target, boolValue);
//                    return;
//                }

//                if (bool.TryParse(value.ToString(), out var parsedBool))
//                {
//                    property.SetValue(target, parsedBool);
//                }

//                return;
//            }

//            if (targetType == typeof(int))
//            {
//                if (int.TryParse(value.ToString(), out var parsedInt))
//                {
//                    property.SetValue(target, parsedInt);
//                }

//                return;
//            }

//            property.SetValue(target, Convert.ChangeType(value, targetType));
//        }
//        catch
//        {
//            // Ignore optional model-property assignment failures.
//        }
//    }
//}


//public class SchemaFetcher
//{
//    private readonly DatabaseConnection _connection;
//    private readonly Action<int, int, string, bool> _logger;
//    private readonly LogLevel _logLevel;
//    private bool _pgGetTableDefEnsured;

//    public SchemaFetcher(DatabaseConnection connection, Action<int, int, string, bool> logger, object verbose, LogLevel logLevel = LogLevel.Basic)
//    {
//        _connection = connection;
//        _logger = logger;
//        _logLevel = logLevel;
//    }

//    #region Get All Schema Objects Tables, Functions, Procedures, Sequences
//    public async Task<DataTable> GetTablesAsync()
//    {
//        string query = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'";
//        return await ExecuteQueryAsync(query);
//    }

//    public async Task<DataTable> GetFunctionsAsync()
//    {
//        const string sql = """
//        SELECT
//            p.oid,
//            p.proname AS routine_name,
//            pg_get_function_identity_arguments(p.oid) AS arguments,
//            pg_get_functiondef(p.oid) AS definition
//        FROM pg_proc p
//        JOIN pg_namespace n ON p.pronamespace = n.oid
//        LEFT JOIN pg_depend d
//            ON d.objid = p.oid
//           AND d.deptype = 'e'
//        LEFT JOIN pg_extension e
//            ON e.oid = d.refobjid
//        WHERE n.nspname = 'public'
//          AND p.prokind = 'f'
//          AND e.oid IS NULL
//        ORDER BY p.proname;
//        """;

//        return await _connection.ExecuteQueryAsync(sql);
//    }
//    //public async Task<DataTable> GetFunctionsAsync()
//    //{
//    //    var query = @"
//    //                    SELECT 
//    //                        p.oid,
//    //                        p.proname AS routine_name,
//    //                        pg_get_function_identity_arguments(p.oid) AS arguments, 
//    //                        pg_get_functiondef(p.oid) AS definition
//    //                    FROM pg_proc p 
//    //                    JOIN pg_namespace n ON p.pronamespace = n.oid 
//    //                    WHERE n.nspname = 'public' 
//    //                      AND p.prokind = 'f';";
//    //    return await ExecuteQueryAsync(query);
//    //}

//    public async Task<DataTable> GetProceduresAsync()
//    {
//        const string sql = """
//        SELECT
//            p.oid,
//            p.proname AS routine_name,
//            pg_get_function_identity_arguments(p.oid) AS arguments,
//            pg_get_functiondef(p.oid) AS definition
//        FROM pg_proc p
//        JOIN pg_namespace n ON p.pronamespace = n.oid
//        LEFT JOIN pg_depend d
//            ON d.objid = p.oid
//           AND d.deptype = 'e'
//        LEFT JOIN pg_extension e
//            ON e.oid = d.refobjid
//        WHERE n.nspname = 'public'
//          AND p.prokind = 'p'
//          AND e.oid IS NULL
//        ORDER BY p.proname;
//        """;

//        return await _connection.ExecuteQueryAsync(sql);
//    }
//    //public async Task<DataTable> GetProceduresAsync()
//    //{
//    //    var query = @"
//    //                    SELECT 
//    //                        p.oid,
//    //                        p.proname AS routine_name,
//    //                        pg_get_function_identity_arguments(p.oid) AS arguments,
//    //                       pg_get_functiondef(p.oid) AS definition
//    //                    FROM pg_proc p 
//    //                    JOIN pg_namespace n ON p.pronamespace = n.oid 
//    //                    WHERE n.nspname = 'public' 
//    //                      AND p.prokind = 'p';"; // 'p' indicates procedures
//    //    return await ExecuteQueryAsync(query);
//    //}

//    public async Task<DataTable> GetSequencesAsync()
//    {
//        string query = @"
//                        SELECT sequence_name 
//                        FROM information_schema.sequences 
//                        WHERE sequence_schema = 'public';";
//        return await ExecuteQueryAsync(query);
//    }

//    public async Task<DataTable> GetViewsAsync()
//    {
//        var query = @"
//        SELECT 
//            table_name,
//            view_definition
//        FROM information_schema.views
//        WHERE table_schema = 'public';";
//        return await ExecuteQueryAsync(query);
//    }


//    public async Task<DataTable> GetTriggersAsync()
//    {
//        var query = @"
//        SELECT 
//            trg.tgname AS trigger_name,
//            tbl.relname AS table_name,
//            pg_get_triggerdef(trg.oid, true) AS definition
//        FROM pg_trigger trg
//        JOIN pg_class tbl ON tbl.oid = trg.tgrelid
//        JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
//        WHERE ns.nspname = 'public'
//          AND NOT trg.tgisinternal;";
//        return await ExecuteQueryAsync(query);
//    }



//    #endregion

//    #region Get Details of a specific table
//    public async Task<TableDefinition> GetTableDefinitionAsync(string tableName)
//    {
//        if (!_pgGetTableDefEnsured)
//        {
//            await EnsurePgGetTableDefFunctionExistsAsync();
//            _pgGetTableDefEnsured = true;
//        }

//        var columnsTask = GetColumnsListAsync(tableName);
//        var pkTask = GetPrimaryKeysListAsync(tableName);
//        var fkTask = GetForeignKeysListAsync(tableName);
//        var indexTask = GetIndexesListAsync(tableName);
//        var uniqueTask = GetUniqueConstraintsListAsync(tableName);
//        var scriptTask = GetCreateTableScriptAsync(tableName);

//        await Task.WhenAll(columnsTask, pkTask, fkTask, indexTask, uniqueTask, scriptTask);

//        return new TableDefinition
//        {
//            Name = tableName,
//            Columns = columnsTask.Result,
//            PrimaryKeys = pkTask.Result,
//            ForeignKeys = fkTask.Result,
//            Indexes = indexTask.Result,
//            UniqueConstraints = uniqueTask.Result,
//            CreateScript = scriptTask.Result
//        };
//    }

//    public async Task<DataTable> GetColumnsAsync(string tableName)
//    {
//        const string query = @"
//        SELECT
//            column_name,
//            data_type,
//            character_maximum_length,
//            is_nullable,
//            column_default
//        FROM
//            information_schema.columns
//        WHERE
//            table_name = @tableName
//            AND table_schema = 'public'
//        ORDER BY ordinal_position";

//        var parameters = new Dictionary<string, object> { { "@tableName", tableName } };
//        var result = await ExecuteQueryAsync(query, parameters);

//        if (result.Columns.Contains("column_name"))
//        {
//            result.PrimaryKey = new DataColumn[] { result.Columns["column_name"] };
//        }

//        return result;
//    }

//    public async Task<List<ColumnDefinition>> GetColumnsListAsync(string tableName)
//    {
//        var columnList = new List<ColumnDefinition>();
//        var columnDataTable = await GetColumnsAsync(tableName);

//        foreach (DataRow row in columnDataTable.Rows)
//        {
//            columnList.Add(new ColumnDefinition
//            {
//                Name = row["column_name"].ToString() ?? string.Empty,
//                DataType = row["data_type"].ToString() ?? string.Empty,
//                IsNullable = row["is_nullable"].ToString() == "YES",
//                Length = row["character_maximum_length"] != DBNull.Value
//                         ? Convert.ToInt32(row["character_maximum_length"])
//                         : null,
//                DefaultValue = row.Table.Columns.Contains("column_default") && row["column_default"] != DBNull.Value
//                               ? row["column_default"].ToString()
//                               : null
//            });
//        }

//        return columnList;
//    }

//    public async Task<List<PrimaryKeyDefinition>> GetPrimaryKeysListAsync(string tableName)
//    {
//        var dataTable = await GetPrimaryKeysAsync(tableName);

//        // Group by constraint_name to build one PK with all its columns in ordinal order
//        var primaryKeys = dataTable.AsEnumerable()
//            .GroupBy(row => row["constraint_name"].ToString() ?? string.Empty)
//            .Select(g => new PrimaryKeyDefinition
//            {
//                Name = g.Key,
//                Columns = g
//                    .Select(r => r["column_name"].ToString() ?? string.Empty)
//                    .Where(c => !string.IsNullOrEmpty(c))
//                    .ToList()
//            })
//            .ToList();

//        return primaryKeys;
//    }

//    public async Task<List<ForeignKeyDefinition>> GetForeignKeysListAsync(string tableName)
//    {
//        var foreignKeyList = new List<ForeignKeyDefinition>();
//        var foreignKeyDataTable = await GetForeignKeysAsync(tableName);

//        foreach (DataRow row in foreignKeyDataTable.Rows)
//        {
//            var newFk = new ForeignKeyDefinition
//            {
//                Name = row["foreign_key_name"].ToString(),
//                ColumnName = row["column_name"].ToString(),
//                ReferencedTable = row["foreign_table_name"].ToString(),
//                ReferencedColumn = row["foreign_column_name"].ToString()
//            };

//            if (!foreignKeyList.Any(fk =>
//                fk.ColumnName == newFk.ColumnName &&
//                fk.ReferencedTable == newFk.ReferencedTable &&
//                fk.ReferencedColumn == newFk.ReferencedColumn))
//            {
//                foreignKeyList.Add(newFk);
//            }
//        }

//        return foreignKeyList;
//    }

//    public async Task<string?> GetCreateTableScriptAsync(string tableName)
//    {
//        const string query = "SELECT pg_get_tabledef(@tableName)";
//        var parameters = new Dictionary<string, object> { { "@tableName", tableName } };
//        var result = await _connection.ExecuteQueryAsync(query, parameters);
//        return result.Rows.Count > 0 ? result.Rows[0][0].ToString() : null;
//    }

//    public async Task<List<IndexDefinition>> GetIndexesListAsync(string tableName)
//    {
//        var indexDataTable = await GetIndexesAsync(tableName);

//        var grouped = indexDataTable.AsEnumerable()
//            .GroupBy(row => row["indexname"].ToString())
//            .Select(group =>
//            {
//                var firstRow = group.First();
//                return new IndexDefinition
//                {
//                    Name = group.Key ?? string.Empty,
//                    Columns = group.Select(r => r["columnname"].ToString() ?? string.Empty).ToList(),
//                    IsUnique = firstRow["is_unique"] != DBNull.Value && (bool)firstRow["is_unique"],
//                    IndexType = firstRow["index_type"]?.ToString() ?? string.Empty,
//                    //CreateScript = firstRow["create_script"]?.ToString() ?? string.Empty
//                };
//            });

//        return grouped.ToList();
//    }

//    public async Task<DataTable> GetIndexesAsync(string tableName)
//    {
//        const string query = @"
//            SELECT
//                i.relname AS indexname,
//                t.relname AS tablename,
//                a.attname AS columnname,
//                ix.indisunique AS is_unique,
//                am.amname AS index_type
//            FROM
//                pg_class t
//                JOIN pg_index ix ON t.oid = ix.indrelid
//                JOIN pg_class i ON i.oid = ix.indexrelid
//                JOIN pg_am am ON i.relam = am.oid
//                JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
//                JOIN pg_namespace ns ON ns.oid = t.relnamespace
//            WHERE
//                t.relname = @tableName
//                AND ns.nspname = 'public'
//                AND a.attnum > 0
//            ORDER BY
//                i.relname, a.attnum;";

//        var parameters = new Dictionary<string, object> { { "@tableName", tableName } };
//        return await ExecuteQueryAsync(query, parameters);
//    }

//    public async Task<string?> GetIndexCreateScriptAsync(string indexName)
//    {
//        const string sql = @"
//        SELECT pg_get_indexdef(indexrelid) AS create_script
//        FROM pg_index idx
//        JOIN pg_class cls ON cls.oid = idx.indexrelid
//        WHERE cls.relname = @indexName
//        LIMIT 1;";

//        var parameters = new Dictionary<string, object>
//    {
//        { "@indexName", indexName }
//    };

//        var result = await ExecuteQueryAsync(sql, parameters);

//        return result.Rows.Count > 0
//            ? result.Rows[0]["create_script"]?.ToString()
//            : null;
//    }

//    public async Task<DataTable> GetPrimaryKeysAsync(string tableName)
//    {
//        string query = @"
//                    SELECT 
//                        tc.constraint_name,
//                        kcu.column_name
//                    FROM 
//                        information_schema.table_constraints tc
//                    JOIN 
//                        information_schema.key_column_usage kcu 
//                        ON tc.constraint_name = kcu.constraint_name
//                        AND tc.table_schema = kcu.table_schema
//                        AND tc.table_name = kcu.table_name
//                    WHERE 
//                        tc.table_name = @tableName
//                        AND tc.constraint_type = 'PRIMARY KEY'
//                        AND tc.table_schema = 'public'
//                    ORDER BY 
//                        kcu.ordinal_position;";

//        var parameters = new Dictionary<string, object>
//                {
//                    { "@tableName", tableName }
//                };

//        return await ExecuteQueryAsync(query, parameters);
//    }

//    public async Task<string?> GetPrimaryKeyCreateScriptAsync(string tableName)
//    {
//        const string sql = @"
//        SELECT
//            'ALTER TABLE ""' || rel.relname || '"" ADD CONSTRAINT ""' || con.conname || '"" ' || 
//            pg_get_constraintdef(con.oid, true) || ';' AS create_script
//        FROM pg_constraint con
//        JOIN pg_class rel ON rel.oid = con.conrelid
//        WHERE con.contype = 'p'
//          AND rel.relname = @tableName
//          AND rel.relnamespace = 'public'::regnamespace
//        LIMIT 1;";

//        var parameters = new Dictionary<string, object>
//    {
//        { "@tableName", tableName }
//    };

//        var result = await ExecuteQueryAsync(sql, parameters);
//        return result.Rows.Count > 0
//            ? result.Rows[0]["create_script"]?.ToString()
//            : null;
//    }

//    public async Task<DataTable> GetForeignKeysAsync(string tableName)
//    {
//        const string query = @"
//            SELECT
//                tc.constraint_name AS foreign_key_name,
//                kcu.column_name,
//                ccu.table_name AS foreign_table_name,
//                ccu.column_name AS foreign_column_name
//            FROM information_schema.table_constraints AS tc
//            JOIN information_schema.key_column_usage AS kcu
//                ON tc.constraint_name = kcu.constraint_name
//                AND tc.table_schema = kcu.table_schema
//                AND tc.table_name = kcu.table_name
//            JOIN information_schema.constraint_column_usage AS ccu
//                ON ccu.constraint_name = tc.constraint_name
//                AND ccu.table_schema = tc.table_schema
//            WHERE tc.constraint_type = 'FOREIGN KEY'
//              AND tc.table_name = @tableName
//              AND tc.table_schema = 'public'";

//        var parameters = new Dictionary<string, object> { { "@tableName", tableName } };

//        return await ExecuteQueryAsync(query, parameters);
//    }

//    public async Task<string?> GetForeignKeyCreateScriptAsync(string tableName, string foreignKeyName)
//    {
//        const string sql = @"
//    SELECT
//        'ALTER TABLE ""' || rel.relname || '"" ADD CONSTRAINT ""' || con.conname || '"" ' ||
//        pg_get_constraintdef(con.oid, true) || ';' AS create_script
//    FROM pg_constraint con
//    JOIN pg_class rel ON rel.oid = con.conrelid
//    WHERE con.contype = 'f'  -- 'f' means foreign key
//      AND rel.relname = @tableName
//      AND con.conname = @foreignKeyName
//      AND rel.relnamespace = 'public'::regnamespace
//    LIMIT 1;";

//        var parameters = new Dictionary<string, object>
//    {
//        { "@tableName", tableName },
//        { "@foreignKeyName", foreignKeyName }
//    };

//        var result = await ExecuteQueryAsync(sql, parameters);
//        return result.Rows.Count > 0
//            ? result.Rows[0]["create_script"]?.ToString()
//            : null;
//    }


//    public async Task<List<UniqueConstraintDefinition>> GetUniqueConstraintsListAsync(string tableName)
//    {
//        const string sql = @"
//            SELECT
//                con.conname AS constraint_name,
//                att.attname AS column_name
//            FROM pg_constraint con
//            JOIN pg_class rel ON rel.oid = con.conrelid
//            JOIN pg_namespace ns ON ns.oid = rel.relnamespace
//            JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = ANY(con.conkey)
//            WHERE con.contype = 'u'
//              AND ns.nspname = 'public'
//              AND rel.relname = @tableName
//            ORDER BY con.conname, att.attnum;";

//        var parameters = new Dictionary<string, object> { { "@tableName", tableName } };
//        var dataTable = await ExecuteQueryAsync(sql, parameters);

//        return dataTable.AsEnumerable()
//            .GroupBy(row => row["constraint_name"].ToString() ?? string.Empty)
//            .Select(g => new UniqueConstraintDefinition
//            {
//                Name = g.Key,
//                Columns = g
//                    .Select(r => r["column_name"].ToString() ?? string.Empty)
//                    .Where(c => !string.IsNullOrEmpty(c))
//                    .ToList()
//            })
//            .ToList();
//    }

//    public async Task<string?> GetUniqueConstraintCreateScriptAsync(string tableName, string constraintName)
//    {
//        const string sql = @"
//            SELECT 'ALTER TABLE ""' || rel.relname || '"" ADD CONSTRAINT ""' || con.conname || '"" ' ||
//                   pg_get_constraintdef(con.oid, true) || ';' AS create_script
//            FROM pg_constraint con
//            JOIN pg_class rel ON rel.oid = con.conrelid
//            WHERE con.contype = 'u'
//              AND rel.relname = @tableName
//              AND con.conname = @constraintName
//              AND rel.relnamespace = 'public'::regnamespace
//            LIMIT 1;";

//        var parameters = new Dictionary<string, object>
//        {
//            { "@tableName", tableName },
//            { "@constraintName", constraintName }
//        };

//        var result = await ExecuteQueryAsync(sql, parameters);
//        return result.Rows.Count > 0 ? result.Rows[0]["create_script"]?.ToString() : null;
//    }

//    public async Task<IList<string>> GetSequenceNamesAsync()
//    {
//        const string sql = @"
//            SELECT sequence_name
//            FROM information_schema.sequences
//            WHERE sequence_schema = 'public'
//            ORDER BY sequence_name;";

//        var dataTable = await ExecuteQueryAsync(sql);
//        return dataTable.AsEnumerable()
//            .Select(row => row["sequence_name"].ToString() ?? string.Empty)
//            .Where(n => !string.IsNullOrEmpty(n))
//            .ToList();
//    }

//    #endregion

//    public async Task<string?> GetFunctionDefinitionAsync(string functionName, string? arguments = null)
//    {
//        string query;
//        Dictionary<string, object> parameters;

//        if (!string.IsNullOrWhiteSpace(arguments))
//        {
//            query = @"
//                SELECT pg_get_functiondef(p.oid) AS definition
//                FROM pg_proc p
//                JOIN pg_namespace n ON p.pronamespace = n.oid
//                WHERE p.proname = @name
//                  AND n.nspname = 'public'
//                  AND pg_get_function_identity_arguments(p.oid) = @arguments
//                LIMIT 1;";
//            parameters = new Dictionary<string, object> { { "name", functionName }, { "arguments", arguments } };
//        }
//        else
//        {
//            query = @"
//                SELECT pg_get_functiondef(p.oid) AS definition
//                FROM pg_proc p
//                JOIN pg_namespace n ON p.pronamespace = n.oid
//                WHERE p.proname = @name AND n.nspname = 'public'
//                LIMIT 1;";
//            parameters = new Dictionary<string, object> { { "name", functionName } };
//        }

//        var result = await ExecuteQueryAsync(query, parameters);
//        return result.Rows.Count > 0 ? result.Rows[0]["definition"].ToString() : null;
//    }

//    public async Task<string?> GetProcedureDefinitionAsync(string procedureName, string? arguments = null)
//    {
//        return await GetFunctionDefinitionAsync(procedureName, arguments);
//    }

//    #region Get Indexe and Sequence Definitions
//    public async Task<string> GetIndexDefinitionAsync(string indexName)
//    {
//        var query = @"
//                        SELECT indexdef 
//                        FROM pg_indexes 
//                        WHERE indexname = @indexName AND schemaname = 'public';";

//        var parameters = new Dictionary<string, object>
//            {
//                { "@indexName", indexName }
//            };

//        var result = await ExecuteQueryAsync(query, parameters);
//        return result.Rows.Count > 0 ? result.Rows[0]["indexdef"].ToString() : null;
//    }

//    public async Task<string> GetSequenceDefinitionAsync(string sequenceName)
//    {
//        var query = @"
//                        SELECT 'CREATE SEQUENCE ' || quote_ident(sequencename) ||
//                               ' START WITH ' || start_value ||
//                               ' INCREMENT BY ' || increment_by ||
//                               ' MINVALUE ' || min_value ||
//                               ' MAXVALUE ' || max_value ||
//                               ' CACHE ' || cache_size ||
//                               CASE WHEN cycle THEN ' CYCLE' ELSE '' END AS definition
//                        FROM pg_sequences
//                        WHERE schemaname = 'public' AND sequencename = @name;
//                    ";

//        var parameters = new Dictionary<string, object>
//                {
//                    { "@name", sequenceName }
//                };

//        var result = await ExecuteQueryAsync(query, parameters);
//        return result.Rows.Count > 0 ? result.Rows[0]["definition"].ToString() : null;
//    }

//    #endregion

//    public async Task EnsurePgGetTableDefFunctionExistsAsync()
//    {
//        string checkFunctionQuery = @"
//                            SELECT EXISTS (
//                                SELECT 1 
//                                FROM pg_proc 
//                                JOIN pg_namespace n ON n.oid = pg_proc.pronamespace
//                                WHERE proname = 'pg_get_tabledef' AND n.nspname = 'public'
//                            );";

//        var result = await _connection.ExecuteQueryAsync(checkFunctionQuery);
//        bool exists = result.Rows.Count > 0 && (bool)result.Rows[0][0];

//        if (!exists)
//        {
//            string createFunctionSql = @"
//                            CREATE OR REPLACE FUNCTION pg_get_tabledef(p_table_name TEXT)
//                            RETURNS TEXT LANGUAGE plpgsql AS $$
//                            DECLARE
//                                col RECORD;
//                                col_defs TEXT := '';
//                                pk_cols TEXT := '';
//                                result TEXT;
//                            BEGIN
//                                FOR col IN
//                                    SELECT 
//                                        column_name,
//                                        data_type,
//                                        character_maximum_length,
//                                        numeric_precision,
//                                        numeric_scale,
//                                        is_nullable,
//                                        column_default
//                                    FROM information_schema.columns
//                                    WHERE table_schema = 'public' AND table_name = p_table_name
//                                    ORDER BY ordinal_position
//                                LOOP
//                                    col_defs := col_defs || 
//                                        format('""%s"" %s%s%s%s, ',
//                                            col.column_name,
//                                            CASE 
//                                                WHEN col.data_type = 'character varying' THEN format('varchar(%s)', col.character_maximum_length)
//                                                WHEN col.data_type = 'numeric' THEN format('numeric(%s,%s)', col.numeric_precision, col.numeric_scale)
//                                                ELSE col.data_type
//                                            END,
//                                            CASE WHEN col.column_default IS NOT NULL THEN ' DEFAULT ' || col.column_default ELSE '' END,
//                                            CASE WHEN col.is_nullable = 'NO' THEN ' NOT NULL' ELSE '' END,
//                                            ''
//                                        );
//                                END LOOP;

//                                -- Get primary key columns
//                                SELECT string_agg(format('""%s""', kcu.column_name), ', ')
//                                INTO pk_cols
//                                FROM information_schema.table_constraints tc
//                                JOIN information_schema.key_column_usage kcu 
//                                  ON tc.constraint_name = kcu.constraint_name
//                                WHERE tc.table_schema = 'public' 
//                                  AND tc.table_name = p_table_name 
//                                  AND tc.constraint_type = 'PRIMARY KEY';

//                                IF pk_cols IS NOT NULL THEN
//                                    col_defs := col_defs || format('PRIMARY KEY (%s), ', pk_cols);
//                                END IF;

//                                col_defs := left(col_defs, length(col_defs) - 2);
//                                result := format('CREATE TABLE ""%s"" (%s);', p_table_name, col_defs);
//                                RETURN result;
//                            END;
//                            $$;
//                            ";
//            await _connection.ExecuteCommandAsync(createFunctionSql);
//        }
//    }

//    private void Log(string message, LogLevel level = LogLevel.Basic)
//    {
//        if (_logLevel >= level)
//        {
//            _logger?.Invoke(0, 0, message, false);
//        }
//    }


//    #region Execute Commands and Queries
//    public async Task<DataTable> ExecuteQueryAsync(string query)
//    {
//        var result = await _connection.ExecuteQueryAsync(query);
//        Log($"Query: {query}", LogLevel.Verbose);
//        Log($"Rows returned: {result.Rows.Count}", LogLevel.Verbose);
//        return result;
//    }

//    public async Task<DataTable> ExecuteQueryAsync(string query, Dictionary<string, object> parameters)
//    {
//        var result = await _connection.ExecuteQueryAsync(query, parameters);
//        Log($"Query: {query}", LogLevel.Verbose);
//        Log($"Rows returned: {result.Rows.Count}", LogLevel.Verbose);
//        return result;
//    }

//    internal async Task<string?> GetViewDefinitionAsync(string viewName)
//    {
//        const string sql = @"
//        SELECT pg_get_viewdef(c.oid, true)
//        FROM pg_class c
//        JOIN pg_namespace n ON n.oid = c.relnamespace
//        WHERE c.relkind = 'v'
//          AND n.nspname = 'public'
//          AND c.relname = @viewName
//        LIMIT 1;";

//        var parameters = new Dictionary<string, object>
//    {
//        { "viewName", viewName }
//    };

//        var result = await _connection.ExecuteQueryAsync(sql, parameters);
//        if (result.Rows.Count > 0 && result.Rows[0][0] != DBNull.Value)
//            return result.Rows[0][0].ToString();

//        return null;
//    }


//    internal async Task<string?> GetTriggerDefinitionAsync(string triggerName)
//    {
//        const string sql = @"
//        SELECT pg_get_triggerdef(t.oid, true)
//        FROM pg_trigger t
//        JOIN pg_class c ON c.oid = t.tgrelid
//        JOIN pg_namespace n ON n.oid = c.relnamespace
//        WHERE t.tgname = @triggerName
//          AND n.nspname = 'public'
//        LIMIT 1;";

//        var parameters = new Dictionary<string, object>
//    {
//        { "triggerName", triggerName }
//    };

//        var result = await _connection.ExecuteQueryAsync(sql, parameters);
//        if (result.Rows.Count > 0 && result.Rows[0][0] != DBNull.Value)
//            return result.Rows[0][0].ToString();

//        return null;
//    }


//    #endregion

//}
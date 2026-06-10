using B2A.DbTula.Core.Models;
using System.Data;

namespace B2A.DbTula.Infrastructure.Postgres;

/// <summary>
/// Fetches the complete schema for one Postgres database using ~13 parallel bulk queries.
/// Requires Postgres 11+ for index INCLUDE column support.
///
/// Key improvements from open-source tools:
///  - Extension filtering (schemainspect): pg_depend deptype='e' excludes all extension-owned objects
///    (PostGIS tables, pgcrypto functions, etc.) from every query, preventing false positives.
///  - Identity sequence exclusion (schemainspect): sequences owned by GENERATED IDENTITY columns
///    are auto-managed by Postgres and must not appear in standalone sequence comparison.
///  - Index INCLUDE columns (Atlas): Postgres 11+ INCLUDE clause captured separately from key columns.
///  - NULLS DISTINCT (Atlas): Postgres 15+ unique index NULLS [NOT] DISTINCT captured.
///  - NoInherit (Atlas/apgdiff): CHECK constraint connoinherit flag captured.
/// </summary>
public class BulkSchemaFetcher
{
    private readonly DatabaseConnection _connection;

    public BulkSchemaFetcher(DatabaseConnection connection)
    {
        _connection = connection;
    }

    public async Task<SchemaSnapshot> TakeSnapshotAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Throttle to 5 concurrent connections — prevents WireGuard/firewall drops
        // when both QA and PROD snapshots run in parallel (14 × 2 = 28 raw connections).
        using var sem = new SemaphoreSlim(5, 5);
        async Task<T> Run<T>(Func<Task<T>> fn)
        {
            await sem.WaitAsync(ct);
            try   { return await fn(); }
            finally { sem.Release(); }
        }

        var tablesTask     = Run(FetchTableNamesAsync);
        var columnsTask    = Run(FetchAllColumnsAsync);
        var pksTask        = Run(FetchAllPrimaryKeysAsync);
        var fksTask        = Run(FetchAllForeignKeysAsync);
        var indexesTask    = Run(FetchAllIndexesAsync);
        var uniqueConsTask = Run(FetchAllUniqueConstraintsAsync);
        var checkConsTask  = Run(FetchAllCheckConstraintsAsync);
        var functionsTask  = Run(FetchAllFunctionsAsync);
        var proceduresTask = Run(FetchAllProceduresAsync);
        var viewsTask      = Run(FetchAllViewsAsync);
        var triggersTask   = Run(FetchAllTriggersAsync);
        var sequencesTask  = Run(FetchAllSequencesAsync);
        var matviewsTask   = Run(FetchMaterializedViewNamesAsync);
        var enumsTask      = Run(FetchAllEnumsAsync);

        await Task.WhenAll(
            tablesTask, columnsTask, pksTask, fksTask, indexesTask,
            uniqueConsTask, checkConsTask, functionsTask, proceduresTask,
            viewsTask, triggersTask, sequencesTask, matviewsTask, enumsTask);

        return new SchemaSnapshot
        {
            TableNames               = tablesTask.Result,
            ColumnsByTable           = columnsTask.Result,
            PrimaryKeysByTable       = pksTask.Result,
            ForeignKeysByTable       = fksTask.Result,
            IndexesByTable           = indexesTask.Result,
            UniqueConstraintsByTable = uniqueConsTask.Result,
            CheckConstraintsByTable  = checkConsTask.Result,
            Functions                = functionsTask.Result,
            Procedures               = proceduresTask.Result,
            Views                    = viewsTask.Result,
            Triggers                 = triggersTask.Result,
            Sequences                = sequencesTask.Result,
            MaterializedViewNames    = matviewsTask.Result,
            Enums                    = enumsTask.Result,
            CapturedAt               = DateTimeOffset.UtcNow,
        };
    }

    // ── Tables ────────────────────────────────────────────────────────────────
    // Extension filter: LEFT JOIN pg_depend ext ... ext.objid IS NULL
    // Excludes tables created by extensions (e.g. PostGIS spatial_ref_sys).

    private async Task<IReadOnlyList<string>> FetchTableNamesAsync()
    {
        const string sql = @"
            SELECT pc.relname AS table_name
            FROM pg_class pc
            JOIN pg_namespace pn ON pn.oid = pc.relnamespace
            LEFT JOIN pg_depend ext
                ON ext.classid = 'pg_class'::regclass
               AND ext.objid   = pc.oid
               AND ext.deptype = 'e'
            WHERE pn.nspname = 'public'
              AND pc.relkind  IN ('r', 'p')
              AND ext.objid   IS NULL
            ORDER BY pc.relname;";

        var dt = await _connection.ExecuteQueryAsync(sql);
        return dt.AsEnumerable()
            .Select(r => r["table_name"].ToString() ?? string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
    }

    // ── Columns ───────────────────────────────────────────────────────────────

    private async Task<IReadOnlyDictionary<string, List<ColumnDefinition>>> FetchAllColumnsAsync()
    {
        const string sql = @"
            SELECT
                c.table_name,
                c.column_name,
                c.ordinal_position,
                c.data_type,
                c.udt_name,
                c.character_maximum_length,
                c.numeric_precision,
                c.numeric_scale,
                c.datetime_precision,
                c.is_nullable,
                c.column_default,
                c.is_generated,
                c.identity_generation
            FROM information_schema.columns c
            JOIN pg_class pc ON pc.relname = c.table_name
            JOIN pg_namespace pn ON pn.oid = pc.relnamespace
            LEFT JOIN pg_depend ext
                ON ext.classid = 'pg_class'::regclass
               AND ext.objid   = pc.oid
               AND ext.deptype = 'e'
            WHERE c.table_schema = 'public'
              AND pn.nspname     = 'public'
              AND pc.relkind     IN ('r', 'p')
              AND ext.objid      IS NULL
            ORDER BY c.table_name, c.ordinal_position;";

        var dt = await _connection.ExecuteQueryAsync(sql);

        return dt.AsEnumerable()
            .GroupBy(r => r["table_name"].ToString() ?? string.Empty)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new ColumnDefinition
                {
                    Name              = r["column_name"].ToString() ?? string.Empty,
                    DataType          = r["data_type"].ToString() ?? string.Empty,
                    UdtName           = r["udt_name"].ToString(),
                    IsNullable        = r["is_nullable"].ToString() == "YES",
                    Length            = r["character_maximum_length"] != DBNull.Value ? Convert.ToInt32(r["character_maximum_length"]) : null,
                    NumericPrecision  = r["numeric_precision"] != DBNull.Value ? Convert.ToInt32(r["numeric_precision"]) : null,
                    NumericScale      = r["numeric_scale"] != DBNull.Value ? Convert.ToInt32(r["numeric_scale"]) : null,
                    DateTimePrecision = r["datetime_precision"] != DBNull.Value ? Convert.ToInt32(r["datetime_precision"]) : null,
                    DefaultValue      = r["column_default"] != DBNull.Value ? r["column_default"].ToString() : null,
                    IsIdentity        = r["identity_generation"] != DBNull.Value && !string.IsNullOrEmpty(r["identity_generation"].ToString()),
                    IsComputed        = r["is_generated"].ToString() == "ALWAYS",
                }).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    // ── Primary Keys ──────────────────────────────────────────────────────────

    private async Task<IReadOnlyDictionary<string, List<PrimaryKeyDefinition>>> FetchAllPrimaryKeysAsync()
    {
        const string sql = @"
            SELECT
                rel.relname AS table_name,
                con.conname AS constraint_name,
                att.attname AS column_name,
                array_position(con.conkey, att.attnum) AS column_position
            FROM pg_constraint con
            JOIN pg_class rel     ON rel.oid = con.conrelid
            JOIN pg_namespace ns  ON ns.oid  = rel.relnamespace
            JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = ANY(con.conkey)
            LEFT JOIN pg_depend ext
                ON ext.classid = 'pg_class'::regclass
               AND ext.objid   = rel.oid
               AND ext.deptype = 'e'
            WHERE con.contype = 'p'
              AND ns.nspname  = 'public'
              AND ext.objid   IS NULL
            ORDER BY rel.relname, con.conname, column_position;";

        var dt = await _connection.ExecuteQueryAsync(sql);

        return dt.AsEnumerable()
            .GroupBy(r => r["table_name"].ToString() ?? string.Empty)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r["constraint_name"].ToString() ?? string.Empty)
                       .Select(cg => new PrimaryKeyDefinition
                       {
                           Name    = cg.Key,
                           Columns = cg.OrderBy(r => Convert.ToInt32(r["column_position"]))
                                       .Select(r => r["column_name"].ToString() ?? string.Empty)
                                       .ToList()
                       }).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    // ── Foreign Keys ──────────────────────────────────────────────────────────

    private async Task<IReadOnlyDictionary<string, List<ForeignKeyDefinition>>> FetchAllForeignKeysAsync()
    {
        const string sql = @"
            SELECT
                rel.relname AS table_name,
                con.conname AS constraint_name,
                src_att.attname AS column_name,
                ref_rel.relname AS referenced_table,
                ref_att.attname AS referenced_column,
                src.ord AS column_position,
                CASE con.confupdtype
                    WHEN 'a' THEN 'NO ACTION'   WHEN 'r' THEN 'RESTRICT'
                    WHEN 'c' THEN 'CASCADE'     WHEN 'n' THEN 'SET NULL'
                    WHEN 'd' THEN 'SET DEFAULT' END AS on_update,
                CASE con.confdeltype
                    WHEN 'a' THEN 'NO ACTION'   WHEN 'r' THEN 'RESTRICT'
                    WHEN 'c' THEN 'CASCADE'     WHEN 'n' THEN 'SET NULL'
                    WHEN 'd' THEN 'SET DEFAULT' END AS on_delete
            FROM pg_constraint con
            JOIN pg_class rel     ON rel.oid = con.conrelid
            JOIN pg_namespace ns  ON ns.oid  = rel.relnamespace
            JOIN pg_class ref_rel ON ref_rel.oid = con.confrelid
            JOIN LATERAL unnest(con.conkey)  WITH ORDINALITY AS src(attnum, ord) ON true
            JOIN LATERAL unnest(con.confkey) WITH ORDINALITY AS ref(attnum, ord) ON ref.ord = src.ord
            JOIN pg_attribute src_att ON src_att.attrelid = con.conrelid  AND src_att.attnum = src.attnum
            JOIN pg_attribute ref_att ON ref_att.attrelid = con.confrelid AND ref_att.attnum = ref.attnum
            LEFT JOIN pg_depend ext
                ON ext.classid = 'pg_class'::regclass
               AND ext.objid   = rel.oid
               AND ext.deptype = 'e'
            WHERE con.contype = 'f'
              AND ns.nspname  = 'public'
              AND ext.objid   IS NULL
            ORDER BY rel.relname, con.conname, src.ord;";

        var dt = await _connection.ExecuteQueryAsync(sql);

        return dt.AsEnumerable()
            .GroupBy(r => r["table_name"].ToString() ?? string.Empty)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r["constraint_name"].ToString() ?? string.Empty)
                       .Select(cg =>
                       {
                           var first = cg.First();
                           return new ForeignKeyDefinition
                           {
                               Name             = cg.Key,
                               ColumnName       = string.Join(",", cg.Select(r => r["column_name"].ToString())),
                               ReferencedTable  = first["referenced_table"].ToString() ?? string.Empty,
                               ReferencedColumn = string.Join(",", cg.Select(r => r["referenced_column"].ToString())),
                               OnUpdate         = first["on_update"].ToString() ?? "NO ACTION",
                               OnDelete         = first["on_delete"].ToString() ?? "NO ACTION",
                           };
                       }).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    // ── Indexes ───────────────────────────────────────────────────────────────
    // Atlas improvement: ix.indnkeyatts separates key columns from INCLUDE columns (Postgres 11+).
    // Extension filter: excludes extension-owned indexes (e.g. PostGIS spatial indexes).

    private async Task<IReadOnlyDictionary<string, List<IndexDefinition>>> FetchAllIndexesAsync()
    {
        const string sql = @"
            SELECT
                t.relname  AS table_name,
                i.relname  AS index_name,
                am.amname  AS index_type,
                ix.indisunique AS is_unique,
                a.attname  AS column_name,
                cols.ord   AS column_position,
                (cols.ord > ix.indnkeyatts) AS is_included,
                pg_get_expr(ix.indpred, ix.indrelid, true) AS predicate
            FROM pg_class t
            JOIN pg_index ix   ON t.oid  = ix.indrelid
            JOIN pg_class i    ON i.oid  = ix.indexrelid
            JOIN pg_am am      ON i.relam = am.oid
            JOIN pg_namespace ns ON ns.oid = t.relnamespace
            CROSS JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS cols(attnum, ord)
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = cols.attnum
            LEFT JOIN pg_depend ext
                ON ext.classid = 'pg_class'::regclass
               AND ext.objid   = i.oid
               AND ext.deptype = 'e'
            LEFT JOIN pg_depend tbl_ext
                ON tbl_ext.classid = 'pg_class'::regclass
               AND tbl_ext.objid   = t.oid
               AND tbl_ext.deptype = 'e'
            WHERE ns.nspname       = 'public'
              AND ix.indisvalid    = true
              AND ix.indisprimary  = false
              AND a.attnum         > 0
              AND ext.objid        IS NULL
              AND tbl_ext.objid    IS NULL
              -- Exclude indexes that implement a constraint (unique/PK); those are
              -- emitted via the constraint path, so listing them here double-creates.
              AND NOT EXISTS (SELECT 1 FROM pg_constraint con WHERE con.conindid = i.oid)
            ORDER BY t.relname, i.relname, cols.ord;";

        var dt = await _connection.ExecuteQueryAsync(sql);

        return dt.AsEnumerable()
            .GroupBy(r => r["table_name"].ToString() ?? string.Empty)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r["index_name"].ToString() ?? string.Empty)
                       .Select(ig =>
                       {
                           var first     = ig.First();
                           var keyRows   = ig.Where(r => r["is_included"] == DBNull.Value || !(bool)r["is_included"]).ToList();
                           var inclRows  = ig.Where(r => r["is_included"] != DBNull.Value && (bool)r["is_included"]).ToList();

                           return new IndexDefinition
                           {
                               Name            = ig.Key,
                               Columns         = keyRows.Select(r => r["column_name"].ToString() ?? string.Empty).ToList(),
                               IncludedColumns = inclRows.Select(r => r["column_name"].ToString() ?? string.Empty).ToList(),
                               IsUnique        = first["is_unique"] != DBNull.Value && (bool)first["is_unique"],
                               IndexType       = first["index_type"]?.ToString() ?? string.Empty,
                               Predicate       = first["predicate"] != DBNull.Value ? first["predicate"]?.ToString() : null,
                           };
                       }).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    // ── Unique Constraints ────────────────────────────────────────────────────

    private async Task<IReadOnlyDictionary<string, List<UniqueConstraintDefinition>>> FetchAllUniqueConstraintsAsync()
    {
        const string sql = @"
            SELECT
                rel.relname AS table_name,
                con.conname AS constraint_name,
                att.attname AS column_name,
                array_position(con.conkey, att.attnum) AS column_position
            FROM pg_constraint con
            JOIN pg_class rel     ON rel.oid = con.conrelid
            JOIN pg_namespace ns  ON ns.oid  = rel.relnamespace
            JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = ANY(con.conkey)
            LEFT JOIN pg_depend ext
                ON ext.classid = 'pg_class'::regclass
               AND ext.objid   = rel.oid
               AND ext.deptype = 'e'
            WHERE con.contype = 'u'
              AND ns.nspname  = 'public'
              AND ext.objid   IS NULL
            ORDER BY rel.relname, con.conname, column_position;";

        var dt = await _connection.ExecuteQueryAsync(sql);

        return dt.AsEnumerable()
            .GroupBy(r => r["table_name"].ToString() ?? string.Empty)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r["constraint_name"].ToString() ?? string.Empty)
                       .Select(cg => new UniqueConstraintDefinition
                       {
                           Name    = cg.Key,
                           Columns = cg.OrderBy(r => Convert.ToInt32(r["column_position"]))
                                       .Select(r => r["column_name"].ToString() ?? string.Empty)
                                       .ToList()
                       }).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    // ── Check Constraints ─────────────────────────────────────────────────────
    // apgdiff improvement: connoinherit (NO INHERIT) flag captured so constraints
    // that do not propagate to child tables are correctly differentiated.

    private async Task<IReadOnlyDictionary<string, List<CheckConstraintDefinition>>> FetchAllCheckConstraintsAsync()
    {
        const string sql = @"
            SELECT
                rel.relname AS table_name,
                con.conname AS constraint_name,
                pg_get_constraintdef(con.oid, true) AS check_clause,
                con.connoinherit AS no_inherit
            FROM pg_constraint con
            JOIN pg_class rel    ON rel.oid = con.conrelid
            JOIN pg_namespace ns ON ns.oid  = rel.relnamespace
            LEFT JOIN pg_depend ext
                ON ext.classid = 'pg_class'::regclass
               AND ext.objid   = rel.oid
               AND ext.deptype = 'e'
            WHERE con.contype   = 'c'
              AND ns.nspname    = 'public'
              AND con.conislocal = true
              AND ext.objid     IS NULL
            ORDER BY rel.relname, con.conname;";

        var dt = await _connection.ExecuteQueryAsync(sql);

        return dt.AsEnumerable()
            .GroupBy(r => r["table_name"].ToString() ?? string.Empty)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new CheckConstraintDefinition
                {
                    Name        = r["constraint_name"].ToString() ?? string.Empty,
                    CheckClause = r["check_clause"].ToString() ?? string.Empty,
                    NoInherit   = r["no_inherit"] != DBNull.Value && (bool)r["no_inherit"],
                }).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    // ── Functions ─────────────────────────────────────────────────────────────
    // Extension filter: excludes extension-provided functions (e.g. PostGIS ST_*, pgcrypto digest()).

    private async Task<IReadOnlyList<DbFunctionDefinition>> FetchAllFunctionsAsync()
    {
        // NOT EXISTS pattern (from schemainspect/pgquarrel) — unambiguous, avoids LEFT JOIN fan-out
        const string sql = @"
            SELECT
                p.proname AS routine_name,
                pg_get_function_identity_arguments(p.oid) AS arguments,
                pg_get_functiondef(p.oid) AS definition
            FROM pg_proc p
            JOIN pg_namespace n ON p.pronamespace = n.oid
            WHERE n.nspname = 'public'
              AND p.prokind = 'f'
              AND NOT EXISTS (
                  SELECT 1 FROM pg_depend d
                  WHERE d.objid   = p.oid
                    AND d.classid = 'pg_proc'::regclass
                    AND d.deptype = 'e'
              );";

        return await FetchFunctionListAsync(sql);
    }

    private async Task<IReadOnlyList<DbFunctionDefinition>> FetchAllProceduresAsync()
    {
        const string sql = @"
            SELECT
                p.proname AS routine_name,
                pg_get_function_identity_arguments(p.oid) AS arguments,
                pg_get_functiondef(p.oid) AS definition
            FROM pg_proc p
            JOIN pg_namespace n ON p.pronamespace = n.oid
            WHERE n.nspname = 'public'
              AND p.prokind = 'p'
              AND NOT EXISTS (
                  SELECT 1 FROM pg_depend d
                  WHERE d.objid   = p.oid
                    AND d.classid = 'pg_proc'::regclass
                    AND d.deptype = 'e'
              );";

        return await FetchFunctionListAsync(sql);
    }

    private async Task<IReadOnlyList<DbFunctionDefinition>> FetchFunctionListAsync(string sql)
    {
        var dt = await _connection.ExecuteQueryAsync(sql);
        return dt.AsEnumerable()
            .Select(r => new DbFunctionDefinition
            {
                Name       = r["routine_name"].ToString(),
                Arguments  = r["arguments"]?.ToString(),
                Definition = r["definition"]?.ToString(),
            }).ToList();
    }

    // ── Views ─────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<DbViewDefinition>> FetchAllViewsAsync()
    {
        const string sql = @"
            SELECT
                c.relname AS view_name,
                pg_get_viewdef(c.oid, true) AS definition
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_depend ext
                ON ext.classid = 'pg_class'::regclass
               AND ext.objid   = c.oid
               AND ext.deptype = 'e'
            WHERE c.relkind = 'v'
              AND n.nspname = 'public'
              AND ext.objid IS NULL
            ORDER BY c.relname;";

        var dt = await _connection.ExecuteQueryAsync(sql);
        return dt.AsEnumerable()
            .Select(r => new DbViewDefinition
            {
                Name       = r["view_name"].ToString() ?? string.Empty,
                Definition = r["definition"] != DBNull.Value ? r["definition"]?.ToString() : null,
            }).ToList();
    }

    // ── Triggers ──────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<DbTriggerDefinition>> FetchAllTriggersAsync()
    {
        const string sql = @"
            SELECT
                trg.tgname  AS trigger_name,
                tbl.relname AS table_name,
                pg_get_triggerdef(trg.oid, true) AS definition
            FROM pg_trigger trg
            JOIN pg_class tbl ON tbl.oid = trg.tgrelid
            JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
            LEFT JOIN pg_depend ext
                ON ext.classid = 'pg_trigger'::regclass
               AND ext.objid   = trg.oid
               AND ext.deptype = 'e'
            WHERE ns.nspname      = 'public'
              AND NOT trg.tgisinternal
              AND ext.objid       IS NULL
            ORDER BY tbl.relname, trg.tgname;";

        var dt = await _connection.ExecuteQueryAsync(sql);
        return dt.AsEnumerable()
            .Select(r => new DbTriggerDefinition
            {
                Name       = r["trigger_name"].ToString() ?? string.Empty,
                Table      = r["table_name"].ToString() ?? string.Empty,
                Definition = r["definition"] != DBNull.Value ? r["definition"]?.ToString() : null,
            }).ToList();
    }

    // ── Sequences ─────────────────────────────────────────────────────────────
    // schemainspect improvement: exclude identity-owned sequences (deptype='i') — these are
    // automatically managed by Postgres for GENERATED ALWAYS AS IDENTITY columns and must not
    // appear in standalone sequence comparison.
    // Extension filter: exclude extension-owned sequences.

    private async Task<IReadOnlyList<DbSequenceDefinition>> FetchAllSequencesAsync()
    {
        const string sql = @"
            SELECT
                s.sequencename,
                s.data_type,
                s.start_value,
                s.increment_by,
                s.min_value,
                s.max_value,
                s.cache_size,
                s.cycle
            FROM pg_sequences s
            JOIN pg_class sc ON sc.relname = s.sequencename
                             AND sc.relnamespace = 'public'::regnamespace
            LEFT JOIN pg_depend identity_dep
                ON identity_dep.objid   = sc.oid
               AND identity_dep.deptype = 'i'
            LEFT JOIN pg_depend ext
                ON ext.classid = 'pg_class'::regclass
               AND ext.objid   = sc.oid
               AND ext.deptype = 'e'
            WHERE s.schemaname       = 'public'
              AND identity_dep.objid IS NULL
              AND ext.objid          IS NULL
            ORDER BY s.sequencename;";

        var dt = await _connection.ExecuteQueryAsync(sql);
        return dt.AsEnumerable()
            .Select(r => new DbSequenceDefinition
            {
                Name        = r["sequencename"].ToString() ?? string.Empty,
                DataType    = r["data_type"].ToString() ?? "bigint",
                StartValue  = Convert.ToInt64(r["start_value"]),
                IncrementBy = Convert.ToInt64(r["increment_by"]),
                MinValue    = Convert.ToInt64(r["min_value"]),
                MaxValue    = Convert.ToInt64(r["max_value"]),
                CacheSize   = Convert.ToInt64(r["cache_size"]),
                Cycle       = r["cycle"] != DBNull.Value && (bool)r["cycle"],
            }).ToList();
    }

    // ── Enum Types ────────────────────────────────────────────────────────────
    // Extension filter: excludes extension-provided enum types (e.g. PostGIS geometry types).

    private async Task<IReadOnlyList<EnumTypeDefinition>> FetchAllEnumsAsync()
    {
        const string sql = @"
            SELECT
                t.typname       AS enum_name,
                e.enumlabel     AS enum_value,
                e.enumsortorder AS sort_order
            FROM pg_type t
            JOIN pg_namespace n ON n.oid = t.typnamespace
            JOIN pg_enum e      ON e.enumtypid = t.oid
            LEFT JOIN pg_depend ext
                ON ext.classid = 'pg_type'::regclass
               AND ext.objid   = t.oid
               AND ext.deptype = 'e'
            WHERE t.typtype = 'e'
              AND n.nspname = 'public'
              AND ext.objid IS NULL
            ORDER BY t.typname, e.enumsortorder;";

        var dt = await _connection.ExecuteQueryAsync(sql);

        return dt.AsEnumerable()
            .GroupBy(r => r["enum_name"].ToString() ?? string.Empty)
            .Select(g => new EnumTypeDefinition
            {
                Name   = g.Key,
                Values = g.OrderBy(r => Convert.ToDouble(r["sort_order"]))
                          .Select(r => r["enum_value"].ToString() ?? string.Empty)
                          .ToList()
            }).ToList();
    }

    // ── Materialized Views ────────────────────────────────────────────────────

    private async Task<HashSet<string>> FetchMaterializedViewNamesAsync()
    {
        const string sql = @"
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_depend ext
                ON ext.classid = 'pg_class'::regclass
               AND ext.objid   = c.oid
               AND ext.deptype = 'e'
            WHERE c.relkind = 'm'
              AND n.nspname = 'public'
              AND ext.objid IS NULL;";

        var dt = await _connection.ExecuteQueryAsync(sql);
        return dt.AsEnumerable()
            .Select(r => r["relname"].ToString() ?? string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

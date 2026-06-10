using B2a.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using B2A.DbTula.Core.Utilities;
using B2A.DbTula.Cli.Services;
using System.Text;
using System.Diagnostics;

namespace B2A.DbTula.Cli;

/// <summary>
/// Enhanced Schema Comparer with Order-Independent Semantic Comparison
/// 
/// Features:
/// - Table comparison is order-independent and semantic using structural signatures
/// - Columns compared by normalized structure (name, type, nullability, default, identity), order does NOT matter
/// - Constraints (PK, FK, Unique, Check) compared by structure, not name/text, order does NOT matter
/// - FKs deduplicated by structural key to avoid double-counting
/// - Indexes compared by method + columns + options + predicate, not name or SQL text
/// - Primary Key and Index Detection with provider-specific logic (Postgres/MySQL)
/// - Robust handling of partitioned tables, inherited tables, materialized views, invalid indexes
/// - Predicate normalization for partial indexes (whitespace-insensitive)
/// - Provider type detection for Postgres vs MySQL specific behavior
/// - Functions, procedures, views, triggers compared by normalized signature
/// - Backward compatible output with maximized correctness and clarity
/// - Well-commented, maintainable, and extensible code structure
/// </summary>
public class SchemaComparer : ISchemaComparer
{
    private readonly SqlDiffService _sqlDiffService;
    private ComparisonOptions _currentOptions = new();
    private HashSet<string> _sourceMatViews = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _targetMatViews = new(StringComparer.OrdinalIgnoreCase);
    private IDatabaseSchemaProvider? _lastSourceProvider;

    /// <summary>
    /// The source snapshot from the most recent CompareAsync call.
    /// Available after comparison for use by SyncScriptGenerator (enum rename-trick needs column info).
    /// </summary>
    public SchemaSnapshot? LastSourceSnapshot { get; private set; }
    public SchemaSnapshot? LastTargetSnapshot { get; private set; }

    public SchemaComparer()
    {
        _sqlDiffService = new SqlDiffService();
    }
    public async Task<IList<ComparisonResult>> CompareAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Action<int, int, string, bool>? progressLogger = null,
        bool runForTest = false, int testObjectLimit = 10,
        ComparisonOptions? options = null)
    {
        options ??= new ComparisonOptions();
        _currentOptions = options;
        _lastSourceProvider = sourceProvider;

        progressLogger?.Invoke(0, 0, "🔍 Taking schema snapshots...", false);
        var sw = Stopwatch.StartNew();

        // If both providers support bulk snapshots, fetch both in parallel (the big win)
        SchemaSnapshot? sourceSnapshot = null;
        SchemaSnapshot? targetSnapshot = null;

        if (sourceProvider is IDatabaseSchemaSnapshot srcSnap && targetProvider is IDatabaseSchemaSnapshot tgtSnap)
        {
            var srcTask = srcSnap.TakeSnapshotAsync();
            var tgtTask = tgtSnap.TakeSnapshotAsync();
            await Task.WhenAll(srcTask, tgtTask);
            sourceSnapshot = srcTask.Result;
            targetSnapshot = tgtTask.Result;
            _sourceMatViews = sourceSnapshot.MaterializedViewNames;
            _targetMatViews = targetSnapshot.MaterializedViewNames;
            LastSourceSnapshot = sourceSnapshot;
            LastTargetSnapshot = targetSnapshot;
            progressLogger?.Invoke(0, 0, $"✅ Snapshots taken in {sw.ElapsedMilliseconds}ms", false);
        }
        else
        {
            // Fallback: fetch matviews individually (e.g. MySQL provider)
            var sourceMatViewsTask = sourceProvider.GetMaterializedViewNamesAsync();
            var targetMatViewsTask = targetProvider.GetMaterializedViewNamesAsync();
            await Task.WhenAll(sourceMatViewsTask, targetMatViewsTask);
            _sourceMatViews = sourceMatViewsTask.Result;
            _targetMatViews = targetMatViewsTask.Result;
        }

        var results = new List<ComparisonResult>();
        var phaseTimes = new List<(string Phase, long Ms)>();
        long phaseStart;

        // ── TABLES ────────────────────────────────────────────────────────────
        var sourceTables = sourceSnapshot?.TableNames.ToList()
            ?? (await sourceProvider.GetTablesAsync()).ToList();
        var targetTables = targetSnapshot?.TableNames.ToList()
            ?? (await targetProvider.GetTablesAsync()).ToList();

        var limitedSourceTables = runForTest ? sourceTables.Take(testObjectLimit).ToList() : sourceTables;
        var limitedTargetTables = runForTest
            ? targetTables.Where(t => limitedSourceTables.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList()
            : targetTables;

        progressLogger?.Invoke(0, 0, $"📄 Comparing {limitedSourceTables.Count} tables...", false);
        phaseStart = sw.ElapsedMilliseconds;

        await CompareTablesAsync(
            sourceProvider, targetProvider,
            limitedSourceTables, limitedTargetTables,
            results, progressLogger, options,
            sourceSnapshot, targetSnapshot);

        phaseTimes.Add(("Tables", sw.ElapsedMilliseconds - phaseStart));

        // ── FUNCTIONS ─────────────────────────────────────────────────────────
        // NormalizeFunctionKey: pg_get_function_identity_arguments uses the current search_path
        // to decide whether to schema-qualify type names. QA and PROD may have different
        // search_paths, so "update_invoice(p_status invoice_status)" on QA becomes
        // "update_invoice(p_status public.invoice_status)" on PROD — different keys, false "missing".
        // Fix: strip schema qualifiers from argument type names and normalise whitespace.
        static string NormalizeArgs(string? args)
        {
            if (string.IsNullOrWhiteSpace(args)) return "";
            // Remove schema-qualified prefixes from type names (e.g. public.my_type → my_type)
            var s = System.Text.RegularExpressions.Regex.Replace(
                args.Trim(), @"\b\w+\.", "", System.Text.RegularExpressions.RegexOptions.None);
            return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").ToLowerInvariant();
        }
        static string GetSignatureKey(DbFunctionDefinition def) =>
            $"{def.Name?.Trim().ToLowerInvariant()}({NormalizeArgs(def.Arguments)})";

        IEnumerable<DbFunctionDefinition> srcFuncList = sourceSnapshot != null
            ? sourceSnapshot.Functions : await sourceProvider.GetFunctionsAsync();
        IEnumerable<DbFunctionDefinition> tgtFuncList = targetSnapshot != null
            ? targetSnapshot.Functions : await targetProvider.GetFunctionsAsync();

        // Deduplicate by key in case the same function appears twice (should not happen but guards ToDictionary crash)
        var sourceFunctions = srcFuncList
            .GroupBy(GetSignatureKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var targetFunctions = tgtFuncList
            .GroupBy(GetSignatureKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        progressLogger?.Invoke(0, 0,
            $"⚙️ Functions found — source: {sourceFunctions.Count}, target: {targetFunctions.Count}", false);

        var limitedSrcFunctions = runForTest
            ? sourceFunctions.Take(testObjectLimit).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            : sourceFunctions;
        var limitedTgtFunctions = targetFunctions
            .Where(kvp => limitedSrcFunctions.ContainsKey(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        progressLogger?.Invoke(0, 0, $"⚙️ Comparing {limitedSrcFunctions.Count} functions...", false);
        phaseStart = sw.ElapsedMilliseconds;
        await CompareFunctionsAsync(sourceProvider, targetProvider, limitedSrcFunctions, limitedTgtFunctions, results, progressLogger, options);
        phaseTimes.Add(("Functions", sw.ElapsedMilliseconds - phaseStart));

        // ── PROCEDURES ────────────────────────────────────────────────────────
        IEnumerable<DbFunctionDefinition> srcProcList = sourceSnapshot != null
            ? sourceSnapshot.Procedures : await sourceProvider.GetProceduresAsync();
        IEnumerable<DbFunctionDefinition> tgtProcList = targetSnapshot != null
            ? targetSnapshot.Procedures : await targetProvider.GetProceduresAsync();

        var sourceProcedures = srcProcList
            .GroupBy(GetSignatureKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var targetProcedures = tgtProcList
            .GroupBy(GetSignatureKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        progressLogger?.Invoke(0, 0,
            $"🛠 Procedures found — source: {sourceProcedures.Count}, target: {targetProcedures.Count}", false);

        var limitedSrcProcedures = runForTest
            ? sourceProcedures.Take(testObjectLimit).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            : sourceProcedures;
        var limitedTgtProcedures = targetProcedures
            .Where(kvp => limitedSrcProcedures.ContainsKey(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        progressLogger?.Invoke(0, 0, $"🛠 Comparing {limitedSrcProcedures.Count} procedures...", false);
        phaseStart = sw.ElapsedMilliseconds;
        await CompareProceduresAsync(sourceProvider, targetProvider, limitedSrcProcedures, limitedTgtProcedures, results, progressLogger, options);
        phaseTimes.Add(("Procedures", sw.ElapsedMilliseconds - phaseStart));

        // ── VIEWS ─────────────────────────────────────────────────────────────
        IEnumerable<DbViewDefinition> srcViewList = sourceSnapshot != null
            ? sourceSnapshot.Views : await sourceProvider.GetViewsAsync();
        IEnumerable<DbViewDefinition> tgtViewList = targetSnapshot != null
            ? targetSnapshot.Views : await targetProvider.GetViewsAsync();

        var sourceViews = srcViewList.ToDictionary(v => v.Name!, StringComparer.OrdinalIgnoreCase);
        var targetViews = tgtViewList.ToDictionary(v => v.Name!, StringComparer.OrdinalIgnoreCase);

        var limitedSrcViews = runForTest
            ? sourceViews.Take(testObjectLimit).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            : sourceViews;
        var limitedTgtViews = targetViews
            .Where(kvp => limitedSrcViews.ContainsKey(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        progressLogger?.Invoke(0, 0, $"🔍 Comparing {limitedSrcViews.Count} views...", false);
        phaseStart = sw.ElapsedMilliseconds;
        await CompareViewsAsync(sourceProvider, targetProvider, limitedSrcViews, limitedTgtViews, results, progressLogger, options);
        phaseTimes.Add(("Views", sw.ElapsedMilliseconds - phaseStart));

        // ── TRIGGERS ──────────────────────────────────────────────────────────
        Func<DbTriggerDefinition, string> triggerKey = t => $"{t.Table}|{t.Name}";
        IEnumerable<DbTriggerDefinition> srcTrigList = sourceSnapshot != null
            ? sourceSnapshot.Triggers : await sourceProvider.GetTriggersAsync();
        IEnumerable<DbTriggerDefinition> tgtTrigList = targetSnapshot != null
            ? targetSnapshot.Triggers : await targetProvider.GetTriggersAsync();

        var sourceTriggers = srcTrigList.ToDictionary(triggerKey, StringComparer.OrdinalIgnoreCase);
        var targetTriggers = tgtTrigList.ToDictionary(triggerKey, StringComparer.OrdinalIgnoreCase);

        var limitedSrcTriggers = runForTest
            ? sourceTriggers.Take(testObjectLimit).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            : sourceTriggers;
        var limitedTgtTriggers = targetTriggers
            .Where(kvp => limitedSrcTriggers.ContainsKey(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        progressLogger?.Invoke(0, 0, $"⏰ Comparing {limitedSrcTriggers.Count} triggers...", false);
        phaseStart = sw.ElapsedMilliseconds;
        await CompareTriggersAsync(sourceProvider, targetProvider, limitedSrcTriggers, limitedTgtTriggers, results, progressLogger, options);
        phaseTimes.Add(("Triggers", sw.ElapsedMilliseconds - phaseStart));

        // ── SEQUENCES ─────────────────────────────────────────────────────────
        progressLogger?.Invoke(0, 0, "🔢 Comparing sequences...", false);
        phaseStart = sw.ElapsedMilliseconds;
        CompareSequences(sourceSnapshot?.Sequences, targetSnapshot?.Sequences, results);
        phaseTimes.Add(("Sequences", sw.ElapsedMilliseconds - phaseStart));

        // ── ENUMS ─────────────────────────────────────────────────────────────
        progressLogger?.Invoke(0, 0, "🔠 Comparing enum types...", false);
        phaseStart = sw.ElapsedMilliseconds;
        CompareEnums(sourceSnapshot?.Enums, targetSnapshot?.Enums, results);
        phaseTimes.Add(("Enums", sw.ElapsedMilliseconds - phaseStart));

        // ── TIMING SUMMARY ────────────────────────────────────────────────────
        if (progressLogger != null)
        {
            var totalMs = sw.ElapsedMilliseconds;
            var sb = new StringBuilder();
            sb.AppendLine("┌──────────────┬──────────┐");
            sb.AppendLine("│ Phase        │     Time │");
            sb.AppendLine("├──────────────┼──────────┤");
            foreach (var (phase, ms) in phaseTimes)
                sb.AppendLine($"│ {phase,-12} │ {ms,5}ms │");
            sb.AppendLine("├──────────────┼──────────┤");
            sb.Append(    $"│ {"TOTAL",-12} │ {totalMs,5}ms │");
            sb.AppendLine();
            sb.Append(    "└──────────────┴──────────┘");
            progressLogger.Invoke(0, 0, sb.ToString(), false);
        }

        progressLogger?.Invoke(0, 0, $"✅ Schema comparison completed in {sw.ElapsedMilliseconds}ms.", false);
        return results;
    }

    // ---------- COMPARE METHODS -----------

    private async Task CompareTablesAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        IList<string> sourceTables,
        IList<string> targetTables,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options,
        SchemaSnapshot? sourceSnapshot = null,
        SchemaSnapshot? targetSnapshot = null)
    {
        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);

        int total = sourceTables.Count;
        var targetTableSet = new HashSet<string>(targetTables, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < total; i++)
        {
            var tableName = sourceTables[i];
            progressLogger?.Invoke(i + 1, total, $"🔄 Comparing table: {tableName}", true);

            // Use snapshot if available (pure in-memory lookup), else fall back to per-table provider call
            var source = sourceSnapshot != null
                ? sourceSnapshot.GetTableDefinition(tableName)
                : await sourceProvider.GetTableDefinitionAsync(tableName);

            var target = targetTableSet.Contains(tableName)
                ? (targetSnapshot != null
                    ? targetSnapshot.GetTableDefinition(tableName)
                    : await targetProvider.GetTableDefinitionAsync(tableName))
                : null;

            if (target == null)
            {
                // Whole table missing in target. Reconstruct the full definition from the source:
                //   • CREATE TABLE (columns + PK + unique inline) from the typed model
                //   • FKs and secondary indexes pulled verbatim from the source catalog
                //     (pg_get_constraintdef / pg_get_indexdef)
                // Emitted as separate sub-results so the generator runs FKs/indexes AFTER all
                // tables exist. Without this the table was silently dropped from the sync script.
                var createScript = BuildCreateTableScript(source);
                var subs = new List<ComparisonSubResult>
                {
                    new("CreateScript", ComparisonStatus.MissingInTarget, "Table missing in target", createScript)
                };
                foreach (var fk in source.ForeignKeys)
                {
                    var fkScript = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, fk.Name);
                    if (!string.IsNullOrWhiteSpace(fkScript))
                        subs.Add(new("ForeignKeys", ComparisonStatus.MissingInTarget, $"Foreign key '{fk.Name}' missing in target", fkScript));
                }
                foreach (var idx in source.Indexes)
                {
                    var idxScript = await sourceProvider.GetIndexCreateScriptAsync(idx.Name);
                    if (!string.IsNullOrWhiteSpace(idxScript))
                        subs.Add(new("Indexes", ComparisonStatus.MissingInTarget, $"Index '{idx.Name}' missing in target", idxScript));
                }
                results.Add(new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Table,
                    Name = tableName,
                    Status = ComparisonStatus.MissingInTarget,
                    Details = "Exists in source, missing in target",
                    DiffScript = createScript,
                    SourceScript = createScript,
                    SubResults = subs
                });
                continue;
            }

            var subResults = new List<ComparisonSubResult>();
            var overallStatus = ComparisonStatus.Match;

            // First check: Use structural signature for semantic comparison (order-independent)
            if (!source.StructuralEquals(target))
            {
                overallStatus = ComparisonStatus.Mismatch;
                
                // Only add script comparison as supplementary info if structural differs
                if (!AreScriptsEqual(source.CreateScript, target.CreateScript, sourceDbKind, targetDbKind, options))
                {
                    subResults.Add(new ComparisonSubResult(
                        "StructuralDiff",
                        ComparisonStatus.Mismatch,
                        "Table structure differs (order-independent semantic comparison)",
                        $"-- SOURCE SIGNATURE\n{source.GetStructuralSignature()}\n\n-- TARGET SIGNATURE\n{target.GetStructuralSignature()}\n\n-- SOURCE SCRIPT\n{source.CreateScript}\n\n-- TARGET SCRIPT\n{target.CreateScript}"
                    ));
                }
            }

            overallStatus |= await ComparePrimaryKeysAsync(sourceProvider, targetProvider, source, target, subResults);
            overallStatus |= CompareColumns(source, target, subResults);
            overallStatus |= await CompareForeignKeysAsync(sourceProvider, targetProvider, source, target, subResults);
            overallStatus |= await CompareIndexesAsync(sourceProvider, targetProvider, source, target, subResults);
            overallStatus |= await CompareConstraintsAsync(sourceProvider, targetProvider, source, target, subResults);
            overallStatus |= CompareCheckConstraints(sourceSnapshot, targetSnapshot, source.Name, target.Name, subResults);

            var diffScript = BuildDiffScript(source, target, subResults, overallStatus);

            var res = new ComparisonResult
            {
                ObjectType = SchemaObjectType.Table,
                Name = source.Name,
                Status = overallStatus,
                DiffScript = diffScript,
                SubResults = subResults
            };

            // Enhance with side-by-side diff information
            EnhanceComparisonResultWithDiff(res, source.CreateScript, target.CreateScript, _currentOptions.SourceLabel, _currentOptions.TargetLabel);
            
            results.Add(res);
        }

        var sourceTableSet = new HashSet<string>(sourceTables, StringComparer.OrdinalIgnoreCase);
        foreach (var targetTable in targetTables.Where(t => !sourceTableSet.Contains(t)))
        {
            var result = await HandleTableMissingInSourceAsync(targetProvider, targetTable, targetSnapshot);
            results.Add(result);
        }
    }

    private async Task<ComparisonStatus> ComparePrimaryKeysAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        // Check if tables are materialized views (skip PK logic for matviews)
        var sourceIsMatView = IsMaterializedView(sourceProvider, source.Name);
        var targetIsMatView = IsMaterializedView(targetProvider, target.Name);
        
        if (sourceIsMatView || targetIsMatView)
        {
            // Skip PK comparison for materialized views
            return ComparisonStatus.Match;
        }

        var sourcePk = source.PrimaryKeys.FirstOrDefault();
        var targetPk = target.PrimaryKeys.FirstOrDefault();

        var sourceValid = sourcePk != null && IsValidPrimaryKey(sourcePk);
        var targetValid = targetPk != null && IsValidPrimaryKey(targetPk);

        if (!sourceValid && targetValid)
        {
            var script = await targetProvider.GetPrimaryKeyCreateScriptAsync(target.Name);
            subResults.Add(new("PrimaryKeys", ComparisonStatus.MissingInSource, 
                $"Primary key missing/invalid in source: columns ({string.Join(", ", targetPk!.Columns)})", script ?? ""));
            return ComparisonStatus.Mismatch;
        }
        else if (!targetValid && sourceValid)
        {
            var script = await sourceProvider.GetPrimaryKeyCreateScriptAsync(source.Name);
            subResults.Add(new("PrimaryKeys", ComparisonStatus.MissingInTarget, 
                $"Primary key missing/invalid in target: columns ({string.Join(", ", sourcePk!.Columns)})", script ?? ""));
            return ComparisonStatus.Mismatch;
        }
        else if (sourceValid && targetValid)
        {
            // Compare by structural definition (column list), not by name
            if (!sourcePk!.StructuralEquals(targetPk!))
            {
                var sourceScript = await sourceProvider.GetPrimaryKeyCreateScriptAsync(source.Name);
                var targetScript = await targetProvider.GetPrimaryKeyCreateScriptAsync(target.Name);
                subResults.Add(new("PrimaryKeys", ComparisonStatus.Mismatch, 
                    $"Primary key structure differs: source({string.Join(", ", sourcePk.Columns)}) vs target({string.Join(", ", targetPk.Columns)})", 
                    $"-- SOURCE PK\n{sourceScript}\n\n-- TARGET PK\n{targetScript}"));
                return ComparisonStatus.Mismatch;
            }
        }

        return ComparisonStatus.Match;
    }

    private ComparisonStatus CompareColumns(TableDefinition source, TableDefinition target, List<ComparisonSubResult> subResults)
    {
        var status = ComparisonStatus.Match;
        var sourceCols = source.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var targetCols = target.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var col in sourceCols.Values)
        {
            if (!targetCols.TryGetValue(col.Name, out var tgt))
            {
                status = ComparisonStatus.Mismatch;
                subResults.Add(new("Columns", ComparisonStatus.MissingInTarget, $"Column '{col.Name}' is missing in target.",
                    $"ALTER TABLE \"{source.Name}\" ADD COLUMN {RenderColumnDefinition(col)};"));
            }
            else if (!col.Equals(tgt))
            {
                status = ComparisonStatus.Mismatch;
                subResults.Add(new("Columns", ComparisonStatus.Mismatch,
                    $"Column '{col.Name}' differs — {DescribeColumnDifference(col, tgt)}",
                    BuildColumnAlterStatements(source.Name, col, tgt)));
            }
        }

        foreach (var col in targetCols.Values)
        {
            if (!sourceCols.ContainsKey(col.Name))
            {
                status = ComparisonStatus.Mismatch;
                subResults.Add(new("Columns", ComparisonStatus.MissingInSource,
                    $"Column '{col.Name}' is missing in source (may be deprecated).", string.Empty));
            }
        }

        return status;
    }

    /// <summary>
    /// Builds a full CREATE TABLE script from the source model: columns (with type/null/default),
    /// inline PRIMARY KEY and UNIQUE constraints. Used when a table is missing in the target so the
    /// sync generator has something to emit. FKs/standalone indexes are handled separately.
    /// </summary>
    internal static string BuildCreateTableScript(TableDefinition table)
    {
        var lines = new List<string>();
        foreach (var col in table.Columns)
            lines.Add(RenderColumnDefinition(col));

        foreach (var pk in table.PrimaryKeys.Where(p => p.Columns.Count > 0))
        {
            var name = string.IsNullOrWhiteSpace(pk.Name) ? $"pk_{table.Name}" : pk.Name;
            lines.Add($"CONSTRAINT \"{name}\" PRIMARY KEY ({string.Join(", ", pk.Columns.Select(c => $"\"{c}\""))})");
        }

        foreach (var uc in table.UniqueConstraints.Where(u => u.Columns.Count > 0))
        {
            var name = string.IsNullOrWhiteSpace(uc.Name) ? $"uq_{table.Name}" : uc.Name;
            lines.Add($"CONSTRAINT \"{name}\" UNIQUE ({string.Join(", ", uc.Columns.Select(c => $"\"{c}\""))})");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE IF NOT EXISTS \"{table.Name}\" (");
        for (int i = 0; i < lines.Count; i++)
            sb.AppendLine($"    {lines[i]}{(i < lines.Count - 1 ? "," : "")}");
        sb.Append(");");
        return sb.ToString();
    }

    /// <summary>
    /// Lists which column attributes actually differ. The old message only printed DataType,
    /// producing confusing "uuid vs uuid" lines when the real difference was nullability/default.
    /// </summary>
    internal static string DescribeColumnDifference(ColumnDefinition s, ColumnDefinition t)
    {
        var diffs = new List<string>();
        if (!string.Equals(s.DataType, t.DataType, StringComparison.OrdinalIgnoreCase))
            diffs.Add($"type: source={s.DataType} target={t.DataType}");
        if (s.IsNullable != t.IsNullable)
            diffs.Add($"nullable: source={(s.IsNullable ? "YES" : "NO")} target={(t.IsNullable ? "YES" : "NO")}");
        if (!string.Equals(s.DefaultValue ?? "", t.DefaultValue ?? "", StringComparison.OrdinalIgnoreCase))
            diffs.Add($"default: source={s.DefaultValue ?? "none"} target={t.DefaultValue ?? "none"}");
        if (s.Length != t.Length)
            diffs.Add($"length: source={s.Length?.ToString() ?? "-"} target={t.Length?.ToString() ?? "-"}");
        if (s.NumericPrecision != t.NumericPrecision || s.NumericScale != t.NumericScale)
            diffs.Add($"precision: source=({s.NumericPrecision},{s.NumericScale}) target=({t.NumericPrecision},{t.NumericScale})");
        if (s.IsIdentity != t.IsIdentity)
            diffs.Add($"identity: source={s.IsIdentity} target={t.IsIdentity}");
        if (s.IsComputed != t.IsComputed)
            diffs.Add($"computed: source={s.IsComputed} target={t.IsComputed}");
        return diffs.Count > 0 ? string.Join("; ", diffs) : "definition differs";
    }

    /// <summary>
    /// Builds the ALTER statements to make the target column match the source — one per differing
    /// attribute, newline-separated. The generator classifies each: SET DEFAULT / DROP DEFAULT /
    /// DROP NOT NULL are safe; TYPE change, backfill UPDATE and SET NOT NULL are risky.
    /// Replaces the old generic "TYPE &lt;new_type&gt;" template that never fixed nullability/default.
    /// </summary>
    internal static string BuildColumnAlterStatements(string table, ColumnDefinition src, ColumnDefinition tgt)
    {
        var stmts = new List<string>();
        var t = $"\"{table}\"";
        var c = $"\"{src.Name}\"";

        // Type/size change — RISKY (data must be castable).
        if (!string.Equals(src.DataType, tgt.DataType, StringComparison.OrdinalIgnoreCase)
            || src.Length != tgt.Length
            || src.NumericPrecision != tgt.NumericPrecision
            || src.NumericScale != tgt.NumericScale)
        {
            var newType = RenderColumnType(src);
            stmts.Add($"ALTER TABLE {t} ALTER COLUMN {c} TYPE {newType} USING {c}::text::{newType};");
        }

        // Default change — SAFE (metadata only).
        if (!string.Equals(src.DefaultValue ?? "", tgt.DefaultValue ?? "", StringComparison.OrdinalIgnoreCase))
            stmts.Add(string.IsNullOrWhiteSpace(src.DefaultValue)
                ? $"ALTER TABLE {t} ALTER COLUMN {c} DROP DEFAULT;"
                : $"ALTER TABLE {t} ALTER COLUMN {c} SET DEFAULT {src.DefaultValue};");

        // Nullability change.
        if (src.IsNullable != tgt.IsNullable)
        {
            if (!src.IsNullable)
            {
                // Tightening to NOT NULL — RISKY. Backfill existing NULLs first.
                if (!string.IsNullOrWhiteSpace(src.DefaultValue))
                    stmts.Add($"UPDATE {t} SET {c} = {src.DefaultValue} WHERE {c} IS NULL;");
                else
                    stmts.Add($"-- backfill required: {table}.{src.Name} has no default; populate NULLs before SET NOT NULL\nALTER TABLE {t} ALTER COLUMN {c} SET NOT NULL;");
                if (!string.IsNullOrWhiteSpace(src.DefaultValue))
                    stmts.Add($"ALTER TABLE {t} ALTER COLUMN {c} SET NOT NULL;");
            }
            else
            {
                // Relaxing to nullable — SAFE.
                stmts.Add($"ALTER TABLE {t} ALTER COLUMN {c} DROP NOT NULL;");
            }
        }

        return string.Join("\n", stmts);
    }

    /// <summary>
    /// Renders a full column definition for ADD COLUMN — type (with length/precision),
    /// nullability, and DEFAULT. Without this the generated DDL drops size/default and
    /// emits the literal "USER-DEFINED" for enum columns.
    /// </summary>
    internal static string RenderColumnDefinition(ColumnDefinition col)
    {
        var sb = new StringBuilder();
        sb.Append($"\"{col.Name}\" {RenderColumnType(col)}");
        if (!col.IsNullable) sb.Append(" NOT NULL");
        if (!string.IsNullOrWhiteSpace(col.DefaultValue)) sb.Append($" DEFAULT {col.DefaultValue}");
        return sb.ToString();
    }

    private static string RenderColumnType(ColumnDefinition col)
    {
        var dt = col.DataType;

        // Enum / composite: data_type is "USER-DEFINED", the real type name is in UdtName.
        if (string.Equals(dt, "USER-DEFINED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(col.UdtName))
            return $"\"{col.UdtName}\"";

        // Arrays: data_type is "ARRAY", UdtName is the element type prefixed with '_' (e.g. "_int4").
        if (string.Equals(dt, "ARRAY", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(col.UdtName))
            return $"{col.UdtName.TrimStart('_')}[]";

        // numeric(p,s) / decimal(p,s) — only these carry meaningful precision/scale.
        if ((string.Equals(dt, "numeric", StringComparison.OrdinalIgnoreCase)
             || string.Equals(dt, "decimal", StringComparison.OrdinalIgnoreCase))
            && col.NumericPrecision.HasValue)
            return col.NumericScale.HasValue
                ? $"{dt}({col.NumericPrecision},{col.NumericScale})"
                : $"{dt}({col.NumericPrecision})";

        // varchar(n) / char(n) / bit(n): character_maximum_length populated only for these.
        if (col.Length.HasValue)
            return $"{dt}({col.Length})";

        return dt;
    }

    private async Task<ComparisonStatus> CompareForeignKeysAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        var status = ComparisonStatus.Match;

        // Group FKs by structural key to avoid double-counting and enable semantic comparison
        var sourceFksByStructure = source.ForeignKeys
            .GroupBy(fk => fk.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var targetFksByStructure = target.ForeignKeys
            .GroupBy(fk => fk.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var allFkStructures = sourceFksByStructure.Keys.Union(targetFksByStructure.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var structuralKey in allFkStructures)
        {
            var inSource = sourceFksByStructure.TryGetValue(structuralKey, out var sourceFk);
            var inTarget = targetFksByStructure.TryGetValue(structuralKey, out var targetFk);

            if (inSource && inTarget)
            {
                // Already structurally equal by grouping key, but check for any additional differences
                if (!sourceFk!.StructuralEquals(targetFk!))
                {
                    var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk.Name);
                    subResults.Add(new("ForeignKeys", ComparisonStatus.Mismatch, 
                        $"Foreign key structure '{structuralKey}' has differences", script ?? ""));
                    status = ComparisonStatus.Mismatch;
                }
            }
            else if (inSource && !inTarget)
            {
                var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk!.Name);
                subResults.Add(new("ForeignKeys", ComparisonStatus.MissingInTarget, 
                    $"Foreign key '{structuralKey}' missing in target", script ?? ""));
                status = ComparisonStatus.Mismatch;
            }
            else if (!inSource && inTarget)
            {
                var script = await targetProvider.GetForeignKeyCreateScriptAsync(target.Name, targetFk!.Name);
                subResults.Add(new("ForeignKeys", ComparisonStatus.MissingInSource, 
                    $"Foreign key '{structuralKey}' missing in source", script ?? ""));
                status = ComparisonStatus.Mismatch;
            }
        }
        return status;
    }

    private async Task<ComparisonStatus> CompareIndexesAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        var status = ComparisonStatus.Match;
        
        // Invalid indexes are already excluded by indisvalid=true in the SQL query.
        // Apply in-memory filter only as a safeguard for empty column lists.
        var validSourceIndexes = source.Indexes.Where(IsValidIndex).ToList();
        var validTargetIndexes = target.Indexes.Where(IsValidIndex).ToList();

        // Group indexes by structural key to enable semantic comparison (method + columns + options + predicate)
        var sourceIndexesByStructure = validSourceIndexes
            .GroupBy(idx => idx.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var targetIndexesByStructure = validTargetIndexes
            .GroupBy(idx => idx.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var allIndexStructures = sourceIndexesByStructure.Keys.Union(targetIndexesByStructure.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var structuralKey in allIndexStructures)
        {
            var inSource = sourceIndexesByStructure.TryGetValue(structuralKey, out var sourceIndex);
            var inTarget = targetIndexesByStructure.TryGetValue(structuralKey, out var targetIndex);

            if (inSource && inTarget)
            {
                // Already structurally equal by grouping key, but verify
                if (!sourceIndex!.StructuralEquals(targetIndex!))
                {
                    var script = await sourceProvider.GetIndexCreateScriptAsync(sourceIndex.Name);
                    subResults.Add(new("Indexes", ComparisonStatus.Mismatch,
                        $"Index structure '{structuralKey}' has subtle differences", script ?? ""));
                    status = ComparisonStatus.Mismatch;
                }
            }
            else if (inSource && !inTarget)
            {
                var script = await sourceProvider.GetIndexCreateScriptAsync(sourceIndex!.Name);
                subResults.Add(new("Indexes", ComparisonStatus.MissingInTarget,
                    $"Index '{structuralKey}' is missing in target.", script ?? ""));
                status = ComparisonStatus.Mismatch;
            }
            else if (!inSource && inTarget)
            {
                var script = await targetProvider.GetIndexCreateScriptAsync(targetIndex!.Name);
                subResults.Add(new("Indexes", ComparisonStatus.MissingInSource,
                    $"Index '{structuralKey}' exists in target but not in source.",
                    $"-- Optionally drop: DROP INDEX IF EXISTS \"{targetIndex.Name}\";"));
                status = ComparisonStatus.Mismatch;
            }
        }

        return status;
    }

    private static async Task<ComparisonStatus> CompareConstraintsAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        var status = ComparisonStatus.Match;

        var sourceUcByStructure = source.UniqueConstraints
            .GroupBy(uc => uc.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var targetUcByStructure = target.UniqueConstraints
            .GroupBy(uc => uc.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var allKeys = sourceUcByStructure.Keys.Union(targetUcByStructure.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var key in allKeys)
        {
            var inSource = sourceUcByStructure.TryGetValue(key, out var sourceUc);
            var inTarget = targetUcByStructure.TryGetValue(key, out var targetUc);

            if (inSource && !inTarget)
            {
                var script = await sourceProvider.GetUniqueConstraintCreateScriptAsync(source.Name, sourceUc!.Name);
                subResults.Add(new("UniqueConstraints", ComparisonStatus.MissingInTarget,
                    $"Unique constraint on ({key}) missing in target", script ?? ""));
                status = ComparisonStatus.Mismatch;
            }
            else if (!inSource && inTarget)
            {
                var script = await targetProvider.GetUniqueConstraintCreateScriptAsync(target.Name, targetUc!.Name);
                subResults.Add(new("UniqueConstraints", ComparisonStatus.MissingInSource,
                    $"Unique constraint on ({key}) missing in source", script ?? ""));
                status = ComparisonStatus.Mismatch;
            }
        }

        return status;
    }

    private static ComparisonStatus CompareCheckConstraints(
        SchemaSnapshot? sourceSnapshot,
        SchemaSnapshot? targetSnapshot,
        string sourceTableName,
        string targetTableName,
        List<ComparisonSubResult> subResults)
    {
        if (sourceSnapshot == null || targetSnapshot == null)
            return ComparisonStatus.Match; // only available via snapshot path

        var sourceChecks = sourceSnapshot.CheckConstraintsByTable
            .GetValueOrDefault(sourceTableName) ?? [];
        var targetChecks = targetSnapshot.CheckConstraintsByTable
            .GetValueOrDefault(targetTableName) ?? [];

        var srcByKey = sourceChecks.ToDictionary(c => c.GetStructuralKey(), StringComparer.OrdinalIgnoreCase);
        var tgtByKey = targetChecks.ToDictionary(c => c.GetStructuralKey(), StringComparer.OrdinalIgnoreCase);
        var allKeys = srcByKey.Keys.Union(tgtByKey.Keys, StringComparer.OrdinalIgnoreCase);

        var status = ComparisonStatus.Match;
        foreach (var key in allKeys)
        {
            var inSrc = srcByKey.TryGetValue(key, out var srcCheck);
            var inTgt = tgtByKey.TryGetValue(key, out _);

            if (inSrc && !inTgt)
            {
                subResults.Add(new("CheckConstraints", ComparisonStatus.MissingInTarget,
                    $"Check constraint ({srcCheck!.Name}): {key} missing in target",
                    $"ALTER TABLE \"{sourceTableName}\" ADD CONSTRAINT \"{srcCheck.Name}\" {srcCheck.CheckClause};"));
                status = ComparisonStatus.Mismatch;
            }
            else if (!inSrc && inTgt)
            {
                subResults.Add(new("CheckConstraints", ComparisonStatus.MissingInSource,
                    $"Check constraint ({key}) missing in source", string.Empty));
                status = ComparisonStatus.Mismatch;
            }
        }
        return status;
    }

    private static void CompareSequences(
        IReadOnlyList<DbSequenceDefinition>? sourceSeqs,
        IReadOnlyList<DbSequenceDefinition>? targetSeqs,
        List<ComparisonResult> results)
    {
        if (sourceSeqs == null || targetSeqs == null) return;

        var srcByName = sourceSeqs.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        var tgtByName = targetSeqs.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        var allNames  = srcByName.Keys.Union(tgtByName.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var name in allNames)
        {
            var inSrc = srcByName.TryGetValue(name, out var src);
            var inTgt = tgtByName.TryGetValue(name, out var tgt);

            if (inSrc && !inTgt)
            {
                results.Add(CreateSequenceResult(name, ComparisonStatus.MissingInTarget,
                    $"CREATE SEQUENCE \"{name}\" INCREMENT BY {src!.IncrementBy} MINVALUE {src.MinValue} MAXVALUE {src.MaxValue} CACHE {src.CacheSize}{(src.Cycle ? " CYCLE" : "")};"));
            }
            else if (!inSrc && inTgt)
            {
                results.Add(CreateSequenceResult(name, ComparisonStatus.MissingInSource, string.Empty));
            }
            else if (inSrc && inTgt && !src!.StructuralEquals(tgt!))
            {
                var details = $"Sequence differs — source: inc={src.IncrementBy} min={src.MinValue} max={src.MaxValue} cache={src.CacheSize} cycle={src.Cycle} | target: inc={tgt!.IncrementBy} min={tgt.MinValue} max={tgt.MaxValue} cache={tgt.CacheSize} cycle={tgt.Cycle}";
                results.Add(CreateSequenceResult(name, ComparisonStatus.Mismatch, details));
            }
            else
            {
                results.Add(CreateSequenceResult(name, ComparisonStatus.Match, string.Empty));
            }
        }
    }

    private static void CompareEnums(
        IReadOnlyList<EnumTypeDefinition>? sourceEnums,
        IReadOnlyList<EnumTypeDefinition>? targetEnums,
        List<ComparisonResult> results)
    {
        if (sourceEnums == null || targetEnums == null) return;

        var srcByName = sourceEnums.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        var tgtByName = targetEnums.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        var allNames  = srcByName.Keys.Union(tgtByName.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var name in allNames)
        {
            var inSrc = srcByName.TryGetValue(name, out var src);
            var inTgt = tgtByName.TryGetValue(name, out var tgt);

            if (inSrc && !inTgt)
            {
                var createScript = $"CREATE TYPE \"{name}\" AS ENUM ({string.Join(", ", src!.Values.Select(v => $"'{v}'"))});";
                results.Add(new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Enum, Name = name,
                    Status = ComparisonStatus.MissingInTarget,
                    Details = $"Enum '{name}' missing in target. Values: {string.Join(", ", src.Values)}",
                    DiffScript = createScript,
                });
            }
            else if (!inSrc && inTgt)
            {
                results.Add(new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Enum, Name = name,
                    Status = ComparisonStatus.MissingInSource,
                    Details = $"Enum '{name}' missing in source.",
                });
            }
            else if (inSrc && inTgt && !src!.StructuralEquals(tgt!))
            {
                var srcValues = string.Join(", ", src.Values);
                var tgtValues = string.Join(", ", tgt!.Values);

                // Added values (safe: ALTER TYPE ... ADD VALUE)
                var added   = src.Values.Except(tgt.Values, StringComparer.Ordinal).ToList();
                // Removed values (destructive: requires recreating the type)
                var removed = tgt.Values.Except(src.Values, StringComparer.Ordinal).ToList();

                var addScripts  = added.Select(v => $"ALTER TYPE \"{name}\" ADD VALUE IF NOT EXISTS '{v}';");
                var diffScript  = removed.Any()
                    ? $"-- ⚠ Values removed from enum (requires DROP/RECREATE): {string.Join(", ", removed)}\n-- Added values:\n{string.Join("\n", addScripts)}"
                    : string.Join("\n", addScripts);

                results.Add(new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Enum, Name = name,
                    Status = ComparisonStatus.Mismatch,
                    Details = $"Enum values differ — source: [{srcValues}] | target: [{tgtValues}]",
                    DiffScript = diffScript,
                });
            }
            else
            {
                results.Add(new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Enum, Name = name,
                    Status = ComparisonStatus.Match,
                });
            }
        }
    }

    private static ComparisonResult CreateSequenceResult(string name, ComparisonStatus status, string detail) => new()
    {
        ObjectType = SchemaObjectType.Sequence,
        Name       = name,
        Status     = status,
        Details    = detail,
        DiffScript = status != ComparisonStatus.Match ? detail : null,
    };

    private string? BuildDiffScript(TableDefinition source, TableDefinition target, List<ComparisonSubResult> subResults, ComparisonStatus status)
    {
        if (status == ComparisonStatus.Match) return null;

        var sb = new StringBuilder();
        foreach (var sub in subResults.Where(s => !string.IsNullOrWhiteSpace(s.CreateScript)))
        {
            sb.AppendLine($"-- {sub.Component}: {sub.Status}");
            sb.AppendLine(sub.CreateScript!.Trim());
            sb.AppendLine();
        }

        if (sb.Length == 0)
        {
            sb.AppendLine("-- SOURCE");
            sb.AppendLine(source.CreateScript?.Trim());
            sb.AppendLine("-- TARGET");
            sb.AppendLine(target.CreateScript?.Trim());
        }

        return sb.ToString().Trim();
    }

    private void EnhanceComparisonResultWithDiff(ComparisonResult result, string? sourceScript, string? targetScript, string sourceLabel = "Source", string targetLabel = "Target")
    {
        result.SourceScript = sourceScript?.Trim();
        result.TargetScript = targetScript?.Trim();

        if (result.Status != ComparisonStatus.Match &&
            (!string.IsNullOrWhiteSpace(sourceScript) || !string.IsNullOrWhiteSpace(targetScript)))
        {
            var diffResult = _sqlDiffService.ComputeDiff(sourceScript, targetScript);
            if (diffResult.HasDifferences)
            {
                result.SideBySideDiffHtml = _sqlDiffService.GenerateSideBySideHtml(diffResult, sourceLabel, targetLabel);
            }
        }
    }

    private async Task<ComparisonResult> HandleTableMissingInSourceAsync(
        IDatabaseSchemaProvider provider, string tableName, SchemaSnapshot? snapshot = null)
    {
        var table = snapshot != null
            ? snapshot.GetTableDefinition(tableName)
            : await provider.GetTableDefinitionAsync(tableName);
        var subResults = new List<ComparisonSubResult>();

        if (!string.IsNullOrWhiteSpace(table.CreateScript))
            subResults.Add(new("CreateScript", ComparisonStatus.MissingInSource, "Create script missing in source", table.CreateScript));

        var pk = table.PrimaryKeys.FirstOrDefault();
        if (pk != null)
        {
            var pkScript = await provider.GetPrimaryKeyCreateScriptAsync(table.Name);
            subResults.Add(new("PrimaryKeys", ComparisonStatus.MissingInSource, "Primary key missing in source", pkScript));
        }

        foreach (var col in table.Columns)
        {
            subResults.Add(new("Columns", ComparisonStatus.MissingInSource,
                $"Column '{col.Name}' is missing in source",
                $"ALTER TABLE \"{table.Name}\" ADD COLUMN {RenderColumnDefinition(col)};"));
        }

        foreach (var fk in table.ForeignKeys)
        {
            var fkScript = await provider.GetForeignKeyCreateScriptAsync(table.Name, fk.Name);
            subResults.Add(new("ForeignKeys", ComparisonStatus.MissingInSource, $"Foreign key '{fk.Name}' is missing in source", fkScript));
        }

        foreach (var idx in table.Indexes)
        {
            var idxScript = await provider.GetIndexCreateScriptAsync(idx.Name);
            subResults.Add(new("Indexes", ComparisonStatus.MissingInSource, $"Index '{idx.Name}' is missing in source", idxScript));
        }

        return new ComparisonResult
        {
            ObjectType = SchemaObjectType.Table,
            Name = table.Name,
            Status = ComparisonStatus.MissingInSource,
            Details = "Exists in target, missing in source",
            DiffScript = BuildDiffScript(table, table, subResults, ComparisonStatus.MissingInSource),
            SubResults = subResults
        };
    }

    // --- FUNCTIONS ---
    // Uses Definition already in the snapshot — no per-function DB round-trips.
    private Task CompareFunctionsAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Dictionary<string, DbFunctionDefinition> sourceFunctions,
        Dictionary<string, DbFunctionDefinition> targetFunctionMap,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);
        int total = sourceFunctions.Count;
        int index = 0;

        foreach (var kvp in sourceFunctions)
        {
            index++;
            var source = kvp.Value;
            progressLogger?.Invoke(index, total, $"⚙️ Comparing function: {source.Name}", true);

            if (!targetFunctionMap.TryGetValue(kvp.Key, out var target))
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Function, ComparisonStatus.MissingInTarget, source.Definition));
                continue;
            }

            var sourceDef = source.Definition;
            var targetDef = target.Definition;

            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
            {
                var result = new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Function,
                    Name = source.Name,
                    Status = ComparisonStatus.Mismatch,
                    Details = "Function definition differs",
                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
                };
                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef, _currentOptions.SourceLabel, _currentOptions.TargetLabel);
                results.Add(result);
            }
            else
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Function, ComparisonStatus.Match));
            }
        }

        foreach (var kvp in targetFunctionMap.Where(kvp => !sourceFunctions.ContainsKey(kvp.Key)))
            results.Add(CreateResult(kvp.Value.Name, SchemaObjectType.Function, ComparisonStatus.MissingInSource, kvp.Value.Definition));

        return Task.CompletedTask;
    }

    // --- PROCEDURES ---
    private Task CompareProceduresAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Dictionary<string, DbFunctionDefinition> sourceProcedureMap,
        Dictionary<string, DbFunctionDefinition> targetProcedureMap,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);
        int total = sourceProcedureMap.Count;
        int index = 0;

        foreach (var kvp in sourceProcedureMap)
        {
            var source = kvp.Value;
            progressLogger?.Invoke(++index, total, $"🛠 Comparing procedure: {source.Name}", true);

            if (!targetProcedureMap.TryGetValue(kvp.Key, out var target))
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Procedure, ComparisonStatus.MissingInTarget, source.Definition));
                continue;
            }

            var sourceDef = source.Definition;
            var targetDef = target.Definition;

            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
            {
                var result = new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Procedure,
                    Name = source.Name,
                    Status = ComparisonStatus.Mismatch,
                    Details = "Procedure definition differs",
                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
                };
                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef, _currentOptions.SourceLabel, _currentOptions.TargetLabel);
                results.Add(result);
            }
            else
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Procedure, ComparisonStatus.Match));
            }
        }

        foreach (var kvp in targetProcedureMap.Where(kvp => !sourceProcedureMap.ContainsKey(kvp.Key)))
            results.Add(CreateResult(kvp.Value.Name, SchemaObjectType.Procedure, ComparisonStatus.MissingInSource, kvp.Value.Definition));

        return Task.CompletedTask;
    }

    // --- VIEWS --- uses snapshot Definition — no DB calls
    private Task CompareViewsAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Dictionary<string, DbViewDefinition> sourceViewMap,
        Dictionary<string, DbViewDefinition> targetViewMap,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);
        int total = sourceViewMap.Count;
        int index = 0;

        foreach (var kvp in sourceViewMap)
        {
            var source = kvp.Value;
            progressLogger?.Invoke(++index, total, $"🔍 Comparing view: {source.Name}", true);

            if (!targetViewMap.TryGetValue(kvp.Key, out var target))
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.View, ComparisonStatus.MissingInTarget, source.Definition));
                continue;
            }

            var sourceDef = source.Definition;
            var targetDef = target.Definition;

            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
            {
                var result = new ComparisonResult
                {
                    ObjectType = SchemaObjectType.View, Name = source.Name,
                    Status = ComparisonStatus.Mismatch, Details = "View definition differs",
                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
                };
                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef, _currentOptions.SourceLabel, _currentOptions.TargetLabel);
                results.Add(result);
            }
            else results.Add(CreateResult(source.Name, SchemaObjectType.View, ComparisonStatus.Match));
        }

        foreach (var kvp in targetViewMap.Where(kvp => !sourceViewMap.ContainsKey(kvp.Key)))
            results.Add(CreateResult(kvp.Value.Name, SchemaObjectType.View, ComparisonStatus.MissingInSource, kvp.Value.Definition));

        return Task.CompletedTask;
    }

    // --- TRIGGERS --- uses snapshot Definition — no DB calls
    private Task CompareTriggersAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Dictionary<string, DbTriggerDefinition> sourceTriggerMap,
        Dictionary<string, DbTriggerDefinition> targetTriggerMap,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);
        int total = sourceTriggerMap.Count;
        int index = 0;

        foreach (var kvp in sourceTriggerMap)
        {
            var source = kvp.Value;
            progressLogger?.Invoke(++index, total, $"⏰ Comparing trigger: {source.Name}", true);

            if (!targetTriggerMap.TryGetValue(kvp.Key, out var target))
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Trigger, ComparisonStatus.MissingInTarget, source.Definition ?? ""));
                continue;
            }

            var sourceDef = source.Definition;
            var targetDef = target.Definition;

            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
            {
                var result = new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Trigger,
                    Name = source.Name,
                    Status = ComparisonStatus.Mismatch,
                    Details = "Trigger definition differs",
                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
                };
                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef, _currentOptions.SourceLabel, _currentOptions.TargetLabel);
                results.Add(result);
            }
            else
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Trigger, ComparisonStatus.Match));
            }
        }

        foreach (var kvp in targetTriggerMap)
        {
            if (!sourceTriggerMap.ContainsKey(kvp.Key))
            {
                var target = kvp.Value;
                results.Add(CreateResult(target.Name, SchemaObjectType.Trigger, ComparisonStatus.MissingInSource, target.Definition ?? ""));
            }
        }

        return Task.CompletedTask;
    }

    private ComparisonResult CreateResult(string name, SchemaObjectType type, ComparisonStatus status, string diffScript = "")
    {
        var result = new ComparisonResult
        {
            ObjectType = type,
            Name = name,
            Status = status,
            DiffScript = diffScript
        };

        // For single-script objects (Functions, Procedures, Views, Triggers), the diffScript might be the definition
        // We need to handle side-by-side diff differently based on the status
        if (type == SchemaObjectType.Function || type == SchemaObjectType.Procedure || 
            type == SchemaObjectType.View || type == SchemaObjectType.Trigger)
        {
            if (status == ComparisonStatus.MissingInTarget)
            {
                // Source exists, target doesn't - show source vs empty
                EnhanceComparisonResultWithDiff(result, diffScript, null, _currentOptions.SourceLabel, _currentOptions.TargetLabel);
            }
            else if (status == ComparisonStatus.MissingInSource)
            {
                // Target exists, source doesn't - show empty vs target
                EnhanceComparisonResultWithDiff(result, null, diffScript, _currentOptions.SourceLabel, _currentOptions.TargetLabel);
            }
            // For Match status, no diff needed
            // For Mismatch, we need to get both source and target - handled elsewhere
        }

        return result;
    }

    private static bool AreScriptsEqual(string? sourceScript, string? targetScript, string sourceDbKind, string targetDbKind, ComparisonOptions options)
    {
        if (options.IgnoreOwnership)
        {
            var canonicalizedSource = DefinitionCanonicalizer.CanonicalizeDefinition(sourceScript, sourceDbKind, options);
            var canonicalizedTarget = DefinitionCanonicalizer.CanonicalizeDefinition(targetScript, targetDbKind, options);
            return string.Equals(canonicalizedSource, canonicalizedTarget, StringComparison.OrdinalIgnoreCase);
        }
        
        return string.Equals(sourceScript?.Trim(), targetScript?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // --- PROVIDER-SPECIFIC HELPERS ---

    private static string GetDbKind(IDatabaseSchemaProvider provider) =>
        provider.ProviderKind switch
        {
            DbProviderKind.Postgres => "postgres",
            DbProviderKind.MySql    => "mysql",
            _                      => "unknown"
        };

    private static bool IsValidPrimaryKey(PrimaryKeyDefinition pk) => pk.Columns.Any();

    // Invalid indexes (indisvalid=false) are already excluded by the SQL query in SchemaFetcher.
    // This helper exists only for any additional in-memory filtering needed in future.
    private static bool IsValidIndex(IndexDefinition index) => index.Columns.Any();

    private bool IsMaterializedView(IDatabaseSchemaProvider provider, string tableName)
    {
        var matViews = provider == _lastSourceProvider ? _sourceMatViews : _targetMatViews;
        return matViews.Contains(tableName);
    }
}

using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using System.Text;

namespace B2A.DbTula.Cli;

/// <summary>
/// Generates safe SQL sync scripts from comparison results, categorised into:
///   Safe        — additive changes with no data risk
///   Risky       — modifications that may affect data or require locking
///   Destructive — DROP statements, only emitted when explicitly enabled
///
/// Three key improvements borrowed from migra:
///   1. Enum rename-trick: changed enums use RENAME + RECAST instead of DROP/RECREATE
///   2. SET check_function_bodies = off before function changes
///   3. Topological sort (Kahn's algorithm) for CREATE TABLE order
/// </summary>
public class SyncScriptGenerator
{
    // ── Public API ────────────────────────────────────────────────────────────

    public SyncScript Generate(
        IList<ComparisonResult> results,
        SyncScriptOptions options,
        SchemaSnapshot? sourceSnapshot = null)
    {
        var script = new SyncScript
        {
            SourceLabel = options.SourceLabel,
            TargetLabel = options.TargetLabel,
            GeneratedAt = DateTimeOffset.UtcNow,
        };

        // ── SAFE: Sequences (may be referenced in column defaults)
        foreach (var r in MissingInTarget(results, SchemaObjectType.Sequence))
            script.Safe.Add(Stmt("Sequence", r.Name, r.DiffScript ?? "", "CREATE SEQUENCE missing in target"));

        // ── SAFE: Missing tables — topologically ordered by FK dependencies
        var missingTables = MissingInTarget(results, SchemaObjectType.Table).ToList();
        foreach (var r in TopologicallyOrdered(missingTables, sourceSnapshot))
        {
            var sql = BuildMissingTableSql(r);
            if (!string.IsNullOrWhiteSpace(sql))
                script.Safe.Add(Stmt("Table", r.Name, sql, "Table missing in target"));
        }

        // ── SAFE: Missing columns (nullable or with DEFAULT — additive)
        foreach (var r in Mismatched(results, SchemaObjectType.Table))
            foreach (var sub in MissingInTargetSubs(r, "Columns"))
                if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                    script.Safe.Add(Stmt("Column", r.Name, sub.CreateScript!, "Column missing in target"));

        // ── SAFE: Missing indexes
        foreach (var r in Mismatched(results, SchemaObjectType.Table))
            foreach (var sub in MissingInTargetSubs(r, "Indexes"))
                if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                    script.Safe.Add(Stmt("Index", r.Name, sub.CreateScript!, "Index missing in target"));

        // ── SAFE: Missing PKs
        foreach (var r in Mismatched(results, SchemaObjectType.Table))
            foreach (var sub in MissingInTargetSubs(r, "PrimaryKeys"))
                if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                    script.Safe.Add(Stmt("PrimaryKey", r.Name, sub.CreateScript!, "Primary key missing in target"));

        // ── SAFE: Missing FKs
        foreach (var r in Mismatched(results, SchemaObjectType.Table))
            foreach (var sub in MissingInTargetSubs(r, "ForeignKeys"))
                if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                    script.Safe.Add(Stmt("ForeignKey", r.Name, sub.CreateScript!, "Foreign key missing in target"));

        // ── SAFE: Missing unique constraints
        foreach (var r in Mismatched(results, SchemaObjectType.Table))
            foreach (var sub in MissingInTargetSubs(r, "UniqueConstraints"))
                if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                    script.Safe.Add(Stmt("UniqueConstraint", r.Name, sub.CreateScript!, "Unique constraint missing in target"));

        // ── SAFE: Missing check constraints
        foreach (var r in Mismatched(results, SchemaObjectType.Table))
            foreach (var sub in MissingInTargetSubs(r, "CheckConstraints"))
                if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                    script.Safe.Add(Stmt("CheckConstraint", r.Name, sub.CreateScript!, "Check constraint missing in target"));

        // ── SAFE: Enum values added (ALTER TYPE ... ADD VALUE IF NOT EXISTS)
        foreach (var r in MissingInTarget(results, SchemaObjectType.Enum))
            if (!string.IsNullOrWhiteSpace(r.DiffScript))
                script.Safe.Add(Stmt("Enum", r.Name, r.DiffScript!, "Enum type missing in target"));

        foreach (var r in Mismatched(results, SchemaObjectType.Enum))
            if (r.DiffScript != null && !r.DiffScript.Contains("RECREATE") && !r.DiffScript.Contains("RENAME"))
                script.Safe.Add(Stmt("Enum", r.Name, r.DiffScript!, "Enum values added"));

        // ── SAFE/RISKY: Functions and procedures
        // Borrowed from migra: SET check_function_bodies = off prevents Postgres from
        // validating function bodies when referenced types are being changed in the same script.
        var funcResults = results
            .Where(r => (r.ObjectType == SchemaObjectType.Function || r.ObjectType == SchemaObjectType.Procedure)
                     && (r.Status == ComparisonStatus.MissingInTarget || r.Status == ComparisonStatus.Mismatch))
            .ToList();

        if (funcResults.Any())
        {
            var funcSb = new StringBuilder();
            funcSb.AppendLine("SET check_function_bodies = off;");
            funcSb.AppendLine();
            foreach (var r in funcResults)
            {
                var body = r.SourceScript ?? r.DiffScript;
                if (!string.IsNullOrWhiteSpace(body))
                    funcSb.AppendLine(body.Trim()).AppendLine();
            }
            script.Safe.Add(Stmt("Functions", "(all changed)", funcSb.ToString().Trim(),
                "CREATE OR REPLACE FUNCTION/PROCEDURE — check_function_bodies disabled during migration"));
        }

        // ── SAFE: Views (CREATE OR REPLACE)
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.View
                     && (r.Status == ComparisonStatus.MissingInTarget || r.Status == ComparisonStatus.Mismatch)))
            if (!string.IsNullOrWhiteSpace(r.SourceScript))
                script.Safe.Add(Stmt("View", r.Name, r.SourceScript!, "View missing or changed in target"));

        // ── SAFE: Triggers
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Trigger
                     && (r.Status == ComparisonStatus.MissingInTarget || r.Status == ComparisonStatus.Mismatch)))
            if (!string.IsNullOrWhiteSpace(r.SourceScript))
                script.Safe.Add(Stmt("Trigger", r.Name, r.SourceScript!, "Trigger missing or changed in target"));

        // ── RISKY ─────────────────────────────────────────────────────────────
        if (options.IncludeRiskyChanges)
        {
            // Enum rename-trick (borrowed from migra) for changed enums where values were removed/reordered.
            // This avoids DROP TYPE which would fail while any column still references the type.
            // Steps: RENAME old → CREATE new → RECAST all columns → DROP old
            if (sourceSnapshot != null)
            {
                var changedEnumsNeedingRename = results
                    .Where(r => r.ObjectType == SchemaObjectType.Enum && r.Status == ComparisonStatus.Mismatch
                             && r.DiffScript != null && r.DiffScript.Contains("RECREATE"))
                    .ToList();

                foreach (var r in changedEnumsNeedingRename)
                {
                    var renameSql = BuildEnumRenameTrick(r.Name, sourceSnapshot);
                    if (!string.IsNullOrWhiteSpace(renameSql))
                        script.Risky.Add(Stmt("EnumRename", r.Name, renameSql,
                            "Enum values changed — uses rename-trick: old renamed, new created, columns recast, old dropped"));
                }
            }

            // Column type changes
            foreach (var r in Mismatched(results, SchemaObjectType.Table))
                foreach (var sub in r.SubResults.Where(s => s.Component == "Columns" && s.Status == ComparisonStatus.Mismatch))
                    script.Risky.Add(Stmt("ColumnTypeChange", r.Name,
                        $"-- ⚠ Verify data compatibility before running\n-- {sub.Details}\n-- ALTER TABLE \"{r.Name}\" ALTER COLUMN ... TYPE ...;",
                        sub.Details ?? "Column definition differs"));

            // Sequence definition changes
            foreach (var r in Mismatched(results, SchemaObjectType.Sequence))
                script.Risky.Add(Stmt("SequenceChange", r.Name,
                    $"-- ⚠ Review sequence change before applying\n-- {r.Details}",
                    r.Details ?? "Sequence definition differs"));

            // Indexes existing in target but not source (may want to DROP)
            foreach (var r in Mismatched(results, SchemaObjectType.Table))
                foreach (var sub in r.SubResults.Where(s => s.Component == "Indexes" && s.Status == ComparisonStatus.MissingInSource))
                    if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                        script.Risky.Add(Stmt("IndexDrop", r.Name, sub.CreateScript!,
                            "Index exists in target but not source"));
        }

        // ── DESTRUCTIVE ────────────────────────────────────────────────────────
        if (options.AllowDestructive)
        {
            foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Table && r.Status == ComparisonStatus.MissingInSource))
                script.Destructive.Add(Stmt("DropTable", r.Name,
                    $"DROP TABLE IF EXISTS \"{r.Name}\";", "Table exists in target but not source"));

            foreach (var r in Mismatched(results, SchemaObjectType.Table))
                foreach (var sub in r.SubResults.Where(s => s.Component == "Columns" && s.Status == ComparisonStatus.MissingInSource))
                    script.Destructive.Add(Stmt("DropColumn", r.Name,
                        $"-- Extract column name from: {sub.Details}\n-- ALTER TABLE \"{r.Name}\" DROP COLUMN IF EXISTS \"...\";",
                        sub.Details ?? "Column exists in target but not source"));
        }

        return script;
    }

    // ── Migra improvement #1: Enum rename-trick ───────────────────────────────
    //
    // When an enum has values removed or reordered, Postgres doesn't support ALTER TYPE RENAME VALUE.
    // The safe approach (from migra):
    //   1. ALTER TYPE old_name RENAME TO old_name__old_to_drop
    //   2. CREATE TYPE old_name AS ENUM (... new values ...)
    //   3. For each column using this enum:
    //      a. ALTER TABLE t ALTER COLUMN c DROP DEFAULT (if has default)
    //      b. ALTER TABLE t ALTER COLUMN c TYPE old_name USING c::text::old_name
    //      c. ALTER TABLE t ALTER COLUMN c SET DEFAULT '...' (restore)
    //   4. DROP TYPE old_name__old_to_drop
    //
    private static string? BuildEnumRenameTrick(string enumName, SchemaSnapshot sourceSnapshot)
    {
        // Find the new enum definition from snapshot
        var enumDef = sourceSnapshot.Enums.FirstOrDefault(e =>
            string.Equals(e.Name, enumName, StringComparison.OrdinalIgnoreCase));
        if (enumDef == null) return null;

        // Find all columns across all tables that use this enum type
        var affectedColumns = new List<(string Table, string Column, string? DefaultValue)>();
        foreach (var (tableName, columns) in sourceSnapshot.ColumnsByTable)
        {
            foreach (var col in columns.Where(c => c.IsEnum &&
                string.Equals(c.UdtName, enumName, StringComparison.OrdinalIgnoreCase)))
            {
                affectedColumns.Add((tableName, col.Name, col.DefaultValue));
            }
        }

        var suffix = "__old_version_to_be_dropped";
        var oldName = $"{enumName}{suffix}";
        var newValues = string.Join(", ", enumDef.Values.Select(v => $"'{v}'"));

        var sb = new StringBuilder();
        sb.AppendLine($"-- ⚠ Enum '{enumName}' changed — using rename-trick to avoid DROP failures");
        sb.AppendLine($"-- Step 1: Rename old enum so the name is free");
        sb.AppendLine($"ALTER TYPE \"{enumName}\" RENAME TO \"{oldName}\";");
        sb.AppendLine();
        sb.AppendLine($"-- Step 2: Create new enum with updated values");
        sb.AppendLine($"CREATE TYPE \"{enumName}\" AS ENUM ({newValues});");
        sb.AppendLine();

        if (affectedColumns.Any())
        {
            sb.AppendLine("-- Step 3: Recast all columns that use this enum type");
            foreach (var (table, column, defaultValue) in affectedColumns)
            {
                if (!string.IsNullOrWhiteSpace(defaultValue))
                    sb.AppendLine($"ALTER TABLE \"{table}\" ALTER COLUMN \"{column}\" DROP DEFAULT;");

                sb.AppendLine($"ALTER TABLE \"{table}\" ALTER COLUMN \"{column}\" TYPE \"{enumName}\"");
                sb.AppendLine($"    USING \"{column}\"::text::\"{enumName}\";");

                if (!string.IsNullOrWhiteSpace(defaultValue))
                    sb.AppendLine($"ALTER TABLE \"{table}\" ALTER COLUMN \"{column}\" SET DEFAULT {defaultValue};");

                sb.AppendLine();
            }
        }

        sb.AppendLine($"-- Step 4: Drop the old renamed enum");
        sb.AppendLine($"DROP TYPE \"{oldName}\";");

        return sb.ToString().Trim();
    }

    // ── Migra improvement #3: Topological sort for CREATE TABLE order ─────────
    //
    // Uses Kahn's algorithm on FK relationships.
    // If table B has a FK to table A, A must be created first.
    // Circular FKs (deferred constraints) fall back to their original order.
    //
    private static IEnumerable<ComparisonResult> TopologicallyOrdered(
        IList<ComparisonResult> missingTables,
        SchemaSnapshot? snapshot)
    {
        if (snapshot == null || missingTables.Count <= 1)
            return missingTables;

        var tableNames = missingTables.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Build adjacency: table → tables it depends on (via FKs)
        var dependsOn = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in missingTables)
            dependsOn[r.Name] = [];

        foreach (var r in missingTables)
        {
            var fks = snapshot.ForeignKeysByTable.GetValueOrDefault(r.Name) ?? [];
            foreach (var fk in fks)
            {
                if (tableNames.Contains(fk.ReferencedTable) &&
                    !string.Equals(fk.ReferencedTable, r.Name, StringComparison.OrdinalIgnoreCase))
                {
                    dependsOn[r.Name].Add(fk.ReferencedTable);
                }
            }
        }

        // Kahn's algorithm
        var inDegree = missingTables.ToDictionary(r => r.Name, r => 0, StringComparer.OrdinalIgnoreCase);
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in missingTables) dependents[r.Name] = [];

        foreach (var (table, deps) in dependsOn)
            foreach (var dep in deps)
            {
                inDegree[table]++;
                dependents[dep].Add(table);
            }

        var queue = new Queue<string>(missingTables
            .Where(r => inDegree[r.Name] == 0)
            .Select(r => r.Name));

        var ordered = new List<string>();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            ordered.Add(current);
            foreach (var dependent in dependents[current])
            {
                if (--inDegree[dependent] == 0)
                    queue.Enqueue(dependent);
            }
        }

        // Circular FK fallback: append any tables not yet sorted
        var remaining = missingTables.Select(r => r.Name)
            .Except(ordered, StringComparer.OrdinalIgnoreCase).ToList();
        if (remaining.Any())
        {
            ordered.AddRange(remaining);
        }

        var byName = missingTables.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        return ordered.Where(n => byName.ContainsKey(n)).Select(n => byName[n]);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    public string Render(SyncScript script, SyncScriptOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine($@"-- ============================================================
-- dbtula Sync Script
-- Source: {script.SourceLabel}
-- Target: {script.TargetLabel}
-- Generated: {script.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC
-- ⚠  REVIEW EVERY STATEMENT BEFORE EXECUTING ON PRODUCTION
-- ============================================================
");
        AppendSection(sb, "SAFE CHANGES (additive / non-destructive)", script.Safe,
            "These statements add missing objects. Safe to apply after review.");

        if (options.IncludeRiskyChanges)
            AppendSection(sb, "RISKY CHANGES (modifications — test on a copy first)", script.Risky,
                "⚠  Run in a transaction. Verify with SELECT COUNT(*) before committing.");

        if (options.AllowDestructive)
            AppendSection(sb, "DESTRUCTIVE CHANGES (data loss possible)", script.Destructive,
                "🚨 These statements DROP objects. Irreversible. Back up first.");

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string title, List<SyncStatement> stmts, string description)
    {
        sb.AppendLine($"-- ============================================================");
        sb.AppendLine($"-- SECTION: {title}");
        sb.AppendLine($"-- {description}");
        sb.AppendLine($"-- ============================================================");
        sb.AppendLine();

        if (!stmts.Any())
        {
            sb.AppendLine("-- (none)");
            sb.AppendLine();
            return;
        }

        foreach (var stmt in stmts)
        {
            sb.AppendLine($"-- [{stmt.ObjectType}: {stmt.ObjectName}] {stmt.Comment}");
            sb.AppendLine(stmt.Sql.Trim());
            sb.AppendLine();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SyncStatement Stmt(string type, string name, string sql, string comment) =>
        new(type, name, sql, comment);

    private static IEnumerable<ComparisonResult> MissingInTarget(IList<ComparisonResult> results, SchemaObjectType type) =>
        results.Where(r => r.ObjectType == type && r.Status == ComparisonStatus.MissingInTarget);

    private static IEnumerable<ComparisonResult> Mismatched(IList<ComparisonResult> results, SchemaObjectType type) =>
        results.Where(r => r.ObjectType == type && r.Status == ComparisonStatus.Mismatch);

    private static IEnumerable<ComparisonSubResult> MissingInTargetSubs(ComparisonResult r, string component) =>
        r.SubResults.Where(s => s.Component == component && s.Status == ComparisonStatus.MissingInTarget);

    private static string? BuildMissingTableSql(ComparisonResult r)
    {
        var createSub = r.SubResults.FirstOrDefault(s => s.Component == "CreateScript");
        if (createSub != null && !string.IsNullOrWhiteSpace(createSub.CreateScript))
            return createSub.CreateScript;

        var cols = r.SubResults
            .Where(s => s.Component == "Columns" && !string.IsNullOrWhiteSpace(s.CreateScript))
            .Select(s => s.CreateScript!
                .Replace($"ALTER TABLE \"{r.Name}\" ADD COLUMN ", "")
                .TrimEnd(';').Trim())
            .ToList();

        if (!cols.Any()) return null;

        var sb = new StringBuilder();
        sb.AppendLine($"-- ⚠ Simplified CREATE — review before executing");
        sb.AppendLine($"CREATE TABLE \"{r.Name}\" (");
        for (int i = 0; i < cols.Count; i++)
        {
            sb.Append($"    {cols[i]}");
            sb.AppendLine(i < cols.Count - 1 ? "," : "");
        }
        sb.AppendLine(");");
        return sb.ToString();
    }
}

// ── Supporting types ──────────────────────────────────────────────────────────

public class SyncScriptOptions
{
    public bool IncludeRiskyChanges { get; set; } = false;
    public bool AllowDestructive    { get; set; } = false;
    public string SourceLabel       { get; set; } = "Source";
    public string TargetLabel       { get; set; } = "Target";

    public static SyncScriptOptions SafeOnly   => new();
    public static SyncScriptOptions WithRisky  => new() { IncludeRiskyChanges = true };
    public static SyncScriptOptions Full       => new() { IncludeRiskyChanges = true, AllowDestructive = true };
}

public class SyncScript
{
    public string SourceLabel { get; set; } = "Source";
    public string TargetLabel { get; set; } = "Target";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<SyncStatement> Safe        { get; } = [];
    public List<SyncStatement> Risky       { get; } = [];
    public List<SyncStatement> Destructive { get; } = [];

    public bool HasAnySafeChanges        => Safe.Any();
    public bool HasAnyRiskyChanges       => Risky.Any();
    public bool HasAnyDestructiveChanges => Destructive.Any();
    public bool IsClean                  => !Safe.Any() && !Risky.Any() && !Destructive.Any();
}

public record SyncStatement(string ObjectType, string ObjectName, string Sql, string Comment);

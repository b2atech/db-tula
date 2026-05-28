using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using System.Text;

namespace B2A.DbTula.Cli;

/// <summary>
/// Generates safe SQL sync scripts from comparison results, categorised into:
///   Safe      — additive changes that can be applied without risk
///   Risky     — modifications that may affect data or require locking
///   Destructive — DROP statements, only emitted when explicitly enabled
///
/// Script sections are ordered to respect dependency constraints
/// (sequences → tables → columns → indexes → PKs → FKs → checks → functions → views → triggers).
/// </summary>
public class SyncScriptGenerator
{
    public SyncScript Generate(IList<ComparisonResult> results, SyncScriptOptions options)
    {
        var script = new SyncScript
        {
            SourceLabel = options.SourceLabel,
            TargetLabel = options.TargetLabel,
            GeneratedAt = DateTimeOffset.UtcNow,
        };

        // ── SAFE CHANGES ─────────────────────────────────────────────────────
        // Sequences first (may be referenced in column defaults)
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Sequence && r.Status == ComparisonStatus.MissingInTarget))
            script.Safe.Add(new SyncStatement("Sequence", r.Name, r.DiffScript ?? "", "CREATE SEQUENCE missing in target"));

        // Missing tables (topological ordering is approximate here — full topo sort is Phase 5 enhancement)
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Table && r.Status == ComparisonStatus.MissingInTarget))
        {
            var subScript = BuildMissingTableScript(r);
            if (!string.IsNullOrWhiteSpace(subScript))
                script.Safe.Add(new SyncStatement("Table", r.Name, subScript, "Table missing in target"));
        }

        // Missing columns (additive only)
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Table && r.Status == ComparisonStatus.Mismatch))
        {
            foreach (var sub in r.SubResults.Where(s => s.Component == "Columns" && s.Status == ComparisonStatus.MissingInTarget))
            {
                if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                    script.Safe.Add(new SyncStatement("Column", $"{r.Name}.{sub.Details}", sub.CreateScript!, "Column missing in target"));
            }
        }

        // Missing indexes
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Table && r.Status == ComparisonStatus.Mismatch))
        {
            foreach (var sub in r.SubResults.Where(s => s.Component == "Indexes" && s.Status == ComparisonStatus.MissingInTarget))
            {
                if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                    script.Safe.Add(new SyncStatement("Index", r.Name, sub.CreateScript!, "Index missing in target"));
            }
        }

        // Missing PKs (these are safe to add if columns exist)
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Table && r.Status == ComparisonStatus.Mismatch))
        {
            foreach (var sub in r.SubResults.Where(s => s.Component == "PrimaryKeys" && s.Status == ComparisonStatus.MissingInTarget))
            {
                if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                    script.Safe.Add(new SyncStatement("PrimaryKey", r.Name, sub.CreateScript!, "Primary key missing in target"));
            }
        }

        // Missing FKs
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Table && r.Status == ComparisonStatus.Mismatch))
        {
            foreach (var sub in r.SubResults.Where(s => s.Component == "ForeignKeys" && s.Status == ComparisonStatus.MissingInTarget))
            {
                if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                    script.Safe.Add(new SyncStatement("ForeignKey", r.Name, sub.CreateScript!, "Foreign key missing in target"));
            }
        }

        // Missing unique constraints
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Table && r.Status == ComparisonStatus.Mismatch))
        {
            foreach (var sub in r.SubResults.Where(s => s.Component == "UniqueConstraints" && s.Status == ComparisonStatus.MissingInTarget))
            {
                if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                    script.Safe.Add(new SyncStatement("UniqueConstraint", r.Name, sub.CreateScript!, "Unique constraint missing in target"));
            }
        }

        // Missing check constraints
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Table && r.Status == ComparisonStatus.Mismatch))
        {
            foreach (var sub in r.SubResults.Where(s => s.Component == "CheckConstraints" && s.Status == ComparisonStatus.MissingInTarget))
            {
                if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                    script.Safe.Add(new SyncStatement("CheckConstraint", r.Name, sub.CreateScript!, "Check constraint missing in target"));
            }
        }

        // Functions and procedures (CREATE OR REPLACE is safe)
        foreach (var r in results.Where(r =>
            (r.ObjectType == SchemaObjectType.Function || r.ObjectType == SchemaObjectType.Procedure)
            && (r.Status == ComparisonStatus.MissingInTarget || r.Status == ComparisonStatus.Mismatch)))
        {
            if (!string.IsNullOrWhiteSpace(r.SourceScript))
                script.Safe.Add(new SyncStatement(r.ObjectType.ToString(), r.Name, r.SourceScript!, $"{r.ObjectType} missing or changed in target"));
            else if (!string.IsNullOrWhiteSpace(r.DiffScript))
                script.Safe.Add(new SyncStatement(r.ObjectType.ToString(), r.Name, r.DiffScript!, $"{r.ObjectType} missing or changed in target"));
        }

        // Views (CREATE OR REPLACE is safe)
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.View
            && (r.Status == ComparisonStatus.MissingInTarget || r.Status == ComparisonStatus.Mismatch)))
        {
            if (!string.IsNullOrWhiteSpace(r.SourceScript))
                script.Safe.Add(new SyncStatement("View", r.Name, r.SourceScript!, "View missing or changed in target"));
        }

        // Missing enum types
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Enum && r.Status == ComparisonStatus.MissingInTarget))
        {
            if (!string.IsNullOrWhiteSpace(r.DiffScript))
                script.Safe.Add(new SyncStatement("Enum", r.Name, r.DiffScript!, "Enum type missing in target"));
        }

        // Enum values added (ALTER TYPE ... ADD VALUE IF NOT EXISTS is safe)
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Enum && r.Status == ComparisonStatus.Mismatch))
        {
            if (r.DiffScript != null && !r.DiffScript.Contains("DROP") && !r.DiffScript.Contains("RECREATE"))
                script.Safe.Add(new SyncStatement("Enum", r.Name, r.DiffScript!, "Enum values added in source"));
        }

        // Triggers (CREATE OR REPLACE)
        foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Trigger
            && (r.Status == ComparisonStatus.MissingInTarget || r.Status == ComparisonStatus.Mismatch)))
        {
            if (!string.IsNullOrWhiteSpace(r.SourceScript))
                script.Safe.Add(new SyncStatement("Trigger", r.Name, r.SourceScript!, "Trigger missing or changed in target"));
        }

        // ── RISKY CHANGES ─────────────────────────────────────────────────────
        if (options.IncludeRiskyChanges)
        {
            // Column type changes
            foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Table && r.Status == ComparisonStatus.Mismatch))
            {
                foreach (var sub in r.SubResults.Where(s => s.Component == "Columns" && s.Status == ComparisonStatus.Mismatch))
                {
                    var alterScript = $"-- ⚠ Verify data compatibility before running\n-- ALTER TABLE \"{r.Name}\" ALTER COLUMN ... TYPE ...;  -- details: {sub.Details}";
                    script.Risky.Add(new SyncStatement("ColumnTypeChange", r.Name, alterScript, sub.Details ?? "Column definition differs"));
                }
            }

            // Sequence definition changes
            foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Sequence && r.Status == ComparisonStatus.Mismatch))
            {
                var alterScript = $"-- ⚠ Review before applying\n-- {r.Details}";
                script.Risky.Add(new SyncStatement("SequenceChange", r.Name, alterScript, r.Details ?? "Sequence definition differs"));
            }

            // Enum values removed (requires DROP TYPE + RECREATE)
            foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Enum && r.Status == ComparisonStatus.Mismatch))
            {
                if (r.DiffScript != null && r.DiffScript.Contains("RECREATE"))
                    script.Risky.Add(new SyncStatement("EnumRecreate", r.Name, r.DiffScript!, "Enum values removed — requires DROP/RECREATE"));
            }

            // Missing indexes in source that exist in target (may want to drop from target)
            foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Table && r.Status == ComparisonStatus.Mismatch))
            {
                foreach (var sub in r.SubResults.Where(s => s.Component == "Indexes" && s.Status == ComparisonStatus.MissingInSource))
                {
                    if (!string.IsNullOrWhiteSpace(sub.CreateScript))
                        script.Risky.Add(new SyncStatement("IndexDrop", r.Name, sub.CreateScript!, "Index exists in target but not source"));
                }
            }
        }

        // ── DESTRUCTIVE CHANGES ────────────────────────────────────────────────
        if (options.AllowDestructive)
        {
            // Drop tables missing in source
            foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Table && r.Status == ComparisonStatus.MissingInSource))
                script.Destructive.Add(new SyncStatement("DropTable", r.Name,
                    $"DROP TABLE IF EXISTS \"{r.Name}\";", "Table exists in target but not source"));

            // Drop columns missing in source
            foreach (var r in results.Where(r => r.ObjectType == SchemaObjectType.Table && r.Status == ComparisonStatus.Mismatch))
            {
                foreach (var sub in r.SubResults.Where(s => s.Component == "Columns" && s.Status == ComparisonStatus.MissingInSource))
                    script.Destructive.Add(new SyncStatement("DropColumn", $"{r.Name}",
                        $"-- ⚠ Column name from: {sub.Details}\n-- ALTER TABLE \"{r.Name}\" DROP COLUMN IF EXISTS \"...\";",
                        sub.Details ?? "Column exists in target but not source"));
            }
        }

        return script;
    }

    private static string? BuildMissingTableScript(ComparisonResult tableResult)
    {
        // Pull the create script from sub-results if available
        var createSub = tableResult.SubResults.FirstOrDefault(s => s.Component == "CreateScript");
        if (createSub != null && !string.IsNullOrWhiteSpace(createSub.CreateScript))
            return createSub.CreateScript;

        // Fallback: build a minimal CREATE from column sub-results
        var columns = tableResult.SubResults
            .Where(s => s.Component == "Columns" && !string.IsNullOrWhiteSpace(s.CreateScript))
            .Select(s => s.CreateScript!)
            .ToList();

        if (!columns.Any()) return null;

        var sb = new StringBuilder();
        sb.AppendLine($"-- ⚠ Simplified CREATE — review before executing");
        sb.AppendLine($"CREATE TABLE \"{tableResult.Name}\" (");
        for (int i = 0; i < columns.Count; i++)
        {
            // Each column script is "ALTER TABLE ... ADD COLUMN ..." — extract the column definition
            var colDef = columns[i]
                .Replace($"ALTER TABLE \"{tableResult.Name}\" ADD COLUMN ", "")
                .TrimEnd(';').Trim();
            sb.Append($"    {colDef}");
            sb.AppendLine(i < columns.Count - 1 ? "," : "");
        }
        sb.AppendLine(");");
        return sb.ToString();
    }

    public string Render(SyncScript script, SyncScriptOptions options)
    {
        var sb = new StringBuilder();
        var header = $@"-- ============================================================
-- dbtula Sync Script
-- Source: {script.SourceLabel}
-- Target: {script.TargetLabel}
-- Generated: {script.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC
-- ⚠  REVIEW EVERY STATEMENT BEFORE EXECUTING ON PRODUCTION
-- ============================================================
";
        sb.AppendLine(header);

        AppendSection(sb, "SAFE CHANGES (additive, non-destructive)", script.Safe,
            "These statements add missing objects. Safe to apply after review.");

        if (options.IncludeRiskyChanges)
            AppendSection(sb, "RISKY CHANGES (modifications — test on a copy first)", script.Risky,
                "⚠  Run in a transaction. Verify with SELECT COUNT(*) before committing.");

        if (options.AllowDestructive)
            AppendSection(sb, "DESTRUCTIVE CHANGES (data loss possible)", script.Destructive,
                "🚨 These statements DROP objects. Irreversible. Back up first.");

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string title, List<SyncStatement> statements, string description)
    {
        sb.AppendLine($"-- ============================================================");
        sb.AppendLine($"-- SECTION: {title}");
        sb.AppendLine($"-- {description}");
        sb.AppendLine($"-- ============================================================");
        sb.AppendLine();

        if (!statements.Any())
        {
            sb.AppendLine("-- (none)");
            sb.AppendLine();
            return;
        }

        foreach (var stmt in statements)
        {
            sb.AppendLine($"-- [{stmt.ObjectType}: {stmt.ObjectName}] {stmt.Comment}");
            sb.AppendLine(stmt.Sql.Trim());
            sb.AppendLine();
        }
    }
}

public class SyncScriptOptions
{
    public bool IncludeRiskyChanges { get; set; } = false;
    public bool AllowDestructive    { get; set; } = false;
    public string SourceLabel       { get; set; } = "Source";
    public string TargetLabel       { get; set; } = "Target";

    public static SyncScriptOptions SafeOnly => new();
    public static SyncScriptOptions WithRisky => new() { IncludeRiskyChanges = true };
    public static SyncScriptOptions Full => new() { IncludeRiskyChanges = true, AllowDestructive = true };
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

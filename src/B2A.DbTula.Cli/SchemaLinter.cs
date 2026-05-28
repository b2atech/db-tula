using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;

namespace B2A.DbTula.Cli;

/// <summary>
/// Assigns Atlas-style named lint codes to comparison results after comparison.
///
/// Code families (matching Atlas sql/sqlcheck):
///   DS = Destructive changes     — high risk, data loss possible
///   MF = Data-dependent changes  — may fail if table/column has data
///   CD = Constraint drops        — may break application integrity
///   BC = Backwards incompatible  — breaks existing app code without migration
///
/// Applied after SchemaComparer.CompareAsync() completes.
/// </summary>
public static class SchemaLinter
{
    // ── Destructive ────────────────────────────────────────────────────────────
    // DS102: Dropping table (equivalent to Atlas DS102)
    // DS103: Dropping non-virtual column
    private const string DS102 = "DS102";
    private const string DS103 = "DS103";

    // ── Data-dependent ─────────────────────────────────────────────────────────
    // MF101: Adding unique index to existing table (may fail on duplicate data)
    // MF103: Adding NOT NULL column to existing table (may fail if table has rows)
    // MF104: Modifying nullable column to NOT NULL (may fail if column has NULLs)
    private const string MF101 = "MF101";
    private const string MF103 = "MF103";
    private const string MF104 = "MF104";

    // ── Constraint drops ────────────────────────────────────────────────────────
    // CD101: Dropping a foreign-key constraint
    private const string CD101 = "CD101";

    // ── Backwards incompatible ──────────────────────────────────────────────────
    // BC101: Renaming a table (breaks existing queries / app references)
    // BC102: Renaming a column
    private const string BC101 = "BC101";
    private const string BC102 = "BC102";

    public static void Annotate(IList<ComparisonResult> results)
    {
        foreach (var result in results)
        {
            switch (result.ObjectType)
            {
                case SchemaObjectType.Table:
                    AnnotateTable(result);
                    break;

                case SchemaObjectType.Sequence:
                case SchemaObjectType.Function:
                case SchemaObjectType.Procedure:
                case SchemaObjectType.View:
                case SchemaObjectType.Trigger:
                case SchemaObjectType.Enum:
                    AnnotateGenericObject(result);
                    break;
            }
        }
    }

    private static void AnnotateTable(ComparisonResult result)
    {
        // DS102: table missing in target = being dropped from target
        if (result.Status == ComparisonStatus.MissingInSource)
        {
            SetLint(result, DS102, LintSeverity.Error,
                $"DS102: Dropping table \"{result.Name}\" — data loss if table has rows");
            return;
        }

        if (result.Status != ComparisonStatus.Mismatch) return;

        // Examine sub-results for column-level issues
        foreach (var sub in result.SubResults)
        {
            if (sub.Component == "Columns")
            {
                // DS103: column being dropped (exists in target, missing in source)
                if (sub.Status == ComparisonStatus.MissingInSource)
                {
                    EscalateIfHigher(result, DS103, LintSeverity.Error,
                        $"DS103: {sub.Details} — column drop causes data loss");
                }

                // MF103: adding NOT NULL column without default to existing table
                if (sub.Status == ComparisonStatus.MissingInTarget
                    && sub.CreateScript != null
                    && sub.CreateScript.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase)
                    && !sub.CreateScript.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase))
                {
                    EscalateIfHigher(result, MF103, LintSeverity.Warning,
                        $"MF103: {sub.Details} — adding NOT NULL column without DEFAULT will fail on non-empty table");
                }

                // MF104: modifying column to NOT NULL
                if (sub.Status == ComparisonStatus.Mismatch
                    && sub.Details != null
                    && sub.Details.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase))
                {
                    EscalateIfHigher(result, MF104, LintSeverity.Warning,
                        $"MF104: {sub.Details} — modifying to NOT NULL may fail if column contains NULLs");
                }
            }

            // CD101: foreign key being dropped
            if (sub.Component == "ForeignKeys" && sub.Status == ComparisonStatus.MissingInSource)
            {
                EscalateIfHigher(result, CD101, LintSeverity.Warning,
                    $"CD101: {sub.Details} — dropping FK constraint may break referential integrity");
            }

            // MF101: unique index added to existing table
            if (sub.Component == "Indexes"
                && sub.Status == ComparisonStatus.MissingInTarget
                && sub.CreateScript != null
                && sub.CreateScript.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
            {
                EscalateIfHigher(result, MF101, LintSeverity.Warning,
                    $"MF101: {sub.Details} — adding unique index may fail if table has duplicate values");
            }
        }
    }

    private static void AnnotateGenericObject(ComparisonResult result)
    {
        // Objects missing in source (exist in target only) = being effectively removed from source environment
        if (result.Status == ComparisonStatus.MissingInSource && result.ObjectType == SchemaObjectType.Table)
        {
            SetLint(result, DS102, LintSeverity.Error,
                $"DS102: Dropping {result.ObjectType} \"{result.Name}\"");
        }
    }

    private static void SetLint(ComparisonResult result, string code, LintSeverity severity, string message)
    {
        result.LintCode    = code;
        result.Severity    = severity;
        result.LintMessage = message;
    }

    private static void EscalateIfHigher(ComparisonResult result, string code, LintSeverity severity, string message)
    {
        if (severity > result.Severity)
        {
            result.LintCode    = code;
            result.Severity    = severity;
            result.LintMessage = message;
        }
    }
}

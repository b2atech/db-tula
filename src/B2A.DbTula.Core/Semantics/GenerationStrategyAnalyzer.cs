using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;

namespace B2A.DbTula.Core.Semantics;

/// <summary>
/// Semantic analyzer for integer value-generation drift (IDENTITY / SERIAL / sequence
/// defaults). Turns the opaque "identity: source=True target=False" diff into a
/// classified verdict — Cosmetic / Metadata / Functional / Data Integrity — with risk,
/// reason, and a remediation script.
///
/// Equivalence model (the key insight that removes false positives):
///
///   class 0 — None             : no auto-generation (plain column)
///   class 1 — IdentityAlways   : auto, REJECTS explicit inserts (needs OVERRIDING)
///   class 2 — auto + explicit  : IdentityByDefault, Serial, SequenceDefault
///                                (all auto-generate AND accept explicit inserts)
///
///   same class  → behaviourally equivalent → at most Metadata/Cosmetic drift
///   0 vs (1|2)  → Functional drift (auto-generation present on one side only)
///   1 vs 2      → Functional drift (ALWAYS vs BY DEFAULT differ on explicit insert)
///
/// Data Integrity (Case D) is orthogonal: when a live <see cref="SequenceSyncState"/>
/// shows the sequence trailing MAX(column), the next insert can collide — escalated
/// regardless of whether the DDL matches.
/// </summary>
public static class GenerationStrategyAnalyzer
{
    public static GenerationDrift Analyze(
        string table,
        ColumnDefinition source,
        ColumnDefinition target,
        SequenceSyncState? targetSync = null,
        bool sourceSequenceOwned = false,
        bool targetSequenceOwned = false)
    {
        var s = source.GetGenerationStrategy(sourceSequenceOwned);
        var t = target.GetGenerationStrategy(targetSequenceOwned);
        var col = source.Name;

        var verdict = Classify(table, col, s, t);

        // Case D — sequence behind MAX(id). Escalates anything below Functional, because
        // even a perfectly matching schema will start raising duplicate-key errors.
        if (targetSync is { IsBehind: true } && verdict.Category < DriftCategory.Functional)
        {
            return new GenerationDrift
            {
                Source = s,
                Target = t,
                Category = DriftCategory.DataIntegrity,
                Risk = LintSeverity.Error,
                MigrationRequired = true,
                Reason =
                    $"Sequence '{targetSync.SequenceName}' last_value={targetSync.LastValue} is behind " +
                    $"MAX({col})={targetSync.MaxColumnValue}; the next insert may raise a duplicate key violation.",
                MigrationScript =
                    $"SELECT setval('{targetSync.SequenceName}', " +
                    $"(SELECT COALESCE(MAX(\"{col}\"), 1) FROM \"{table}\"));"
            };
        }

        return verdict;
    }

    private static GenerationDrift Classify(string table, string col, GenerationStrategy s, GenerationStrategy t)
    {
        int cs = ClassOf(s), ct = ClassOf(t);

        // Both plain — nothing to do here (ordinary column comparison handles literal defaults).
        if (cs == 0 && ct == 0)
            return None(s, t);

        // Identical strategy — fully equivalent (Case C when names differ only cosmetically).
        if (s == t)
            return None(s, t);

        // Auto-generation on exactly one side (Case A).
        if (cs == 0 || ct == 0)
        {
            var (present, missing) = cs == 0 ? ("target", "source") : ("source", "target");
            var presentStrat = cs == 0 ? t : s;
            return new GenerationDrift
            {
                Source = s,
                Target = t,
                Category = DriftCategory.Functional,
                Risk = LintSeverity.Error,
                MigrationRequired = true,
                Reason =
                    $"Automatic key generation exists in {present} ({Describe(presentStrat)}) " +
                    $"but {missing} is a plain column. Inserts that omit \"{col}\" will fail on the plain side.",
                // Remediation always brings target up to the source-of-truth (source = QA).
                MigrationScript = ct == 0
                    ? AddIdentityScript(table, col)        // target is the plain one → add generation
                    : DropIdentityScript(table, col)       // source is plain → target over-generates
            };
        }

        // Both auto-generating, different strategies.
        // class 2 vs 2 (e.g. IDENTITY BY DEFAULT vs SERIAL vs DEFAULT nextval) → equivalent (Case B).
        if (cs == 2 && ct == 2)
            return new GenerationDrift
            {
                Source = s,
                Target = t,
                Category = DriftCategory.Metadata,
                Risk = LintSeverity.Info,
                MigrationRequired = false,
                Reason =
                    $"{Describe(s)} (source) and {Describe(t)} (target) are behaviourally equivalent: " +
                    "both auto-generate keys and both accept explicit inserts. No migration required.",
                MigrationScript = null
            };

        // IdentityAlways vs an explicit-permissive strategy → real behavioural difference.
        return new GenerationDrift
        {
            Source = s,
            Target = t,
            Category = DriftCategory.Functional,
            Risk = LintSeverity.Warning,
            MigrationRequired = true,
            Reason =
                $"{Describe(s)} (source) vs {Describe(t)} (target): GENERATED ALWAYS rejects explicit " +
                $"\"{col}\" values (without OVERRIDING SYSTEM VALUE) while the other side accepts them — " +
                "inserts behave differently.",
            MigrationScript = AlignIdentityKindScript(table, col, s)
        };
    }

    // class 0 = none, 1 = identity always (no explicit), 2 = auto + explicit allowed
    private static int ClassOf(GenerationStrategy g) => g switch
    {
        GenerationStrategy.None => 0,
        GenerationStrategy.IdentityAlways => 1,
        _ => 2 // IdentityByDefault, Serial, SequenceDefault
    };

    private static GenerationDrift None(GenerationStrategy s, GenerationStrategy t) => new()
    {
        Source = s,
        Target = t,
        Category = DriftCategory.None,
        Risk = LintSeverity.None,
        MigrationRequired = false,
        Reason = "Generation strategy is equivalent."
    };

    private static string Describe(GenerationStrategy g) => g switch
    {
        GenerationStrategy.None => "PLAIN INTEGER",
        GenerationStrategy.IdentityAlways => "GENERATED ALWAYS AS IDENTITY",
        GenerationStrategy.IdentityByDefault => "GENERATED BY DEFAULT AS IDENTITY",
        GenerationStrategy.Serial => "SERIAL (owned sequence)",
        GenerationStrategy.SequenceDefault => "DEFAULT nextval(sequence)",
        _ => g.ToString()
    };

    private static string AddIdentityScript(string table, string col) =>
        $"ALTER TABLE \"{table}\" ALTER COLUMN \"{col}\" ADD GENERATED BY DEFAULT AS IDENTITY;\n" +
        $"-- align the new identity sequence with existing data:\n" +
        $"SELECT setval(pg_get_serial_sequence('\"{table}\"', '{col}'), " +
        $"(SELECT COALESCE(MAX(\"{col}\"), 1) FROM \"{table}\"));";

    private static string DropIdentityScript(string table, string col) =>
        $"-- target auto-generates but source does not; only drop if intentional:\n" +
        $"ALTER TABLE \"{table}\" ALTER COLUMN \"{col}\" DROP IDENTITY IF EXISTS;";

    private static string AlignIdentityKindScript(string table, string col, GenerationStrategy source) =>
        source == GenerationStrategy.IdentityAlways
            ? $"ALTER TABLE \"{table}\" ALTER COLUMN \"{col}\" SET GENERATED ALWAYS;"
            : $"ALTER TABLE \"{table}\" ALTER COLUMN \"{col}\" SET GENERATED BY DEFAULT;";
}

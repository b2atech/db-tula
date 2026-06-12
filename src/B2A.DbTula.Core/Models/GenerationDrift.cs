using B2A.DbTula.Core.Enums;

namespace B2A.DbTula.Core.Models;

/// <summary>
/// Result of comparing the value-generation strategy of one column across two
/// databases. Produced by <see cref="Semantics.GenerationStrategyAnalyzer"/>.
/// Replaces the old opaque "identity: source=True target=False" output with a
/// classified, actionable verdict.
/// </summary>
public class GenerationDrift
{
    public GenerationStrategy Source { get; init; }
    public GenerationStrategy Target { get; init; }

    public DriftCategory Category { get; init; }
    public LintSeverity Risk { get; init; } = LintSeverity.None;

    /// <summary>Whether a schema migration is required to reconcile the difference.</summary>
    public bool MigrationRequired { get; init; }

    /// <summary>Human-readable explanation of the verdict.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Suggested SQL to reconcile target → source (null when none needed).</summary>
    public string? MigrationScript { get; init; }

    /// <summary>True when there is any difference worth surfacing.</summary>
    public bool IsDrift => Category != DriftCategory.None;

    /// <summary>
    /// True when the difference is a false positive under the old text comparer —
    /// equivalent behaviour that should NOT flip a table to Mismatch.
    /// </summary>
    public bool IsSuppressible => Category is DriftCategory.Cosmetic or DriftCategory.Metadata;
}

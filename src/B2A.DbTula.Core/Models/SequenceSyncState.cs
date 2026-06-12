namespace B2A.DbTula.Core.Models;

/// <summary>
/// Runtime synchronization state of a sequence relative to the column it feeds.
/// Captured by a live probe (see GenerationStrategyAnalyzer remarks) — NOT available
/// from a schema-only snapshot. Used to detect Case D (sequence behind MAX(id)).
/// </summary>
public class SequenceSyncState
{
    public string SequenceName { get; init; } = string.Empty;

    /// <summary>The sequence's current last_value (pg_sequences.last_value).</summary>
    public long LastValue { get; init; }

    /// <summary>MAX() of the owning column at probe time. 0 when the table is empty.</summary>
    public long MaxColumnValue { get; init; }

    /// <summary>
    /// True when the next nextval() could collide with an existing row.
    /// A sequence is healthy while last_value >= MAX(column); once it trails, the
    /// next BY DEFAULT / SERIAL insert risks a duplicate-key violation.
    /// </summary>
    public bool IsBehind => LastValue < MaxColumnValue;
}

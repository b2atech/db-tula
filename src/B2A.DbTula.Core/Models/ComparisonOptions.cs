namespace B2A.DbTula.Core.Models;

/// <summary>
/// Configuration options for schema comparison behavior
/// </summary>
public class ComparisonOptions
{
    /// <summary>
    /// When true, ignores owner/definer differences and DDL noise in schema object comparisons.
    /// Default is true for ownership-agnostic comparison.
    /// </summary>
    public bool IgnoreOwnership { get; set; } = true;
    public string SourceLabel { get; set; } = "Source";
    public string TargetLabel { get; set; } = "Target";
}
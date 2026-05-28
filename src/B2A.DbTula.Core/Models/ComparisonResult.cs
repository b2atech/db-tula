using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Extensions;

namespace B2A.DbTula.Core.Models;

public class ComparisonResult
{
    public SchemaObjectType ObjectType { get; set; }
    public string Name { get; set; } = string.Empty;
    public ComparisonStatus Status { get; set; }
    public string Details { get; set; } = string.Empty;
    public string? DiffScript { get; set; }

    // New properties for side-by-side diff visualization
    public string? SourceScript { get; set; }
    public string? TargetScript { get; set; }
    public string? SideBySideDiffHtml { get; set; }
    public bool HasSideBySideDiff => !string.IsNullOrWhiteSpace(SideBySideDiffHtml);

    public List<ComparisonSubResult> SubResults { get; set; } = new();

    /// <summary>
    /// Atlas-style lint code (DS102, MF103, CD101, etc.).
    /// Null when the change has no known risk classification.
    /// </summary>
    public string? LintCode { get; set; }

    /// <summary>
    /// Risk severity derived from the lint code.
    /// </summary>
    public LintSeverity Severity { get; set; } = LintSeverity.None;

    /// <summary>
    /// Human-readable description of the lint rule that fired.
    /// </summary>
    public string? LintMessage { get; set; }

    public string DisplayType => ObjectType.ToDisplayString();
    public string DisplayStatus => Status.ToDisplayString();

    private static readonly HashSet<ComparisonStatus> _mismatchStatuses = new()
    {
        ComparisonStatus.Mismatch,
        ComparisonStatus.MissingInTarget,
        ComparisonStatus.MissingInSource
    };

    // Derived mismatch indicators
    public bool HasPrimaryKeyMismatch =>
    SubResults.Any(s => s.Component.Equals("PrimaryKeys", StringComparison.OrdinalIgnoreCase) &&
                        _mismatchStatuses.Contains(s.Status));

    public bool HasForeignKeyMismatch =>
        SubResults.Any(s => s.Component.Equals("ForeignKeys", StringComparison.OrdinalIgnoreCase) &&
                            _mismatchStatuses.Contains(s.Status));

    public bool HasColumnMismatch =>
        SubResults.Any(s => s.Component.Equals("Columns", StringComparison.OrdinalIgnoreCase) &&
                            _mismatchStatuses.Contains(s.Status));

    public bool HasIndexMismatch =>
        SubResults.Any(s => s.Component.Equals("Indexes", StringComparison.OrdinalIgnoreCase) &&
                            _mismatchStatuses.Contains(s.Status));

    public bool HasCreateScriptMismatch =>
     (ObjectType == SchemaObjectType.Function || ObjectType == SchemaObjectType.Procedure)
         ? _mismatchStatuses.Contains(Status)
         : SubResults.Any(s => s.Component.Equals("CreateScript", StringComparison.OrdinalIgnoreCase) &&
                               _mismatchStatuses.Contains(s.Status));
}
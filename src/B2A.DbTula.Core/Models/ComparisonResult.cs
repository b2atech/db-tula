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

    public List<ComparisonSubResult> SubResults { get; set; } = new();

    public string DisplayType => ObjectType.ToDisplayString();
    public string DisplayStatus => Status.ToDisplayString();

    // Derived mismatch indicators
    public bool HasPrimaryKeyMismatch =>
        SubResults.Any(s => s.Component.Equals("PrimaryKeys", StringComparison.OrdinalIgnoreCase) &&
                            s.Status == ComparisonStatus.Mismatch);

    public bool HasForeignKeyMismatch =>
        SubResults.Any(s => s.Component.Equals("ForeignKeys", StringComparison.OrdinalIgnoreCase) &&
                            s.Status == ComparisonStatus.Mismatch);

    public bool HasColumnMismatch =>
        SubResults.Any(s => s.Component.Equals("Columns", StringComparison.OrdinalIgnoreCase) &&
                            s.Status == ComparisonStatus.Mismatch);

    public bool HasIndexMismatch =>
        SubResults.Any(s => s.Component.Equals("Indexes", StringComparison.OrdinalIgnoreCase) &&
                            s.Status == ComparisonStatus.Mismatch);

    public bool HasCreateScriptMismatch =>
        SubResults.Any(s => s.Component.Equals("CreateScript", StringComparison.OrdinalIgnoreCase) &&
                            s.Status == ComparisonStatus.Mismatch);
}
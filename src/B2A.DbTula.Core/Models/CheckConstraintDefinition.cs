namespace B2A.DbTula.Core.Models;

public class CheckConstraintDefinition
{
    public string Name { get; set; } = string.Empty;
    public string CheckClause { get; set; } = string.Empty;

    /// <summary>
    /// When true (connoinherit), this check constraint is NOT inherited by child tables.
    /// Differences in this flag are a structural mismatch.
    /// </summary>
    public bool NoInherit { get; set; }

    public string GetStructuralKey() =>
        NormalizeClause(CheckClause);

    public bool StructuralEquals(CheckConstraintDefinition other)
    {
        if (other == null) return false;
        return string.Equals(NormalizeClause(CheckClause), NormalizeClause(other.CheckClause),
                   StringComparison.OrdinalIgnoreCase)
               && NoInherit == other.NoInherit;
    }

    private static string NormalizeClause(string clause) =>
        System.Text.RegularExpressions.Regex.Replace(clause?.Trim() ?? "", @"\s+", " ").ToLower();
}

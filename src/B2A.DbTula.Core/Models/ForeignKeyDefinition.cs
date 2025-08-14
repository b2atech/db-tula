namespace B2A.DbTula.Core.Models;

public class ForeignKeyDefinition
{
    public string Name { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string ReferencedTable { get; set; } = string.Empty;
    public string ReferencedColumn { get; set; } = string.Empty;
    public string? CreateScript { get; set; }

    /// <summary>
    /// Gets the structural key for semantic comparison (independent of FK name)
    /// </summary>
    public string GetStructuralKey()
    {
        return $"{ColumnName.ToLower()}→{ReferencedTable.ToLower()}.{ReferencedColumn.ToLower()}";
    }

    /// <summary>
    /// Compares FKs by structure, not by name
    /// </summary>
    public bool StructuralEquals(ForeignKeyDefinition other)
    {
        if (other == null) return false;
        
        return string.Equals(ColumnName, other.ColumnName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(ReferencedTable, other.ReferencedTable, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(ReferencedColumn, other.ReferencedColumn, StringComparison.OrdinalIgnoreCase);
    }
}
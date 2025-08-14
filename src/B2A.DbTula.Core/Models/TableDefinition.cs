namespace B2A.DbTula.Core.Models;

public class TableDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<ColumnDefinition> Columns { get; set; } = new();
    public List<PrimaryKeyDefinition> PrimaryKeys { get; set; } = new(); // just column names
    public List<ForeignKeyDefinition> ForeignKeys { get; set; } = new();
    public List<IndexDefinition> Indexes { get; set; } = new();
    public string CreateScript { get; set; } = string.Empty; // full CREATE TABLE script

    /// <summary>
    /// Gets a canonical signature of the table structure for semantic comparison
    /// Order-independent hash based on structural elements
    /// </summary>
    public string GetStructuralSignature()
    {
        var elements = new List<string>();
        
        // Add normalized columns (sorted by name for order independence)
        foreach (var col in Columns.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var colSig = $"COL:{col.Name.ToLower()}:{col.DataType.ToLower()}:{col.IsNullable}:{col.Length}:{col.DefaultValue?.ToLower() ?? ""}:{col.IsComputed}";
            elements.Add(colSig);
        }
        
        // Add normalized PKs (sorted by structural key)
        foreach (var pk in PrimaryKeys.OrderBy(p => p.GetStructuralKey()))
        {
            elements.Add($"PK:{pk.GetStructuralKey()}");
        }
        
        // Add normalized FKs (sorted by structural key to avoid duplicates)
        foreach (var fk in ForeignKeys.OrderBy(f => f.GetStructuralKey()))
        {
            elements.Add($"FK:{fk.GetStructuralKey()}");
        }
        
        // Add normalized indexes (sorted by structural key)
        foreach (var idx in Indexes.OrderBy(i => i.GetStructuralKey()))
        {
            elements.Add($"IDX:{idx.GetStructuralKey()}");
        }
        
        return string.Join("|", elements);
    }

    /// <summary>
    /// Compares tables by structural signature, not by scripts or names
    /// </summary>
    public bool StructuralEquals(TableDefinition other)
    {
        if (other == null) return false;
        
        return string.Equals(GetStructuralSignature(), other.GetStructuralSignature(), StringComparison.OrdinalIgnoreCase);
    }
}
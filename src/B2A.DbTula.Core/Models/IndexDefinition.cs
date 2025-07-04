namespace B2A.DbTula.Core.Models;

public class IndexDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new(); // Handles composite indexes
    public bool IsUnique { get; set; }
    public string IndexType { get; set; } = string.Empty; // Optional: btree, gin, etc.
}
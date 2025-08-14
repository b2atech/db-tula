namespace B2A.DbTula.Core.Models;

public class IndexDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new(); // Handles composite indexes
    public bool IsUnique { get; set; }
    public string IndexType { get; set; } = string.Empty; // Optional: btree, gin, etc.
    public string CreateScript { get; set; } = string.Empty; // <-- new
    
    /// <summary>
    /// Optional predicate for partial indexes (WHERE clause)
    /// </summary>
    public string? Predicate { get; set; }

    /// <summary>
    /// Gets the structural key for semantic comparison (independent of index name)
    /// </summary>
    public string GetStructuralKey()
    {
        var columnList = string.Join(",", Columns.Select(c => c.ToLower()));
        var indexMethod = IndexType.ToLower();
        var uniqueness = IsUnique ? "unique" : "nonunique";
        var predicate = NormalizePredicate(Predicate);
        
        return $"{indexMethod}:{uniqueness}:{columnList}:{predicate}";
    }

    /// <summary>
    /// Compares indexes by structure (method + columns + uniqueness + predicate), not by name
    /// </summary>
    public bool StructuralEquals(IndexDefinition other)
    {
        if (other == null) return false;
        
        return IsUnique == other.IsUnique &&
               string.Equals(IndexType, other.IndexType, StringComparison.OrdinalIgnoreCase) &&
               Columns.SequenceEqual(other.Columns, StringComparer.OrdinalIgnoreCase) &&
               string.Equals(NormalizePredicate(Predicate), NormalizePredicate(other.Predicate), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes predicate text (removes extra whitespace, standardizes case)
    /// </summary>
    private static string NormalizePredicate(string? predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate))
            return string.Empty;
            
        // Remove extra whitespace and normalize to lowercase for comparison
        return System.Text.RegularExpressions.Regex.Replace(predicate.Trim().ToLower(), @"\s+", " ");
    }
}
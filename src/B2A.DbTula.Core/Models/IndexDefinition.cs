namespace B2A.DbTula.Core.Models;

public class IndexDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public bool IsUnique { get; set; }
    public string IndexType { get; set; } = string.Empty;
    public string CreateScript { get; set; } = string.Empty;

    /// <summary>
    /// Columns in the INCLUDE clause (Postgres 11+, covering indexes).
    /// These are stored but not used for index lookups.
    /// </summary>
    public List<string> IncludedColumns { get; set; } = [];

    /// <summary>
    /// NULLS NOT DISTINCT behaviour for unique indexes (Postgres 15+).
    /// Null means not specified (old behaviour: nulls are always distinct).
    /// </summary>
    public bool? NullsDistinct { get; set; }

    /// <summary>
    /// Optional predicate for partial indexes (WHERE clause).
    /// </summary>
    public string? Predicate { get; set; }

    /// <summary>
    /// Gets the structural key for semantic comparison (independent of index name)
    /// </summary>
    public string GetStructuralKey()
    {
        var columnList  = string.Join(",", Columns.Select(c => c.ToLower()));
        var includedList = string.Join(",", IncludedColumns.Select(c => c.ToLower()));
        var indexMethod = IndexType.ToLower();
        var uniqueness  = IsUnique ? "unique" : "nonunique";
        var predicate   = NormalizePredicate(Predicate);
        var nullsDistinct = NullsDistinct.HasValue ? (NullsDistinct.Value ? "nd:true" : "nd:false") : "";

        return $"{indexMethod}:{uniqueness}:{columnList}:incl:{includedList}:{predicate}:{nullsDistinct}";
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
               IncludedColumns.SequenceEqual(other.IncludedColumns, StringComparer.OrdinalIgnoreCase) &&
               NullsDistinct == other.NullsDistinct &&
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
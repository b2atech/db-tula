namespace B2A.DbTula.Core.Models;

public class UniqueConstraintDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public string? CreateScript { get; set; }

    public string GetStructuralKey() =>
        string.Join(",", Columns.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).Select(c => c.ToLower()));

    public bool StructuralEquals(UniqueConstraintDefinition other)
    {
        if (other == null) return false;
        return string.Equals(GetStructuralKey(), other.GetStructuralKey(), StringComparison.OrdinalIgnoreCase);
    }
}

namespace B2A.DbTula.Core.Models;

public class EnumTypeDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Enum values in declaration order (order matters for Postgres ALTER TYPE ADD VALUE BEFORE/AFTER).
    /// </summary>
    public List<string> Values { get; set; } = [];

    public string GetStructuralKey() =>
        $"{Name.ToLower()}:[{string.Join(",", Values.Select(v => v.ToLower()))}]";

    public bool StructuralEquals(EnumTypeDefinition other)
    {
        if (other == null) return false;
        return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
            && Values.SequenceEqual(other.Values, StringComparer.Ordinal); // enum values are case-sensitive
    }
}

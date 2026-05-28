namespace B2A.DbTula.Core.Models;

public class DbSequenceDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = "bigint";
    public long StartValue { get; set; } = 1;
    public long IncrementBy { get; set; } = 1;
    public long MinValue { get; set; } = 1;
    public long MaxValue { get; set; } = long.MaxValue;
    public long CacheSize { get; set; } = 1;
    public bool Cycle { get; set; } = false;

    public string GetStructuralKey() =>
        $"{Name.ToLower()}|type:{DataType.ToLower()}|inc:{IncrementBy}|min:{MinValue}|max:{MaxValue}|cache:{CacheSize}|cycle:{Cycle}";

    public bool StructuralEquals(DbSequenceDefinition other)
    {
        if (other == null) return false;
        return string.Equals(DataType, other.DataType, StringComparison.OrdinalIgnoreCase)
            && IncrementBy == other.IncrementBy
            && MinValue == other.MinValue
            && MaxValue == other.MaxValue
            && CacheSize == other.CacheSize
            && Cycle == other.Cycle;
    }
}

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

    /// <summary>
    /// True when every option equals the PostgreSQL defaults for an ascending integer
    /// identity/serial sequence of this <see cref="DataType"/>:
    ///   INCREMENT BY 1, MINVALUE 1, START 1, CACHE 1, NO CYCLE, and
    ///   MAXVALUE = the maximum of the integer type (smallint 32767, integer 2147483647,
    ///   bigint 9223372036854775807).
    ///
    /// This lets reporting label a sequence whose DDL was written out in full (the expanded
    /// "INCREMENT BY 1 MINVALUE 1 MAXVALUE 2147483647 ..." form) as carrying only the implicit
    /// defaults, so it is never surfaced as drift against the shorthand. It does NOT collapse
    /// sequences of different integer types — that would hide a real int4→int8 change.
    /// </summary>
    public bool HasIntegerTypeDefaults()
    {
        var typeMax = DataType.Trim().ToLowerInvariant() switch
        {
            "smallint" or "int2" => (long)short.MaxValue,
            "integer" or "int" or "int4" => int.MaxValue,
            "bigint" or "int8" => long.MaxValue,
            _ => (long?)null
        };
        if (typeMax is null) return false;

        return IncrementBy == 1
            && StartValue == 1
            && MinValue == 1
            && CacheSize == 1
            && !Cycle
            && MaxValue == typeMax.Value;
    }

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

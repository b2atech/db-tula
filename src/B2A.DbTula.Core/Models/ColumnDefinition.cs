namespace B2A.DbTula.Core.Models;

public class ColumnDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Data type of the column (e.g., varchar, int, timestamp)
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the column allows NULL values
    /// </summary>
    public bool IsNullable { get; set; }

    /// <summary>
    /// Length for variable-length types (e.g., varchar(255))
    /// </summary>
    public int? Length { get; set; }

    /// <summary>
    /// Precision for numeric types (e.g., 18 in numeric(18,4))
    /// </summary>
    public int? NumericPrecision { get; set; }

    /// <summary>
    /// Scale for numeric types (e.g., 4 in numeric(18,4))
    /// </summary>
    public int? NumericScale { get; set; }

    /// <summary>
    /// Precision for datetime types (e.g., timestamp(3))
    /// </summary>
    public int? DateTimePrecision { get; set; }

    /// <summary>
    /// Default value expression or literal assigned to the column
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// True if this column is computed/generated (optional, depending on use case)
    /// </summary>
    public bool IsComputed { get; set; } = false;

    /// <summary>
    /// True if this column is an identity/auto-increment column
    /// </summary>
    public bool IsIdentity { get; set; } = false;

    /// <summary>
    /// The underlying type name from pg_catalog (udt_name).
    /// For enum columns, DataType = "USER-DEFINED" and UdtName = the enum type name.
    /// For built-in types, UdtName = the internal name (e.g. "int4", "varchar").
    /// </summary>
    public string? UdtName { get; set; }

    /// <summary>
    /// True when this column's type is a user-defined enum (DataType == "USER-DEFINED").
    /// </summary>
    public bool IsEnum => string.Equals(DataType, "USER-DEFINED", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Optional: Script to add this column (ALTER TABLE ... ADD COLUMN ...)
    /// </summary>
    public string? CreateScript { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj is not ColumnDefinition other)
            return false;

        return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(DataType, other.DataType, StringComparison.OrdinalIgnoreCase)
            && IsNullable == other.IsNullable
            && Length == other.Length
            && NumericPrecision == other.NumericPrecision
            && NumericScale == other.NumericScale
            && DateTimePrecision == other.DateTimePrecision
            && string.Equals(DefaultValue ?? "", other.DefaultValue ?? "", StringComparison.OrdinalIgnoreCase)
            && IsComputed == other.IsComputed
            && IsIdentity == other.IsIdentity;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            HashCode.Combine(Name.ToLower(), DataType.ToLower(), IsNullable, Length),
            HashCode.Combine(NumericPrecision, NumericScale, DateTimePrecision, DefaultValue?.ToLower(), IsComputed, IsIdentity));
    }
}

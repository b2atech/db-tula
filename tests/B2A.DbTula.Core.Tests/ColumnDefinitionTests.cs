using B2A.DbTula.Core.Models;
using Xunit;

namespace B2A.DbTula.Core.Tests;

public class ColumnDefinitionTests
{
    [Fact]
    public void Equals_IdenticalColumns_ReturnsTrue()
    {
        var a = new ColumnDefinition { Name = "amount", DataType = "numeric", NumericPrecision = 18, NumericScale = 4, IsNullable = false };
        var b = new ColumnDefinition { Name = "amount", DataType = "numeric", NumericPrecision = 18, NumericScale = 4, IsNullable = false };

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_DifferentNumericPrecision_ReturnsFalse()
    {
        var src = new ColumnDefinition { Name = "amount", DataType = "numeric", NumericPrecision = 18, NumericScale = 4 };
        var tgt = new ColumnDefinition { Name = "amount", DataType = "numeric", NumericPrecision = 10, NumericScale = 2 };

        Assert.NotEqual(src, tgt);
    }

    [Fact]
    public void Equals_DifferentNumericScale_ReturnsFalse()
    {
        var src = new ColumnDefinition { Name = "rate", DataType = "numeric", NumericPrecision = 10, NumericScale = 4 };
        var tgt = new ColumnDefinition { Name = "rate", DataType = "numeric", NumericPrecision = 10, NumericScale = 2 };

        Assert.NotEqual(src, tgt);
    }

    [Fact]
    public void Equals_DifferentNullability_ReturnsFalse()
    {
        var nullable    = new ColumnDefinition { Name = "name", DataType = "text", IsNullable = true };
        var notNullable = new ColumnDefinition { Name = "name", DataType = "text", IsNullable = false };

        Assert.NotEqual(nullable, notNullable);
    }

    [Fact]
    public void Equals_DifferentDataType_ReturnsFalse()
    {
        var int4 = new ColumnDefinition { Name = "id", DataType = "integer" };
        var int8 = new ColumnDefinition { Name = "id", DataType = "bigint"  };

        Assert.NotEqual(int4, int8);
    }

    [Fact]
    public void Equals_CaseInsensitiveNameAndType_ReturnsTrue()
    {
        var a = new ColumnDefinition { Name = "CustomerID", DataType = "INTEGER" };
        var b = new ColumnDefinition { Name = "customerid", DataType = "integer" };

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_DifferentDatetimePrecision_ReturnsFalse()
    {
        var ts3 = new ColumnDefinition { Name = "created_at", DataType = "timestamp without time zone", DateTimePrecision = 3 };
        var ts6 = new ColumnDefinition { Name = "created_at", DataType = "timestamp without time zone", DateTimePrecision = 6 };

        Assert.NotEqual(ts3, ts6);
    }
}

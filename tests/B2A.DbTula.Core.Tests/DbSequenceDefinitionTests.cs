using B2A.DbTula.Core.Models;
using Xunit;

namespace B2A.DbTula.Core.Tests;

public class DbSequenceDefinitionTests
{
    [Fact]
    public void StructuralEquals_IdenticalSequences_ReturnsTrue()
    {
        var a = new DbSequenceDefinition { Name = "invoice_seq", IncrementBy = 1, MinValue = 1, MaxValue = long.MaxValue, CacheSize = 1, Cycle = false, DataType = "bigint" };
        var b = new DbSequenceDefinition { Name = "invoice_seq", IncrementBy = 1, MinValue = 1, MaxValue = long.MaxValue, CacheSize = 1, Cycle = false, DataType = "bigint" };

        Assert.True(a.StructuralEquals(b));
    }

    [Fact]
    public void StructuralEquals_DifferentIncrementBy_ReturnsFalse()
    {
        var by1 = new DbSequenceDefinition { Name = "seq", IncrementBy = 1 };
        var by5 = new DbSequenceDefinition { Name = "seq", IncrementBy = 5 };

        Assert.False(by1.StructuralEquals(by5));
    }

    [Fact]
    public void StructuralEquals_DifferentCycle_ReturnsFalse()
    {
        var cycled    = new DbSequenceDefinition { Name = "seq", Cycle = true  };
        var notCycled = new DbSequenceDefinition { Name = "seq", Cycle = false };

        Assert.False(cycled.StructuralEquals(notCycled));
    }

    [Fact]
    public void StructuralEquals_DifferentMaxValue_ReturnsFalse()
    {
        var a = new DbSequenceDefinition { Name = "seq", MaxValue = 1_000_000 };
        var b = new DbSequenceDefinition { Name = "seq", MaxValue = long.MaxValue };

        Assert.False(a.StructuralEquals(b));
    }
}

using B2A.DbTula.Core.Models;
using Xunit;

namespace B2A.DbTula.Core.Tests;

public class EnumTypeDefinitionTests
{
    [Fact]
    public void StructuralEquals_IdenticalEnums_ReturnsTrue()
    {
        var a = new EnumTypeDefinition { Name = "status_type", Values = ["pending", "active", "inactive"] };
        var b = new EnumTypeDefinition { Name = "status_type", Values = ["pending", "active", "inactive"] };

        Assert.True(a.StructuralEquals(b));
    }

    [Fact]
    public void StructuralEquals_AddedValue_ReturnsFalse()
    {
        var src = new EnumTypeDefinition { Name = "status_type", Values = ["pending", "active", "inactive"] };
        var tgt = new EnumTypeDefinition { Name = "status_type", Values = ["pending", "active"] };

        Assert.False(src.StructuralEquals(tgt));
    }

    [Fact]
    public void StructuralEquals_ReorderedValues_ReturnsFalse()
    {
        // Postgres enum order matters for ADD VALUE BEFORE/AFTER
        var a = new EnumTypeDefinition { Name = "priority", Values = ["low", "medium", "high"] };
        var b = new EnumTypeDefinition { Name = "priority", Values = ["high", "medium", "low"] };

        Assert.False(a.StructuralEquals(b));
    }

    [Fact]
    public void StructuralEquals_EnumValuesCaseSensitive()
    {
        // Postgres enum labels are case-sensitive
        var a = new EnumTypeDefinition { Name = "role_type", Values = ["Admin"] };
        var b = new EnumTypeDefinition { Name = "role_type", Values = ["admin"] };

        Assert.False(a.StructuralEquals(b));
    }
}

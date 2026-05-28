using B2A.DbTula.Core.Models;
using Xunit;

namespace B2A.DbTula.Core.Tests;

public class CheckConstraintDefinitionTests
{
    [Fact]
    public void StructuralEquals_IdenticalClauses_ReturnsTrue()
    {
        var a = new CheckConstraintDefinition { Name = "chk_amount", CheckClause = "CHECK (amount > 0)" };
        var b = new CheckConstraintDefinition { Name = "chk_amount", CheckClause = "CHECK (amount > 0)" };

        Assert.True(a.StructuralEquals(b));
    }

    [Fact]
    public void StructuralEquals_WhitespaceDifference_ReturnsTrue()
    {
        var a = new CheckConstraintDefinition { CheckClause = "CHECK (amount > 0)" };
        var b = new CheckConstraintDefinition { CheckClause = "CHECK  (amount  >  0)" };

        Assert.True(a.StructuralEquals(b));
    }

    [Fact]
    public void StructuralEquals_CaseInsensitive_ReturnsTrue()
    {
        var a = new CheckConstraintDefinition { CheckClause = "CHECK (status IN ('active', 'inactive'))" };
        var b = new CheckConstraintDefinition { CheckClause = "check (STATUS in ('active', 'inactive'))" };

        Assert.True(a.StructuralEquals(b));
    }

    [Fact]
    public void StructuralEquals_DifferentClause_ReturnsFalse()
    {
        var a = new CheckConstraintDefinition { CheckClause = "CHECK (amount > 0)" };
        var b = new CheckConstraintDefinition { CheckClause = "CHECK (amount >= 0)" };

        Assert.False(a.StructuralEquals(b));
    }
}

using B2A.DbTula.Core.Models;
using Xunit;

namespace B2A.DbTula.Core.Tests;

public class IndexDefinitionTests
{
    [Fact]
    public void StructuralKey_PreservesColumnOrder()
    {
        var cityState = new IndexDefinition { Columns = ["city", "state"], IndexType = "btree", IsUnique = false };
        var stateCity = new IndexDefinition { Columns = ["state", "city"], IndexType = "btree", IsUnique = false };

        Assert.NotEqual(cityState.GetStructuralKey(), stateCity.GetStructuralKey());
    }

    [Fact]
    public void StructuralEquals_SameColumnsAndType_ReturnsTrue()
    {
        var a = new IndexDefinition { Columns = ["customer_id", "created_at"], IndexType = "btree", IsUnique = false };
        var b = new IndexDefinition { Columns = ["customer_id", "created_at"], IndexType = "btree", IsUnique = false };

        Assert.True(a.StructuralEquals(b));
    }

    [Fact]
    public void StructuralEquals_DifferentUniqueness_ReturnsFalse()
    {
        var unique    = new IndexDefinition { Columns = ["email"], IndexType = "btree", IsUnique = true };
        var nonUnique = new IndexDefinition { Columns = ["email"], IndexType = "btree", IsUnique = false };

        Assert.False(unique.StructuralEquals(nonUnique));
    }

    [Fact]
    public void StructuralEquals_DifferentIndexType_ReturnsFalse()
    {
        var btree = new IndexDefinition { Columns = ["name"], IndexType = "btree", IsUnique = false };
        var hash  = new IndexDefinition { Columns = ["name"], IndexType = "hash",  IsUnique = false };

        Assert.False(btree.StructuralEquals(hash));
    }

    [Fact]
    public void StructuralEquals_DifferentPredicate_ReturnsFalse()
    {
        var withPred    = new IndexDefinition { Columns = ["amount"], IndexType = "btree", IsUnique = false, Predicate = "amount > 0" };
        var withoutPred = new IndexDefinition { Columns = ["amount"], IndexType = "btree", IsUnique = false, Predicate = null };

        Assert.False(withPred.StructuralEquals(withoutPred));
    }

    [Fact]
    public void StructuralEquals_PredicateWhitespaceDifference_ReturnsTrue()
    {
        var a = new IndexDefinition { Columns = ["status"], IndexType = "btree", IsUnique = false, Predicate = "status = 'active'" };
        var b = new IndexDefinition { Columns = ["status"], IndexType = "btree", IsUnique = false, Predicate = "status  =  'active'" };

        Assert.True(a.StructuralEquals(b));
    }
}

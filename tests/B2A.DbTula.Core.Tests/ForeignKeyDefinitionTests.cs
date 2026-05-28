using B2A.DbTula.Core.Models;
using Xunit;

namespace B2A.DbTula.Core.Tests;

public class ForeignKeyDefinitionTests
{
    [Fact]
    public void StructuralEquals_IdenticalFKs_ReturnsTrue()
    {
        var a = new ForeignKeyDefinition { ColumnName = "customer_id", ReferencedTable = "customers", ReferencedColumn = "id", OnDelete = "CASCADE",    OnUpdate = "NO ACTION" };
        var b = new ForeignKeyDefinition { ColumnName = "customer_id", ReferencedTable = "customers", ReferencedColumn = "id", OnDelete = "CASCADE",    OnUpdate = "NO ACTION" };

        Assert.True(a.StructuralEquals(b));
    }

    [Fact]
    public void StructuralEquals_DifferentOnDelete_ReturnsFalse()
    {
        var cascade  = new ForeignKeyDefinition { ColumnName = "order_id", ReferencedTable = "orders", ReferencedColumn = "id", OnDelete = "CASCADE" };
        var restrict = new ForeignKeyDefinition { ColumnName = "order_id", ReferencedTable = "orders", ReferencedColumn = "id", OnDelete = "RESTRICT" };

        Assert.False(cascade.StructuralEquals(restrict));
    }

    [Fact]
    public void StructuralEquals_DifferentOnUpdate_ReturnsFalse()
    {
        var noAction = new ForeignKeyDefinition { ColumnName = "cat_id", ReferencedTable = "categories", ReferencedColumn = "id", OnUpdate = "NO ACTION" };
        var cascade  = new ForeignKeyDefinition { ColumnName = "cat_id", ReferencedTable = "categories", ReferencedColumn = "id", OnUpdate = "CASCADE"   };

        Assert.False(noAction.StructuralEquals(cascade));
    }

    [Fact]
    public void GetStructuralKey_IncludesCascadeActions()
    {
        var fk = new ForeignKeyDefinition
        {
            ColumnName = "user_id", ReferencedTable = "users", ReferencedColumn = "id",
            OnDelete = "SET NULL", OnUpdate = "CASCADE"
        };

        var key = fk.GetStructuralKey();

        Assert.Contains("del:set null", key);
        Assert.Contains("upd:cascade",  key);
    }

    [Fact]
    public void StructuralEquals_DifferentReferencedTable_ReturnsFalse()
    {
        var a = new ForeignKeyDefinition { ColumnName = "ref_id", ReferencedTable = "table_a", ReferencedColumn = "id" };
        var b = new ForeignKeyDefinition { ColumnName = "ref_id", ReferencedTable = "table_b", ReferencedColumn = "id" };

        Assert.False(a.StructuralEquals(b));
    }
}

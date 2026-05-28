using B2A.DbTula.Core.Models;
using B2A.DbTula.Core.Utilities;
using Xunit;

namespace B2A.DbTula.Core.Tests;

public class DefinitionCanonicalizerTests
{
    private static readonly ComparisonOptions IgnoreOwnership = new() { IgnoreOwnership = true };
    private static readonly ComparisonOptions KeepOwnership   = new() { IgnoreOwnership = false };

    [Fact]
    public void DoesNotCorrupt_TableQualifiedColumnRefs_InFunctionBody()
    {
        var input = @"CREATE FUNCTION get_amount() RETURNS numeric AS $$
            SELECT t.amount::numeric FROM invoices t WHERE t.id = 1;
        $$ LANGUAGE sql;";

        var result = DefinitionCanonicalizer.CanonicalizeDefinition(input, "postgres", IgnoreOwnership);

        Assert.Contains("t.amount::numeric", result);
    }

    [Fact]
    public void DoesNotCorrupt_PgCatalogQualifiedTypes()
    {
        var input = "CREATE FUNCTION foo() RETURNS pg_catalog.text AS $$ SELECT 'x'; $$ LANGUAGE sql;";

        var result = DefinitionCanonicalizer.CanonicalizeDefinition(input, "postgres", IgnoreOwnership);

        Assert.Contains("pg_catalog", result);
    }

    [Fact]
    public void Removes_OwnerTo_Statement()
    {
        var input = @"CREATE FUNCTION foo() RETURNS void AS $$ BEGIN END; $$ LANGUAGE plpgsql;
ALTER FUNCTION foo() OWNER TO superuser;";

        var result = DefinitionCanonicalizer.CanonicalizeDefinition(input, "postgres", IgnoreOwnership);

        Assert.DoesNotContain("OWNER TO", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE FUNCTION", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Removes_SetSearchPath_Statement()
    {
        var input = @"SET search_path = public, pg_catalog;
CREATE FUNCTION foo() RETURNS void AS $$ BEGIN END; $$ LANGUAGE plpgsql;";

        var result = DefinitionCanonicalizer.CanonicalizeDefinition(input, "postgres", IgnoreOwnership);

        Assert.DoesNotContain("search_path", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Removes_Grant_Statement()
    {
        var input = @"CREATE FUNCTION foo() RETURNS void AS $$ BEGIN END; $$ LANGUAGE plpgsql;
GRANT EXECUTE ON FUNCTION foo() TO app_user;";

        var result = DefinitionCanonicalizer.CanonicalizeDefinition(input, "postgres", IgnoreOwnership);

        Assert.DoesNotContain("GRANT", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Removes_MySQL_Definer_Clause()
    {
        var input = "CREATE DEFINER=`admin`@`localhost` FUNCTION `foo`() RETURNS int READS SQL DATA BEGIN RETURN 1; END";

        var result = DefinitionCanonicalizer.CanonicalizeDefinition(input, "mysql", IgnoreOwnership);

        Assert.DoesNotContain("DEFINER", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WhenIgnoreOwnershipFalse_PreservesOwnerTo()
    {
        var input = "CREATE FUNCTION foo() RETURNS void AS $$ BEGIN END; $$ LANGUAGE plpgsql;\nALTER FUNCTION foo() OWNER TO superuser;";

        var result = DefinitionCanonicalizer.CanonicalizeDefinition(input, "postgres", KeepOwnership);

        Assert.Contains("OWNER TO", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NullInput_ReturnsEmpty()
    {
        var result = DefinitionCanonicalizer.CanonicalizeDefinition(null, "postgres", IgnoreOwnership);
        Assert.Equal(string.Empty, result);
    }
}

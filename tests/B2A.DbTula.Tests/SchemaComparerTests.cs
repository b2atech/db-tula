using B2A.DbTula.Cli;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;

namespace B2A.DbTula.Tests;

public class SchemaComparerTests
{
    private static SchemaComparer CreateComparer() => new();

    // ── helpers ──────────────────────────────────────────────────────────────

    private static TableDefinition SimpleTable(string name, params ColumnDefinition[] columns) =>
        new() { Name = name, Columns = columns.ToList() };

    private static ColumnDefinition Col(string name, string type, bool nullable = true) =>
        new() { Name = name, DataType = type, IsNullable = nullable };

    // ── 1. Missing table ─────────────────────────────────────────────────────

    [Fact]
    public async Task Table_MissingInTarget_ReportsMissingInTarget()
    {
        var source = new MockSchemaProvider
        {
            Tables = ["orders"],
            TableDefs = { ["orders"] = SimpleTable("orders", Col("id", "integer")) }
        };
        var target = new MockSchemaProvider();

        var results = await CreateComparer().CompareAsync(source, target);

        var r = Assert.Single(results.Where(x => x.ObjectType == SchemaObjectType.Table));
        Assert.Equal(ComparisonStatus.MissingInTarget, r.Status);
        Assert.Equal("orders", r.Name);
    }

    // ── 2. Missing column ────────────────────────────────────────────────────

    [Fact]
    public async Task Column_MissingInTarget_ReportsMismatch()
    {
        var table = "users";

        var sourceDef = SimpleTable(table,
            Col("id", "integer"),
            Col("email", "varchar"));

        var targetDef = SimpleTable(table,
            Col("id", "integer")); // email is missing

        var source = new MockSchemaProvider { Tables = [table], TableDefs = { [table] = sourceDef } };
        var target = new MockSchemaProvider { Tables = [table], TableDefs = { [table] = targetDef } };

        var results = await CreateComparer().CompareAsync(source, target);

        var r = Assert.Single(results.Where(x => x.ObjectType == SchemaObjectType.Table && x.Name == table));
        Assert.Equal(ComparisonStatus.Mismatch, r.Status);
        Assert.True(r.HasColumnMismatch);
        Assert.Contains(r.SubResults, s => s.Component == "Columns" && s.Status == ComparisonStatus.MissingInTarget && s.Details.Contains("email"));
    }

    // ── 3. Different column type ─────────────────────────────────────────────

    [Fact]
    public async Task Column_DifferentType_ReportsMismatch()
    {
        var table = "products";

        var sourceDef = SimpleTable(table, Col("price", "numeric"));
        var targetDef = SimpleTable(table, Col("price", "integer")); // wrong type in PROD

        var source = new MockSchemaProvider { Tables = [table], TableDefs = { [table] = sourceDef } };
        var target = new MockSchemaProvider { Tables = [table], TableDefs = { [table] = targetDef } };

        var results = await CreateComparer().CompareAsync(source, target);

        var r = Assert.Single(results.Where(x => x.ObjectType == SchemaObjectType.Table && x.Name == table));
        Assert.Equal(ComparisonStatus.Mismatch, r.Status);
        Assert.Contains(r.SubResults, s => s.Component == "Columns" && s.Status == ComparisonStatus.Mismatch && s.Details.Contains("price"));
    }

    // ── 4. Missing function ───────────────────────────────────────────────────

    [Fact]
    public async Task Function_MissingInTarget_ReportsMissingInTarget()
    {
        var fn = new DbFunctionDefinition { Name = "get_balance", Arguments = "account_id integer" };

        var source = new MockSchemaProvider
        {
            Functions = [fn],
            FunctionDefs = { ["get_balance"] = "CREATE OR REPLACE FUNCTION get_balance(...) ..." }
        };
        var target = new MockSchemaProvider();

        var results = await CreateComparer().CompareAsync(source, target);

        var r = Assert.Single(results.Where(x => x.ObjectType == SchemaObjectType.Function));
        Assert.Equal(ComparisonStatus.MissingInTarget, r.Status);
        Assert.Equal("get_balance", r.Name);
    }

    // ── 5. Different function body ────────────────────────────────────────────

    [Fact]
    public async Task Function_DifferentBody_ReportsMismatch()
    {
        var fn = new DbFunctionDefinition { Name = "calculate_tax", Arguments = "" };

        var sourceDef = "CREATE OR REPLACE FUNCTION calculate_tax() RETURNS numeric AS $$ SELECT 0.18; $$ LANGUAGE SQL;";
        var targetDef = "CREATE OR REPLACE FUNCTION calculate_tax() RETURNS numeric AS $$ SELECT 0.10; $$ LANGUAGE SQL;";

        var source = new MockSchemaProvider
        {
            Functions = [fn],
            FunctionDefs = { ["calculate_tax"] = sourceDef }
        };
        var target = new MockSchemaProvider
        {
            Functions = [fn],
            FunctionDefs = { ["calculate_tax"] = targetDef }
        };

        var results = await CreateComparer().CompareAsync(source, target);

        var r = Assert.Single(results.Where(x => x.ObjectType == SchemaObjectType.Function));
        Assert.Equal(ComparisonStatus.Mismatch, r.Status);
    }

    // ── 6. Missing view ───────────────────────────────────────────────────────

    [Fact]
    public async Task View_MissingInTarget_ReportsMissingInTarget()
    {
        var view = new DbViewDefinition { Name = "active_users", Definition = "SELECT * FROM users WHERE active = true" };

        var source = new MockSchemaProvider
        {
            Views = [view],
            ViewDefs = { ["active_users"] = view.Definition }
        };
        var target = new MockSchemaProvider();

        var results = await CreateComparer().CompareAsync(source, target);

        var r = Assert.Single(results.Where(x => x.ObjectType == SchemaObjectType.View));
        Assert.Equal(ComparisonStatus.MissingInTarget, r.Status);
    }

    // ── 7. Different procedure body ───────────────────────────────────────────

    [Fact]
    public async Task Procedure_DifferentBody_ReportsMismatch()
    {
        var proc = new DbFunctionDefinition { Name = "archive_old_records", Arguments = "" };

        var sourceDef = "CREATE OR REPLACE PROCEDURE archive_old_records() AS $$ DELETE FROM logs WHERE created < NOW() - INTERVAL '1 year'; $$ LANGUAGE SQL;";
        var targetDef = "CREATE OR REPLACE PROCEDURE archive_old_records() AS $$ DELETE FROM logs WHERE created < NOW() - INTERVAL '2 years'; $$ LANGUAGE SQL;";

        var source = new MockSchemaProvider
        {
            Procedures = [proc],
            ProcedureDefs = { ["archive_old_records"] = sourceDef }
        };
        var target = new MockSchemaProvider
        {
            Procedures = [proc],
            ProcedureDefs = { ["archive_old_records"] = targetDef }
        };

        var results = await CreateComparer().CompareAsync(source, target);

        var r = Assert.Single(results.Where(x => x.ObjectType == SchemaObjectType.Procedure));
        Assert.Equal(ComparisonStatus.Mismatch, r.Status);
    }

    // ── 8. Tables match — no false positives ─────────────────────────────────

    [Fact]
    public async Task Table_IdenticalInBoth_ReportsMatch()
    {
        var table = "accounts";
        var def = SimpleTable(table, Col("id", "integer", false), Col("name", "varchar"));

        var source = new MockSchemaProvider { Tables = [table], TableDefs = { [table] = def } };
        var target = new MockSchemaProvider { Tables = [table], TableDefs = { [table] = def } };

        var results = await CreateComparer().CompareAsync(source, target);

        var r = Assert.Single(results.Where(x => x.ObjectType == SchemaObjectType.Table));
        Assert.Equal(ComparisonStatus.Match, r.Status);
    }
}

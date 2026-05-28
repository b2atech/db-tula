using B2A.DbTula.Infrastructure.Postgres;
using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;
using Xunit;

namespace B2A.DbTula.Integration.Tests;

/// <summary>
/// Integration tests for BulkSchemaFetcher against a real Postgres container.
/// Requires Docker to be running. Each test class gets its own container.
/// </summary>
[Collection("postgres")]
public class BulkSchemaFetcherTests : IAsyncLifetime
{
    private PostgreSqlContainer _pg = null!;
    private DatabaseConnection _conn = null!;
    private BulkSchemaFetcher _fetcher = null!;

    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        await _pg.StartAsync();

        _conn = new DatabaseConnection(_pg.GetConnectionString(),
            (_, _, msg, _) => Console.WriteLine(msg), verbose: false,
            logLevel: B2A.DbTula.Core.Enums.LogLevel.Basic);

        _fetcher = new BulkSchemaFetcher(_conn);

        await SeedSchema();
    }

    public async Task DisposeAsync()
    {
        await _pg.DisposeAsync();
    }

    private async Task SeedSchema()
    {
        await _conn.ExecuteCommandAsync(@"
            CREATE TYPE invoice_status AS ENUM ('draft', 'sent', 'paid', 'cancelled');

            CREATE TABLE customers (
                id         integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                email      character varying(255) NOT NULL,
                created_at timestamp without time zone DEFAULT now()
            );
            ALTER TABLE customers ADD CONSTRAINT uq_customers_email UNIQUE (email);

            CREATE TABLE invoices (
                id          integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                customer_id integer NOT NULL REFERENCES customers(id) ON DELETE CASCADE ON UPDATE NO ACTION,
                amount      numeric(18,4) NOT NULL,
                status      invoice_status NOT NULL DEFAULT 'draft',
                due_date    date,
                CONSTRAINT chk_amount_positive CHECK (amount > 0)
            );

            CREATE INDEX idx_invoices_customer_id ON invoices (customer_id);
            CREATE INDEX idx_invoices_status_due  ON invoices (status, due_date);

            CREATE SEQUENCE payment_seq START WITH 1000 INCREMENT BY 5 MINVALUE 1000 MAXVALUE 999999;

            CREATE MATERIALIZED VIEW mv_customer_totals AS
                SELECT customer_id, SUM(amount) AS total FROM invoices GROUP BY customer_id;

            CREATE OR REPLACE FUNCTION get_invoice_total(p_customer_id integer)
            RETURNS numeric LANGUAGE sql AS $$
                SELECT SUM(t.amount) FROM invoices t WHERE t.customer_id = p_customer_id;
            $$;
        ");
    }

    [Fact]
    public async Task Snapshot_ContainsBothTables()
    {
        var snap = await _fetcher.TakeSnapshotAsync();
        Assert.Contains("customers", snap.TableNames);
        Assert.Contains("invoices",  snap.TableNames);
    }

    [Fact]
    public async Task Columns_NumericPrecisionAndScaleFetched()
    {
        var snap = await _fetcher.TakeSnapshotAsync();
        var amount = snap.ColumnsByTable["invoices"].First(c => c.Name == "amount");

        Assert.Equal(18, amount.NumericPrecision);
        Assert.Equal(4,  amount.NumericScale);
    }

    [Fact]
    public async Task Columns_IsIdentityDetected()
    {
        var snap = await _fetcher.TakeSnapshotAsync();
        var id = snap.ColumnsByTable["invoices"].First(c => c.Name == "id");

        Assert.True(id.IsIdentity);
    }

    [Fact]
    public async Task Indexes_ColumnOrderPreserved()
    {
        var snap = await _fetcher.TakeSnapshotAsync();
        var idx = snap.IndexesByTable["invoices"].First(i => i.Name == "idx_invoices_status_due");

        Assert.Equal(new[] { "status", "due_date" }, idx.Columns);
    }

    [Fact]
    public async Task ForeignKeys_CascadeActionsFetched()
    {
        var snap = await _fetcher.TakeSnapshotAsync();
        var fk = snap.ForeignKeysByTable["invoices"].First();

        Assert.Equal("CASCADE",   fk.OnDelete);
        Assert.Equal("NO ACTION", fk.OnUpdate);
        Assert.Equal("customers", fk.ReferencedTable);
    }

    [Fact]
    public async Task CheckConstraints_Fetched()
    {
        var snap = await _fetcher.TakeSnapshotAsync();
        var checks = snap.CheckConstraintsByTable["invoices"];

        Assert.Contains(checks, c => c.Name == "chk_amount_positive");
    }

    [Fact]
    public async Task MaterializedViews_DetectedViaPgClass_NotByName()
    {
        var snap = await _fetcher.TakeSnapshotAsync();

        Assert.Contains("mv_customer_totals", snap.MaterializedViewNames);
        // mv_customer_totals is NOT in TableNames (not a base table)
        Assert.DoesNotContain("mv_customer_totals", snap.TableNames);
    }

    [Fact]
    public async Task Enums_ValueOrderPreserved()
    {
        var snap = await _fetcher.TakeSnapshotAsync();
        var status = snap.Enums.First(e => e.Name == "invoice_status");

        Assert.Equal(new[] { "draft", "sent", "paid", "cancelled" }, status.Values);
    }

    [Fact]
    public async Task Sequences_DefinitionFetched()
    {
        var snap = await _fetcher.TakeSnapshotAsync();
        var seq = snap.Sequences.First(s => s.Name == "payment_seq");

        Assert.Equal(5,    seq.IncrementBy);
        Assert.Equal(1000, seq.MinValue);
        Assert.Equal(999999, seq.MaxValue);
    }

    [Fact]
    public async Task Functions_FetchedWithDefinition()
    {
        var snap = await _fetcher.TakeSnapshotAsync();
        var fn = snap.Functions.FirstOrDefault(f => f.Name == "get_invoice_total");

        Assert.NotNull(fn);
        Assert.Contains("customer_id", fn.Definition);
    }

    [Fact]
    public async Task UniqueConstraints_Fetched()
    {
        var snap = await _fetcher.TakeSnapshotAsync();
        var uqs = snap.UniqueConstraintsByTable["customers"];

        Assert.Contains(uqs, u => u.Name == "uq_customers_email");
    }
}

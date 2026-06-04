# Data Tula — Architecture Review & Improved Design (v2)

> **Reviewer role:** senior software/ERP-accounting architect + PostgreSQL performance + .NET platform.
> **Subject:** [data-tula-detailed-implementation-plan.md](data-tula-detailed-implementation-plan.md) — the proposed business-data reconciliation tool for the Dgtula/Dhanman platform.
> **Status:** This document supersedes the original plan where they conflict. The original is kept for history.
> **Companion:** [data-tula-rule-catalog.md](data-tula-rule-catalog.md) — the full, live-verified rule catalog across all modules (Sales, Purchase, Payroll, Inventory, Common, Bank). Read it alongside this architecture.

---

## 0. Executive Summary

The original plan is a **strong first draft** — good instincts on read-only safety, raw Npgsql over EF, SQL-file rules, finding hashing, and run/finding diagnostics separation. But it contains **one structural error that invalidates its central premise**, plus several wrong table/column assumptions, because it was written before the real service databases were inspected.

I read the actual repos (`dhanman-sales`, `dhanman-common`). The headline finding:

> **The accounting ledger does NOT live in the Sales DB.** Invoices live in **Sales DB**; transactions and journal entries live in **Common DB**. Therefore the flagship use case (SAL-001…SAL-004, "invoice ↔ ledger") is **inherently cross-database**, not a same-database SQL rule. Only **SAL-005** (invoice settled amount vs payment records) is same-database.

This single fact reshapes the design: **v0.1 must include the cross-database comparison engine from day one** (confirmed decision), rather than deferring it to v0.5 as the original plan assumed.

I then **verified everything against the live QA databases** (`db.qa.dgtula.com`, PostgreSQL 18.3, read-only) across all nine service DBs. The live data surfaced a second structural correction that the source code alone did not reveal: **the forward link `<document>.transaction_id` is almost never populated** (of 23,433 sales invoices, 23,211 have a Common transaction via `document_id`, yet **21,089 have `transaction_id = NULL`**). The **authoritative link is the reverse direction — `transaction_headers.document_id` + `transaction_source_type`** — not `transaction_id`. Keying reconciliation on `transaction_id` would emit ~21k false positives. Full evidence and the complete rule catalog are in [data-tula-rule-catalog.md](data-tula-rule-catalog.md).

The rest of this review (a) classifies the original design, (b) presents the corrected architecture, and (c) ends with an agent-ready v0.1 implementation prompt that uses **verified** names and a stop-and-report gate.

### Locked decisions (from review discussion)
1. **v0.1 includes the cross-DB engine** — keyset-batched C# comparator (Sales ↔ Common), plus SQL-file rules for genuinely same-DB checks.
2. **Separate Diagnostics DB** (`dhanman_diagnostics_qa` / `_prod`) — the only writable database.
3. **Replica-ready, default to primary** — connection names imply replicas (`SalesReadDb`, `CommonReadDb`), default to primary guarded by read-only user + `statement_timeout` + off-peak scheduling; switch to a replica when one exists.
4. Reconciliation logic stays **separate from db-tula** (schema comparison). Shared conventions only.

---

## 0.1 Verified schema (the ground truth that drives this design)

Confirmed first by reading `dhanman-sales`/`dhanman-common` source, then **validated against the live QA databases** (`db.qa.dgtula.com`, read-only, 2026-06-04). See [data-tula-rule-catalog.md §1](data-tula-rule-catalog.md) for the complete multi-DB schema, the `transaction_source_type` code map, and per-module document tables.

| Concept | **Verified reality** | Original plan assumed |
|---|---|---|
| **Invoice header** | `invoice_headers` — **Sales DB**, `public`. Columns: `id` uuid PK, `invoice_number`, `customer_id` uuid, `company_id` uuid, `total_amount` numeric, `settled_amount` numeric, `invoice_date` timestamp, `invoice_status_id` int, `payment_status_id` int, `is_deleted` bool, `created_on_utc`, **`transaction_id` bigint** (link → Common) | mostly right, **missed `transaction_id`** |
| **Invoice payments** | **`invoice_payment_headers`** + **`invoice_payment_details`** — Sales DB. Payment amount = **`received_amount`**, date = `received_date`. Detail FK = `invoice_header_id`. Header also has `transaction_id` bigint → Common | used `invoice_payments` + `amount` — **wrong table & column** |
| **Transaction header** | `transaction_headers` — **Common DB** (NOT Sales). `id` **bigint** PK, `company_id`, `customer_id`/`vendor_id`/`employee_id`, **`transaction_source_type` int** (1=Invoice, 2=Bill, 3=Invoice Payment, 4=Bill Payment, 5=Salary Posting, 6=Salary Payment, …), **`document_id` uuid** (= source doc id), `document_number`, `status_id`, **`is_reversed` bool**, `transaction_date` | placed in Sales DB with `source_type`/`source_id` — **wrong DB and wrong link columns** |
| **Journal entries** | `journal_entries` — **Common DB**. `id` bigint, `transaction_id`, `account_id` uuid?, **`entry_type` char ('D'/'C')**, **`amount` numeric — always ≥ 0 (sign carried by entry_type; verified 0 negatives)**, `entry_source_id` (1=AUTO/2=MANUAL), `is_deleted` | `entry_type` right; **wrong DB**; amount-sign convention now confirmed |
| **Document → ledger link** | **AUTHORITATIVE = reverse:** `transaction_headers.document_id = <doc>.id` AND `transaction_source_type = <code>`. The **forward** `<doc>.transaction_id` is mostly NULL (21,089/23,211 invoices) — unusable as a join key | original used a non-existent `source_type/source_id` join |
| **Doc headers (all modules)** | `invoice_headers`, `bill_headers`, `payroll_transaction_headers`, all `*_payment_headers` share: `is_posted` bool, `transaction_id` bigint?, **`debit_account_id`/`credit_account_id` uuid** (AR/AP + revenue/expense legs on the doc itself), `company_id`, status id, totals, `tds_amount` | not known |
| **Invoice status** | `invoice_statuses`: 1 DRAFT, 2 PENDING_APPROVAL, **3 APPROVED**, 4 PARTIALLY_PAID, **5 PAID**, 6 REJECTED, 7 CANCELLED, 8 REVERSE. Posted ⇔ a Common txn exists for the doc (NOT `transaction_id IS NOT NULL`) | left as a `TODO` placeholder |
| **Party master** | Common `parties` (`gstin`, `pan`, `party_name`, `entity_type_id`); `customers.party_id`, `vendors.party_id` link to it | n/a (relevant to future COM rules) |
| **Tenancy** | Common `organizations` (uuid) → `companies` (uuid). `company_id` used across all services; `organization_id` is the top-level tenant | correct |
| **Existing recon** | Common already has `bank_reconciliations`, `audit_queries`, `audit_query_responses`, `reconciliation_statuses` — a working reconciliation/audit precedent | not mentioned |
| **Audit convention** | `created_on_utc`, `created_by`, `modified_on_utc`, `modified_by`, `is_deleted`, `deleted_on_utc` (IAuditableEntity / ISoftDeletableEntity), with global `is_deleted=false` query filter | correct |

**Implication on the link model (live-verified):** No `source_type`/`source_id`/`source_number` migration on `transaction_headers` is needed (the original plan proposed one — **drop it**). The link already exists, and the live data tells us which direction to trust:
- **Reverse — AUTHORITATIVE:** `transaction_headers.document_id = <doc>.id` AND `transaction_source_type = <code>`. **All reconciliation joins use this.**
- **Forward — UNRELIABLE:** `<doc>.transaction_id` is NULL for ~90% of posted documents. Its being null *while* a Common transaction exists is itself a **High** finding (broken back-link, SAL-008/PUR-008/PAY-008), not a join key.

**Live evidence (Sales, QA 2026-06-04):** 23,433 invoices · 23,211 linked via `document_id` · **21,089 with NULL `transaction_id`** · 222 genuinely unposted · 150 duplicate postings · 0 orphans. This is exactly the kind of systemic integrity gap data-tula exists to catch — and it would be invisible (or 21k false positives) under the original `transaction_id` model.

---

## 1. Architecture Assessment

### Good decisions (keep)
- **Phase 1 read-only** on business data; only diagnostics writable. Correct and non-negotiable for a tool that runs against production.
- **Raw Npgsql, no EF Core.** Right call for a diagnostic engine: transparent SQL, no migration baggage, full control over timeouts and streaming.
- **SQL-file rules for same-DB checks.** Keeps analyst-authored rules reviewable and version-controlled.
- **Three-table diagnostics model** (rules / runs / findings) and the **finding hash** for cross-run dedup.
- **Exit-code contract** (0 / 1 / 2) — clean for CI gating.
- **Source-document traceability** emphasis — the right north star (it already exists in the data; the plan just didn't know the column names).
- **Standardized rule output columns** → generic row→finding mapping. Good.

### Weaknesses (fix)
- **Flagship use case mis-modeled as same-DB.** SAL-001…004 cross Sales↔Common. This is the big one.
- **Wrong payment table/columns** (`invoice_payments`/`amount` → `invoice_payment_headers`+`invoice_payment_details`/`received_amount`).
- **Unnecessary migration** proposed on `transaction_headers` — equivalents already exist; remove.
- **SAL-002 logic too loose** — comparing invoice total to the sum of *all* debit entries is wrong; it must target the **accounts-receivable (customer) account** leg, otherwise tax/round-off legs cause false mismatches.
- **No safety baked into the connection layer** — read-only/`statement_timeout`/app-name are described as aspirations, not enforced in code.
- **Rule metadata duplicated** in both config JSON *and* the `data_integrity_rules` table, with no stated source of truth → drift.

### Missing pieces (add)
- **Cross-DB batching engine** (needed in v0.1, not v0.5).
- **Enforced** `statement_timeout`, command timeout, `transaction_read_only`, `Application Name`.
- **Finding lifecycle** beyond a status column: upsert-by-hash, auto-resolution, first/last-seen tracking.
- **Per-rule execution record** (timing, rows scanned, error) for performance tracking and partial-failure visibility.
- **Rule cost classification** (Light/Heavy) to drive scheduling.
- **`@ExecutionTimeUtc` plumbing** and a **grace window** to suppress eventual-consistency false positives.

### Risks
- **Production primary overload** from heavy `GROUP BY`/`SUM`/`JOIN`.
- **Join-key type mismatch** (`transaction_headers.id` is **bigint**; invoice ids are **uuid**). Code and SQL must respect this — `transaction_id` is the bigint join key, not the invoice uuid.
- **Eventual consistency**: Sales posts to Common asynchronously (event-driven, `TransactionPostedEventConsumer`). An invoice approved seconds ago may legitimately have no transaction yet → false "missing posting" findings without a grace window.

### Over-engineering for v0.1 (defer)
Per-tenant scheduling, RazorLight templating, Slack/email notifications, repair-suggestion workflow, the full Admin UI.

### Under-engineering (invest more)
Production safety and cross-DB consistency are under-specified relative to the risk of running SUM/GROUP BY against a live accounting primary.

---

## 2. Improved Architecture

Three clean layers, rule-type-agnostic runner.

```
                   ┌─────────────────────────────────────────────┐
   CLI (Console) → │  RuleRunner (orchestration)                 │
                   │   • creates run, picks rules, fans out,     │
                   │     aggregates findings, finalizes run      │
                   └───────┬─────────────────────────┬───────────┘
                           │                         │
            ┌──────────────▼─────────┐   ┌───────────▼───────────────┐
            │ SameDatabaseSqlRule    │   │ MultiDatabaseCSharpRule    │
            │ runs a .sql file vs    │   │ keyset-batched IAsyncEnum  │
            │ ONE *ReadDb            │   │ from N *ReadDb, compares   │
            └──────────────┬─────────┘   │ in C#                      │
                           │             └───────────┬───────────────┘
                           ▼                         ▼
              ┌──────────────────────────────────────────────────┐
              │ IConnectionFactory → GUARDED NpgsqlConnection     │
              │  *ReadDb : transaction_read_only=on,              │
              │            statement_timeout, App Name=DataTula   │
              │  DiagnosticsWriteDb : the only writable conn      │
              └───────────────────────┬──────────────────────────┘
                                       ▼
                         IRunRepository → Diagnostics DB
                                       ▼
                         IReportGenerator → HTML + JSON
```

Key principles:
- **Connection layer is the safety boundary.** Every operational connection is read-only and time-boxed *by construction*; nothing downstream can accidentally write to a service DB. Only `DiagnosticsWriteDb` permits writes.
- **One finding shape** (`DataIntegrityFinding`) regardless of rule type → uniform persistence and reporting.
- **Rules are data + small code.** Same-DB rules are `.sql` files; cross-DB rules are small C# classes implementing one interface. The runner doesn't care which.

---

## 3. Final Project Structure

```text
dgtula-tools/
  data-tula/
    src/
      DataTula.Console/        # CLI entrypoint, arg parsing, host wiring, exit codes
      DataTula.Core/           # models, abstractions, RuleRunner, hashing (no DB/IO deps)
      DataTula.Postgres/       # IConnectionFactory (guarded), SqlRuleExecutor, RunRepository, batch readers
      DataTula.Rules.Sales/    # MultiDatabaseCSharpRule classes for SAL-001..004
      DataTula.Reports/        # HTML + JSON generators
      DataTula.Tests/          # xUnit
    rules/
      sales/                   # same-DB .sql rules (SAL-005)
      accounting/              # same-DB .sql rules vs Common (ACC-001/002)
      purchase/ inventory/ payroll/ common/ cross-database/   # placeholders
    database/
      diagnostics_schema.sql   # diagnostics DB schema (the only writable DB)
      readonly_user.sql        # GRANT-only role for service DBs (run by a DBA per service DB)
    config/
      data-tula.qa.example.json
      data-tula.prod.example.json
    reports/                   # run output: reports/yyyyMMdd-HHmmss/{report.html,report.json}
    logs/
    docs/
    README.md
```

Changes vs original: split `DataTula.Rules` into `Rules.Sales` (C# cross-DB rules live with the module they serve), add `database/readonly_user.sql`, and make explicit that **no schema-comparison code from db-tula is reused** — only conventions (report style, Serilog setup, Jenkins approach).

---

## 4. Database Design (Diagnostics DB)

`dhanman_diagnostics_qa` / `dhanman_diagnostics_prod`, schema `diagnostics`. This is the **only** DB Data Tula writes to.

```sql
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS diagnostics;

-- Rule catalog (source of truth for rule metadata; config JSON only points to it / overrides enablement)
CREATE TABLE IF NOT EXISTS diagnostics.data_integrity_rules (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    rule_code       varchar(50)  NOT NULL UNIQUE,
    rule_name       varchar(250) NOT NULL,
    module_name     varchar(100) NOT NULL,
    category        varchar(100),
    rule_type       varchar(40)  NOT NULL,           -- SameDatabaseSqlRule | MultiDatabaseCSharpRule
    databases       text[]       NOT NULL DEFAULT '{}',  -- e.g. {SalesDb} or {SalesDb,CommonDb}
    severity        varchar(20)  NOT NULL,           -- Critical | High | Medium | Low
    cost_class      varchar(20)  NOT NULL DEFAULT 'Light', -- Light | Heavy
    sql_file_path   text,                            -- null for C# rules
    handler_type    text,                            -- C# type name for C# rules
    description     text,
    is_active       boolean      NOT NULL DEFAULT true,
    created_on_utc  timestamptz  NOT NULL DEFAULT now(),
    modified_on_utc timestamptz
);

-- One row per execution of the tool
CREATE TABLE IF NOT EXISTS diagnostics.data_integrity_runs (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    run_type           varchar(40)  NOT NULL,        -- Manual | Scheduled
    environment        varchar(20)  NOT NULL,        -- qa | prod
    module_name        varchar(100),
    run_timestamp_utc  timestamptz  NOT NULL,        -- the single @ExecutionTimeUtc cutoff for the whole run
    started_on_utc     timestamptz  NOT NULL DEFAULT now(),
    completed_on_utc   timestamptz,
    status             varchar(40)  NOT NULL,        -- Running|Completed|CompletedWithFindings|CompletedWithRuleErrors|Failed
    git_sha            varchar(64),
    host_name          varchar(150),
    replica_used       boolean      NOT NULL DEFAULT false,
    total_rules        int NOT NULL DEFAULT 0,
    executed_rules     int NOT NULL DEFAULT 0,
    failed_rules       int NOT NULL DEFAULT 0,
    total_findings     int NOT NULL DEFAULT 0,
    critical_count     int NOT NULL DEFAULT 0,
    high_count         int NOT NULL DEFAULT 0,
    medium_count       int NOT NULL DEFAULT 0,
    low_count          int NOT NULL DEFAULT 0,
    company_id         uuid,
    organization_id    uuid,
    from_date          timestamptz,
    to_date            timestamptz,
    report_file_path   text,
    json_file_path     text,
    summary_json       jsonb,
    error_message      text
);

-- Per-rule-per-run execution telemetry (timing, rows scanned, partial failures)
CREATE TABLE IF NOT EXISTS diagnostics.data_integrity_rule_executions (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id             uuid NOT NULL REFERENCES diagnostics.data_integrity_runs(id),
    rule_code          varchar(50) NOT NULL,
    status             varchar(40) NOT NULL,         -- Succeeded | Failed | Skipped
    rows_scanned       bigint,
    finding_count      int NOT NULL DEFAULT 0,
    rule_execution_ms  int,
    error_message      text,
    started_on_utc     timestamptz NOT NULL DEFAULT now(),
    completed_on_utc   timestamptz
);

-- Findings with lifecycle (upsert by hash across runs)
CREATE TABLE IF NOT EXISTS diagnostics.data_integrity_findings (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    finding_hash      varchar(64) NOT NULL,
    rule_code         varchar(50) NOT NULL,
    module_name       varchar(100) NOT NULL,
    category          varchar(100),
    severity          varchar(20) NOT NULL,
    organization_id   uuid,
    company_id        uuid,
    source_type       varchar(100),
    source_id         varchar(150),
    source_number     varchar(150),
    related_type      varchar(100),
    related_id        varchar(150),
    related_number    varchar(150),
    message           text NOT NULL,
    expected_value    text,
    actual_value      text,
    difference_value  numeric(18,2),
    status            varchar(20) NOT NULL DEFAULT 'Open',  -- Open|Acknowledged|Ignored|Resolved
    first_seen_run_id uuid NOT NULL REFERENCES diagnostics.data_integrity_runs(id),
    last_seen_run_id  uuid NOT NULL REFERENCES diagnostics.data_integrity_runs(id),
    first_seen_on_utc timestamptz NOT NULL DEFAULT now(),
    last_seen_on_utc  timestamptz NOT NULL DEFAULT now(),
    resolved_on_utc   timestamptz,
    note              text,
    CONSTRAINT uq_finding_hash UNIQUE (finding_hash)
);

CREATE INDEX IF NOT EXISTS ix_findings_status      ON diagnostics.data_integrity_findings(status);
CREATE INDEX IF NOT EXISTS ix_findings_rule_code   ON diagnostics.data_integrity_findings(rule_code);
CREATE INDEX IF NOT EXISTS ix_findings_company     ON diagnostics.data_integrity_findings(company_id);
CREATE INDEX IF NOT EXISTS ix_findings_last_seen   ON diagnostics.data_integrity_findings(last_seen_run_id);
CREATE INDEX IF NOT EXISTS ix_findings_source      ON diagnostics.data_integrity_findings(source_type, source_id);
CREATE INDEX IF NOT EXISTS ix_runs_started         ON diagnostics.data_integrity_runs(started_on_utc DESC);
CREATE INDEX IF NOT EXISTS ix_ruleexec_run         ON diagnostics.data_integrity_rule_executions(run_id);
```

**Severity model:** Critical (financial integrity broken — missing/duplicate posting, amount mismatch), High (settlement/payment mismatch), Medium (referential inconsistency that isn't financially material), Low (informational).

**Status / lifecycle model:**
- A finding is keyed by `finding_hash` (see §17 of original; inputs corrected below).
- On each run, the runner computes the current finding set per rule and **upserts**:
  - New hash → insert with `status='Open'`, `first_seen_run_id = last_seen_run_id = thisRun`.
  - Existing hash still failing → update `last_seen_run_id`, `last_seen_on_utc`.
  - Existing **Open** hash **not** present in this run's results for a rule that *did* execute → **auto-resolve** (`status='Resolved'`, `resolved_on_utc=now()`).
- `Acknowledged` / `Ignored` are set by a human (future UI); the runner never overwrites a human-set `Ignored` back to Open (it re-opens only `Resolved`→`Open` if it recurs).

This answers "**how are old findings marked resolved**": automatically, by absence in the latest successful run of their owning rule — never by a successful run that *errored* (so an errored rule can't false-resolve).

---

## 5. C# Framework Design (.NET 9, raw Npgsql, no EF, no Dapper)

```csharp
// ---- Core models ----
public sealed record RunContext(
    Guid RunId,
    string Environment,
    DateTime RunTimestampUtc,     // single cutoff for the whole run
    Guid? CompanyId,
    Guid? OrganizationId,
    DateTime? FromDate,
    DateTime? ToDate,
    int GraceWindowMinutes,       // suppress in-flight async-posting false positives
    bool ReplicaUsed);

public sealed class DataIntegrityFinding { /* fields per §4 findings table */ }

public sealed class RuleDefinition {
    public string RuleCode = "";
    public string RuleType = "";          // SameDatabaseSqlRule | MultiDatabaseCSharpRule
    public string[] Databases = [];
    public string Severity = "High";
    public string CostClass = "Light";
    public string? SqlFile;
    public string? HandlerType;
    public bool Enabled = true;
}

// ---- Abstractions ----
public interface IRule {
    RuleDefinition Definition { get; }
    IAsyncEnumerable<DataIntegrityFinding> ExecuteAsync(RuleExecutionContext ctx, CancellationToken ct);
}

public sealed class RuleExecutionContext {
    public required RunContext Run { get; init; }
    public required IConnectionFactory Connections { get; init; }
    public int BatchSize { get; init; } = 5000;
    public int MaxFindings { get; init; } = 10_000;
    public int CommandTimeoutSeconds { get; init; } = 60;
    public int StatementTimeoutMs { get; init; } = 60_000;
}

public interface IConnectionFactory {
    // GUARDED: returns an opened connection with read_only + statement_timeout + App Name applied.
    Task<NpgsqlConnection> OpenReadAsync(string logicalDb, RuleExecutionContext ctx, CancellationToken ct);
    Task<NpgsqlConnection> OpenDiagnosticsWriteAsync(CancellationToken ct);
}

public interface ISqlRuleExecutor {        // drives SameDatabaseSqlRule
    IAsyncEnumerable<DataIntegrityFinding> ExecuteAsync(RuleDefinition rule, RuleExecutionContext ctx, CancellationToken ct);
}

public interface IBatchReader<T> {          // keyset pagination, batch=5000
    IAsyncEnumerable<IReadOnlyList<T>> ReadBatchesAsync(NpgsqlConnection conn, RuleExecutionContext ctx, CancellationToken ct);
}

public interface IRunRepository {
    Task<Guid> CreateRunAsync(RunContext ctx, CancellationToken ct);
    Task RecordRuleExecutionAsync(Guid runId, RuleExecutionResult r, CancellationToken ct);
    Task UpsertFindingsAsync(Guid runId, string ruleCode, IReadOnlyList<DataIntegrityFinding> findings, CancellationToken ct);
    Task AutoResolveAbsentAsync(Guid runId, string ruleCode, CancellationToken ct);
    Task CompleteRunAsync(Guid runId, DataTulaRunResult result, string status, CancellationToken ct);
    Task FailRunAsync(Guid runId, Exception ex, CancellationToken ct);
}

public interface IReportGenerator {
    Task<string> GenerateHtmlAsync(DataTulaRunResult r, CancellationToken ct);
    Task<string> GenerateJsonAsync(DataTulaRunResult r, CancellationToken ct);
}

public interface IRuleRunner {
    Task<DataTulaRunResult> RunAsync(DataTulaRunRequest req, CancellationToken ct);
}

public static class FindingHashService { public static string CreateHash(DataIntegrityFinding f) => /* SHA-256 */ ""; }
```

Guarded connection (the safety boundary), conceptually:

```csharp
public async Task<NpgsqlConnection> OpenReadAsync(string logicalDb, RuleExecutionContext ctx, CancellationToken ct) {
    var csb = new NpgsqlConnectionStringBuilder(_strings[logicalDb]) {
        ApplicationName = "DataTula",
        CommandTimeout  = ctx.CommandTimeoutSeconds
    };
    var conn = new NpgsqlConnection(csb.ConnectionString);
    await conn.OpenAsync(ct);
    await using var c = new NpgsqlCommand(
        $"SET statement_timeout = {ctx.StatementTimeoutMs}; SET default_transaction_read_only = on;", conn);
    await c.ExecuteNonQueryAsync(ct);
    return conn;     // any accidental write now fails at the server
}
```

---

## 6. Rule Types — when to use which

**Decision rule:** *Do all columns a rule needs live in exactly one service database?*

| Answer | Rule type | Mechanism |
|---|---|---|
| Yes | `SameDatabaseSqlRule` | One `.sql` file, parameterized, executed against one `*ReadDb`; rows map 1:1 to findings. |
| No (spans DBs) | `MultiDatabaseCSharpRule` | Small C# class. Keyset-batched stream from each DB; compare in memory; emit findings. **No FDW/dblink** in v0.1. |

Applied to the flagship use case:

| Rule | Type | Why |
|---|---|---|
| SAL-001 invoice has exactly one transaction | **C# (Sales+Common)** | invoice in Sales, transaction in Common |
| SAL-002 invoice total = AR ledger posting | **C# (Sales+Common)** | journal_entries in Common |
| SAL-003 customer_id matches | **C# (Sales+Common)** | transaction_headers in Common |
| SAL-004 company_id matches | **C# (Sales+Common)** | transaction_headers in Common |
| SAL-005 settled_amount = Σ payments | **SQL (Sales only)** | all in Sales DB |
| ACC-002 journal debits = credits (per txn) | **SQL (Common only)** | all in Common DB |

---

## 7. First Use Case — Sales Invoice → Ledger (corrected)

### 7.1 Data assumptions (live-verified)
- Invoices: Sales DB `public.invoice_headers`. Approved ⇔ `invoice_status_id = 3`; consider also 4 (PARTIALLY_PAID) and 5 (PAID). **Posted ⇔ a Common transaction exists for the invoice via `document_id`** (NOT `transaction_id IS NOT NULL` — that column is null for ~90% of posted invoices).
- AR/revenue legs are **on the invoice**: `invoice_headers.debit_account_id` (receivable) and `credit_account_id` (revenue). SAL-002 compares the invoice total to the journal `D` amount on `debit_account_id` — no account-group guessing needed.
- Payments: Sales DB `invoice_payment_headers` / `invoice_payment_details` (`received_amount`, `tds_amount`); payment header links to ledger via `transaction_source_type = 3`.
- Ledger: Common DB `transaction_headers` (bigint `id`, `is_reversed`) + `journal_entries` (`entry_type` 'D'/'C', `amount` ≥ 0).
- **Authoritative link:** `transaction_headers.document_id = invoice.id` AND `transaction_source_type = 1`.

### 7.2 Pre-coding verification checklist (re-confirm against the target env before finalizing)
*(All five were confirmed in QA on 2026-06-04; re-run per environment.)*
1. ✅ Authoritative link is `transaction_headers.document_id` + `transaction_source_type=1` (forward `transaction_id` mostly NULL).
2. ✅ Source-type codes: sales invoice = 1, invoice payment = 3 (`transaction_source_types` lookup).
3. ✅ AR ledger leg = journal `D` entries on `invoice_headers.debit_account_id`.
4. ✅ `journal_entries.amount` is always ≥ 0; sign carried by `entry_type`.
5. ✅ `transaction_headers` has `is_deleted` + `is_reversed`; exclude reversed when matching active invoices (handle REVERSE invoices separately).
> **Stop-and-report gate:** if any of these does NOT hold in the target environment, halt and report rather than guess.

### 7.3 Cross-DB algorithm (SAL-001..004,006..008), batched
```
runTs = RunContext.RunTimestampUtc
cutoff = runTs - GraceWindowMinutes      # ignore very-recently-approved invoices (async posting)

stream posted-status invoices from Sales (keyset by id, batch 5000):
  SELECT id, invoice_number, customer_id, company_id, total_amount, transaction_id,
         debit_account_id, is_posted
  FROM public.invoice_headers
  WHERE is_deleted = false
    AND invoice_status_id IN (3,4,5)          -- approved/partially-paid/paid
    AND created_on_utc <= @Cutoff
    AND (@CompanyId IS NULL OR company_id = @CompanyId)
    AND (@FromDate   IS NULL OR invoice_date >= @FromDate)
    AND (@ToDate     IS NULL OR invoice_date <= @ToDate)
    AND id > @LastId ORDER BY id LIMIT 5000

for each batch:
  docIds = batch.Select(id)               # join on the AUTHORITATIVE reverse link
  load Common transaction_headers WHERE transaction_source_type=1 AND document_id = ANY(@docIds)
       -> group by document_id  (count: 0 / 1 / >1 ; capture txn.id, company_id, customer_id, is_reversed)
  load Common journal sums per txn (and the D-sum on each invoice's debit_account_id)
  for each invoice:
    SAL-001: 0 non-reversed txns                              -> Critical "missing posting"
    SAL-007: >1 non-reversed txns for the document_id         -> Critical "duplicate posting"
    SAL-002: |invoice.total_amount - AR-D-sum on debit_account_id| > tol -> Critical (diff)
    SAL-003: txn.customer_id != invoice.customer_id           -> Critical
    SAL-004: txn.company_id  != invoice.company_id            -> Critical
    SAL-008: txn exists but invoice.transaction_id IS NULL/<>  -> High "broken back-link"
```
`MaxFindings` caps emission per rule; once hit, stop and flag the run as truncated.
> See [data-tula-rule-catalog.md §2](data-tula-rule-catalog.md) — this is the generic Document→Ledger template; Purchase (source_type 2) and Payroll (source_type 5) reuse it verbatim.

### 7.4 SAL-005 (same-DB SQL, corrected)
```sql
-- rules/sales/SAL-005_invoice_settled_amount_must_match_payments.sql
WITH payment_summary AS (
    SELECT ih.id AS invoice_id, ih.invoice_number, ih.company_id,
           ih.settled_amount,
           COALESCE(SUM(ipd.received_amount), 0) AS payment_total
    FROM public.invoice_headers ih
    LEFT JOIN public.invoice_payment_details ipd
           ON ipd.invoice_header_id = ih.id AND ipd.is_deleted = false
    WHERE ih.is_deleted = false
      AND ih.invoice_status_id IN (4,5)                 -- partially-paid / paid
      AND ih.created_on_utc <= @ExecutionTimeUtc
      AND (@CompanyId::uuid IS NULL OR ih.company_id = @CompanyId::uuid)
      AND (@FromDate::timestamp IS NULL OR ih.invoice_date >= @FromDate::timestamp)
      AND (@ToDate::timestamp   IS NULL OR ih.invoice_date <= @ToDate::timestamp)
    GROUP BY ih.id, ih.invoice_number, ih.company_id, ih.settled_amount
)
SELECT 'SAL-005' AS rule_code, 'Sales' AS module_name, 'SalesInvoiceLedger' AS category,
       'High' AS severity, NULL::uuid AS organization_id, company_id,
       'SalesInvoice' AS source_type, invoice_id::text AS source_id, invoice_number AS source_number,
       'InvoicePayment' AS related_type, NULL::text AS related_id, NULL::text AS related_number,
       'Invoice settled_amount must equal sum of invoice payment detail received_amount.' AS message,
       settled_amount::text AS expected_value, payment_total::text AS actual_value,
       settled_amount - payment_total AS difference_value
FROM payment_summary
WHERE settled_amount <> payment_total;
```

### 7.5 Standard result columns / parameters / safety filters
- **Columns:** `rule_code, module_name, category, severity, organization_id, company_id, source_type, source_id, source_number, related_type, related_id, related_number, message, expected_value, actual_value, difference_value`.
- **Parameters (every rule):** `@ExecutionTimeUtc, @CompanyId, @OrganizationId, @FromDate, @ToDate`.
- **Safety filters:** `is_deleted=false`; status filter; `created_on_utc <= @ExecutionTimeUtc` (and grace cutoff for cross-DB); company/date filters mandatory for Heavy rules.

### 7.6 Expected findings & limitations
- Expected: unposted approved invoices, amount mismatches (often tax/round-off → motivates AR-account targeting), customer/company drift, settlement mismatches.
- Limitations: cross-DB is not a perfect snapshot (eventual consistency → grace window); SAL-002 accuracy depends on correct AR-account identification; reversed/cancelled flows need explicit handling once their semantics are confirmed.

---

## 8. Production Safety Design

- **Replica-ready connection naming:** `SalesReadDb`, `CommonReadDb`, `DiagnosticsWriteDb`. Default to primary; flip a connection to a replica when one exists. `runs.replica_used` records what was used.
- **Read-only DB user** (`database/readonly_user.sql`): a `datatula_ro` role with `GRANT SELECT` only, `ALTER ROLE datatula_ro SET default_transaction_read_only = on`. Belt-and-suspenders with the connection-layer `SET`.
- **Time-boxing:** per-rule `statement_timeout` (server) + Npgsql `CommandTimeout` (client). Heavy rules get a longer, explicit budget; Light rules a short one.
- **Caps:** `MaxFindings` per rule; `BatchSize = 5000`; keyset pagination (no `OFFSET`).
- **Cost classification:** `cost_class` Light/Heavy in the rule catalog. Heavy rules run **night-only** (scheduling) and **require** company/date scoping.
- **`Application Name=DataTula`** on every connection → visible in `pg_stat_activity` for DBAs to spot/kill.
- **Tenant/date scoping** strongly encouraged for prod; nightly full scans only off-peak.

---

## 9. Execution Consistency Design

- **`RunTimestampUtc`**: computed once at run start, stored on the run, passed as `@ExecutionTimeUtc` to every rule. All rules see the same logical "as-of" time.
- **Same-DB rules:** wrap reads in a `REPEATABLE READ READ ONLY` transaction → internally consistent snapshot per DB.
- **Cross-DB rules:** perfect multi-DB snapshots are impossible without distributed snapshots (out of scope). Mitigations:
  - **Grace window** (`GraceWindowMinutes`, default e.g. 15): exclude invoices created/approved after `RunTimestampUtc - grace`, so in-flight async postings don't read as "missing".
  - **Cutoff everywhere:** both DBs filtered to `<= @ExecutionTimeUtc`.
  - **Re-confirm before flagging Critical:** for "missing transaction", optionally re-query Common once at end-of-rule for the small candidate set to drop races.
- **Replica lag:** when reading a replica, treat `RunTimestampUtc` minus expected lag as the effective cutoff; record `replica_used=true` so findings are interpreted accordingly. This **reduces false positives** materially.

---

## 10. Reporting Design

- **HTML** (`reports/yyyyMMdd-HHmmss/report.html`): header (env/module/run-type/timestamps/status), run-metadata, summary cards (rules/executed/failed/findings + per-severity), rule summary table (with timing + rows scanned from `rule_executions`), findings table (severity, rule, company, source number, message, expected/actual/diff), and a rule-errors section.
- **JSON** (`report.json`): same data, machine-readable, for downstream/UI ingestion.
- **Exit codes:** `0` = no Critical findings; `1` = Critical findings present; `2` = tool/runtime failure (and also if a rule errored *and* policy says errors are fatal).
- **Jenkins artifacts:** archive `reports/**`; use HTML Publisher for the latest report.

---

## 11. Jenkins / CI Design

```groovy
pipeline {
  agent any
  stages {
    stage('Restore') { steps { sh 'dotnet restore data-tula/DataTula.sln' } }
    stage('Build')   { steps { sh 'dotnet build -c Release --no-restore data-tula/DataTula.sln' } }
    stage('Run Data Tula') {
      steps {
        withCredentials([ /* DATATULA_CONNECTIONS__SALESDB_QA, __COMMONDB_QA, __DIAGNOSTICSDB_QA */ ]) {
          sh 'dotnet run -c Release --project data-tula/src/DataTula.Console -- run --env qa --module sales'
        }
      }
    }
  }
  post {
    always   { archiveArtifacts artifacts: 'data-tula/reports/**/*.*', fingerprint: true
               publishHTML(/* latest report.html */) }
    // exit 1 (critical findings) -> mark UNSTABLE; exit 2 -> FAILURE
  }
}
```
- **Env-scoped credentials** via `DATATULA_CONNECTIONS__*` Jenkins secrets; **QA first**, Production added only after QA is trusted.
- **Heavy rules** run on a separate **nightly** schedule, not on every build.

---

## 12. Implementation Plan (phased)

- **v0.1 (this scope):** CLI + config + **separate Diagnostics DB** + schema + **guarded connection factory** + SQL executor + **cross-DB batch engine** + **SAL-001..005 (corrected)** + ACC-002 + HTML/JSON reports + README + tests.
- **v0.2:** Jenkins nightly schedule + read-replica support + report publishing/retention.
- **v0.3:** Purchase (PUR-001..005 — bill ↔ Common ledger; also **cross-DB**).
- **v0.4:** Inventory / product-hierarchy completeness rules.
- **v0.5:** More multi-DB party/mapping rules (COM-001..003: customer/vendor ↔ `parties` by GSTIN).
- **v1.0:** Data Integrity Center UI — review / acknowledge / ignore / resolve workflow + repair suggestions with **manual approval** (never auto-fix).

---

## 13. Agent-Ready v0.1 Implementation Prompt

```text
Implement Data Tula v0.1 — a business-data reconciliation tool for the Dgtula/Dhanman platform.
It is SEPARATE from db-tula (db-tula = schema comparison; do NOT touch or reuse its schema-compare code).

HARD RULES
- Create a branch first: feature/data-tula-v0.1
- Do NOT modify db-tula.
- Do NOT use EF Core. Do NOT use Dapper. Use raw Npgsql (NpgsqlConnection/Command/DataReader) only.
- Operational service DBs are READ-ONLY. Every operational connection must, on open, run:
  SET statement_timeout = <ms>; SET default_transaction_read_only = on;  and set Application Name=DataTula.
- The ONLY writable DB is the separate Diagnostics DB (dhanman_diagnostics_{env}).

VERIFIED SCHEMA — USE THESE NAMES (confirmed live against QA db.qa.dgtula.com, 2026-06-04):
- Sales DB (public): invoice_headers(id uuid, invoice_number, customer_id, company_id, total_amount,
  settled_amount, invoice_date, invoice_status_id int, payment_status_id int, is_posted bool,
  transaction_id bigint NULL, debit_account_id uuid, credit_account_id uuid, is_deleted, created_on_utc);
  invoice_payment_headers; invoice_payment_details(invoice_header_id, received_amount, tds_amount, is_deleted).
- Common DB (public): transaction_headers(id BIGINT, company_id, customer_id, vendor_id, employee_id,
  transaction_source_type int, document_id uuid, document_number, status_id, is_reversed bool, is_deleted);
  journal_entries(id, transaction_id, account_id uuid, entry_type char 'D'/'C', amount numeric>=0, is_deleted).
- transaction_source_type codes: 1=Invoice, 2=Bill, 3=Invoice Payment, 4=Bill Payment, 5=Salary Posting, 6=Salary Payment.
- Invoice status: APPROVED=3, PARTIALLY_PAID=4, PAID=5.
- AUTHORITATIVE LINK = transaction_headers.document_id = invoice.id AND transaction_source_type=1.
  The forward invoice.transaction_id is NULL for ~90% of posted invoices — DO NOT join on it; a non-null
  Common txn while invoice.transaction_id IS NULL is itself a finding (SAL-008, High).
- AR ledger leg for SAL-002 = journal D entries on invoice.debit_account_id (account is on the invoice; no group lookup).
- journal amounts are always >= 0; sign is in entry_type. Exclude transaction_headers.is_reversed=true when matching active docs.
- DO NOT add source_type/source_id/source_number columns to transaction_headers — equivalents already exist.

STOP-AND-REPORT GATE
The five facts above were verified in QA. Re-verify them in the TARGET environment before finalizing SQL
(authoritative link direction, source-type codes, AR leg = debit_account_id, amount sign, is_reversed handling).
If ANY does not hold, STOP and report — do not guess.

BUILD
1. Solution dgtula-tools/data-tula with projects: DataTula.Console, DataTula.Core, DataTula.Postgres,
   DataTula.Rules.Sales, DataTula.Reports, DataTula.Tests.
2. database/diagnostics_schema.sql (rules, runs, rule_executions, findings — per the review doc) and
   database/readonly_user.sql (GRANT SELECT + default_transaction_read_only role).
3. Guarded IConnectionFactory; multi-DB config (SalesReadDb, CommonReadDb, DiagnosticsWriteDb) via JSON +
   DATATULA_CONNECTIONS__* env overrides.
4. ISqlRuleExecutor for SameDatabaseSqlRule; keyset-batched (batch=5000) IBatchReader for MultiDatabaseCSharpRule.
5. RuleRunner: single RunTimestampUtc -> @ExecutionTimeUtc to all rules; grace window for cross-DB;
   upsert findings by finding_hash; auto-resolve absent findings; per-rule execution telemetry.
6. Rules (join on the authoritative reverse link transaction_headers.document_id + transaction_source_type):
   - SAL-005 (same-DB SQL, Sales): settled_amount vs SUM(invoice_payment_details.received_amount).
   - SAL-010/011 (same-DB SQL, Sales): tax components sum to total; payment details roll up to header.
   - ACC-001 (same-DB SQL, Common): per non-reversed transaction, SUM debit = SUM credit.
   - SAL-001..004,006..008 (C# cross-DB Sales+Common): existence (missing posting), duplicate posting (>1),
     total vs AR D-leg on debit_account_id, customer_id match, company_id match, invoice-payment posting,
     broken back-link (Common txn exists but invoice.transaction_id IS NULL).
   - See data-tula-rule-catalog.md for the full catalog (PUR/PAY/INV/COM/BNK reuse the §2 template).
7. HtmlReportGenerator + JsonReportGenerator -> reports/yyyyMMdd-HHmmss/.
8. README (purpose, setup, config, run command, exit codes). Tests where practical (hashing, row->finding
   mapping, batch reader, finding lifecycle upsert/auto-resolve).

ACCEPTANCE
  dotnet run --project src/DataTula.Console -- run --env qa --module sales
must: load config; connect Sales(read-only)+Common(read-only)+Diagnostics(write); create one run row;
execute SAL-001..005 + ACC-002; upsert findings; write HTML+JSON; finalize run; return exit code
(0 none-critical / 1 critical / 2 runtime failure). NEVER write to Sales or Common. NEVER auto-fix data.
```

---

## 14. Key Questions — answered explicitly

1. **Diagnostics in Common DB or separate Diagnostics DB?** → **Separate** `dhanman_diagnostics_{qa,prod}` from day one. It's the only writable DB; keeps every service DB cleanly read-only and avoids mixing tool writes into an operational accounting DB.
2. **v0.1 SQL-only, or C# rules too?** → **Both, day one.** Forced by reality: the flagship invoice↔ledger use case spans Sales+Common, so the cross-DB C# engine is required now. SQL rules cover the genuinely same-DB checks (SAL-005, ACC-002).
3. **Store findings permanently or only reports?** → **Store permanently** in the Diagnostics DB with lifecycle, *and* emit reports. Persistence enables trend analysis, dedup, and the future review/resolve workflow.
4. **How are old findings marked resolved?** → **Auto-resolution by absence**: a finding `Open` in a prior run but absent from the latest *successful* run of its owning rule is set `Resolved`. Human `Ignored`/`Acknowledged` states are preserved; recurrence re-opens only `Resolved`.
5. **Should critical findings fail Jenkins?** → Critical findings → exit `1` → mark build **UNSTABLE** (visible, not blocking deploys); tool/runtime failure → exit `2` → **FAILURE**. (Promote to hard-fail later if desired.)
6. **How to prevent heavy rules hurting production?** → Read-only user + replica-ready connections, enforced `statement_timeout`/command timeout, `MaxFindings` cap, keyset batching, `cost_class`-driven night-only scheduling, mandatory company/date scoping, `Application Name=DataTula` for DBA visibility.
7. **How do multi-DB rules avoid memory problems?** → **Keyset-paginated `IAsyncEnumerable` batches (5000)**; never load whole tables; fetch the matching counterpart rows per batch by key set (`= ANY(@ids)`); stream findings out; cap with `MaxFindings`.
8. **Minimum viable v0.1 with real value?** → SAL-001..005 + ACC-002 (corrected names), guarded connections, Diagnostics DB + lifecycle, HTML/JSON reports, CLI. That already catches missing postings, amount mismatches, and settlement gaps — genuine financial-integrity value.
9. **What to deliberately postpone?** → Admin UI, repair suggestions/auto-fix, Slack/email, per-tenant scheduling, RazorLight, FDW/dblink, Purchase/Inventory/Payroll/Party rules.
10. **What would a 30-year architect change?** → (a) **Fix the cross-DB modeling** — the flagship is cross-database; design for it now. (b) **Bake safety into the connection layer** so read-only/timeout/app-name are structural, not conventions. (c) **Add a real finding lifecycle** (upsert-by-hash + auto-resolution + per-rule telemetry) instead of a bare status column. (d) **Single source of truth for rule metadata** (the catalog table), not duplicated in config. (e) **Target the AR account in SAL-002**, not all debits, and add a **grace window** for eventual consistency. (f) **Drop the proposed `transaction_headers` migration** — the linkage already exists.
```

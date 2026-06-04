# Data Tula Detailed Implementation Plan

## 1. Objective

Build **Data Tula** as a separate business-data reconciliation framework under the Dgtula ecosystem.

Data Tula must validate whether business data is correct, complete, traceable, and reconciled across Dgtula service databases.

It must remain separate from **db-tula**.

```text
db-tula   = schema comparison
           tables, columns, functions, procedures, indexes, keys

data-tula = business data reconciliation
           invoices, bills, payments, ledgers, products, payroll, party mapping
```

The first implementation should focus on:

```text
Sales Invoice -> Transaction Header -> Journal Entries -> Invoice Payments
```

---

## 2. Key Architectural Decision

Dgtula uses separate databases for different services.

Examples:

```text
Sales DB
Purchase DB
Inventory DB
Payroll DB
Community DB
Common DB
Diagnostics DB
```

Therefore Data Tula must be designed as a **multi-database reconciliation framework** from day one.

However, Phase 1 should remain simple:

```text
Use SQL-file-based rules where all required data is in one database.
Use C#-based rules later for cross-database checks.
```

---

## 3. Separation from db-tula

Data Tula must not reuse db-tula's schema comparison logic.

Allowed sharing:

```text
Repository style
Report style
Jenkins execution approach
Logging approach
Coding conventions
HTML publishing style
```

Not allowed:

```text
No schema comparison inside data-tula
No business reconciliation inside db-tula
No mixing rule concepts
```

---

## 4. Recommended Repository Structure

Create Data Tula under the Dgtula tools area.

```text
dgtula-tools/
  db-tula/
    src/
    reports/
    docs/

  data-tula/
    src/
      DataTula.Console/
      DataTula.Core/
      DataTula.Postgres/
      DataTula.Rules/
      DataTula.Reports/
      DataTula.Tests/

    rules/
      sales/
        SAL-001_invoice_must_have_one_transaction.sql
        SAL-002_invoice_total_must_match_ledger.sql
        SAL-003_invoice_customer_must_match_transaction.sql
        SAL-004_invoice_company_must_match_transaction.sql
        SAL-005_invoice_settled_amount_must_match_payments.sql

      accounting/
        ACC-001_transaction_must_have_journal_entries.sql
        ACC-002_transaction_debit_credit_must_balance.sql

      purchase/
        .gitkeep

      inventory/
        .gitkeep

      payroll/
        .gitkeep

      common/
        .gitkeep

      cross-database/
        .gitkeep

    config/
      data-tula.qa.example.json
      data-tula.prod.example.json

    database/
      diagnostics_schema.sql

    reports/
      .gitkeep

    logs/
      .gitkeep

    docs/
      data-tula-design.md
      data-tula-rule-book.md
      data-tula-implementation-plan.md

    README.md
```

---

## 5. Technology Stack

Use:

```text
.NET 9
C#
Npgsql
Serilog
JSON configuration
Environment variable overrides
HTML report output
JSON report output
```

Optional later:

```text
Dapper
RazorLight
Jenkins HTML publisher
Slack/email notifications
Dgtula Admin UI
```

Do not use EF Core in v0.1 unless there is a strong reason.

Reason:

```text
Data Tula is a diagnostic tool.
Raw SQL is simpler, faster, transparent, and easier to tune.
```

---

## 6. Solution and Project Creation

Create the solution:

```bash
mkdir data-tula
cd data-tula

dotnet new sln -n DataTula

dotnet new console -n DataTula.Console -o src/DataTula.Console
dotnet new classlib -n DataTula.Core -o src/DataTula.Core
dotnet new classlib -n DataTula.Postgres -o src/DataTula.Postgres
dotnet new classlib -n DataTula.Rules -o src/DataTula.Rules
dotnet new classlib -n DataTula.Reports -o src/DataTula.Reports
dotnet new xunit -n DataTula.Tests -o src/DataTula.Tests

dotnet sln add src/DataTula.Console/DataTula.Console.csproj
dotnet sln add src/DataTula.Core/DataTula.Core.csproj
dotnet sln add src/DataTula.Postgres/DataTula.Postgres.csproj
dotnet sln add src/DataTula.Rules/DataTula.Rules.csproj
dotnet sln add src/DataTula.Reports/DataTula.Reports.csproj
dotnet sln add src/DataTula.Tests/DataTula.Tests.csproj
```

Add references:

```bash
dotnet add src/DataTula.Console/DataTula.Console.csproj reference src/DataTula.Core/DataTula.Core.csproj
dotnet add src/DataTula.Console/DataTula.Console.csproj reference src/DataTula.Postgres/DataTula.Postgres.csproj
dotnet add src/DataTula.Console/DataTula.Console.csproj reference src/DataTula.Reports/DataTula.Reports.csproj
dotnet add src/DataTula.Console/DataTula.Console.csproj reference src/DataTula.Rules/DataTula.Rules.csproj

dotnet add src/DataTula.Postgres/DataTula.Postgres.csproj reference src/DataTula.Core/DataTula.Core.csproj
dotnet add src/DataTula.Reports/DataTula.Reports.csproj reference src/DataTula.Core/DataTula.Core.csproj
dotnet add src/DataTula.Rules/DataTula.Rules.csproj reference src/DataTula.Core/DataTula.Core.csproj
dotnet add src/DataTula.Rules/DataTula.Rules.csproj reference src/DataTula.Postgres/DataTula.Postgres.csproj
```

Add packages:

```bash
dotnet add src/DataTula.Console package Microsoft.Extensions.Hosting
dotnet add src/DataTula.Console package Microsoft.Extensions.Configuration.Json
dotnet add src/DataTula.Console package Microsoft.Extensions.Configuration.EnvironmentVariables
dotnet add src/DataTula.Console package Serilog
dotnet add src/DataTula.Console package Serilog.Sinks.Console
dotnet add src/DataTula.Console package Serilog.Sinks.File

dotnet add src/DataTula.Postgres package Npgsql

dotnet add src/DataTula.Reports package System.Text.Json
```

---

## 7. Runtime Modes

Minimum v0.1 command:

```bash
dotnet run --project src/DataTula.Console -- run --env qa --module sales
```

Future commands:

```bash
dotnet run --project src/DataTula.Console -- run --env qa --module sales --rule SAL-001

dotnet run --project src/DataTula.Console -- run --env qa --module sales --company-id <company-id>

dotnet run --project src/DataTula.Console -- run --env qa --module all

dotnet run --project src/DataTula.Console -- run --env qa --module sales --from-date 2026-04-01 --to-date 2027-03-31
```

Exit codes:

```text
0 = run completed and no critical findings
1 = run completed but critical findings exist
2 = tool/runtime failure
```

---

## 8. Rule Types

Data Tula must support two rule types.

### 8.1 SameDatabaseSqlRule

Used when all required tables are in one service database.

Example:

```text
Sales Invoice -> Transaction Header -> Journal Entries
```

If all tables are in Sales DB, this can run as one SQL rule.

### 8.2 MultiDatabaseCSharpRule

Used when required data is across different service databases.

Example:

```text
Sales DB customer
Common DB party/company/organization mapping
```

These rules should:

```text
1. Query Sales DB
2. Query Common DB
3. Compare in memory
4. Produce normalized findings
```

Do not use PostgreSQL FDW or dblink in v0.1.

---

## 9. Diagnostics Database

Recommended final design:

```text
dhanman_diagnostics_qa
dhanman_diagnostics_prod
```

For v0.1, if creating a new database is too much, diagnostics tables can temporarily live in Common DB under the `diagnostics` schema.

Recommended:

```text
Use a separate Diagnostics DB as early as possible.
```

---

## 10. Diagnostics Schema

Create file:

```text
database/diagnostics_schema.sql
```

SQL:

```sql
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE SCHEMA IF NOT EXISTS diagnostics;

CREATE TABLE IF NOT EXISTS diagnostics.data_integrity_rules (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    rule_code varchar(50) NOT NULL UNIQUE,
    rule_name varchar(250) NOT NULL,
    module_name varchar(100) NOT NULL,
    category varchar(100) NULL,

    rule_type varchar(50) NOT NULL DEFAULT 'SameDatabaseSqlRule',
    database_name varchar(100) NULL,
    severity varchar(50) NOT NULL,
    description text NULL,

    sql_file_path text NULL,
    is_active boolean NOT NULL DEFAULT true,

    created_on_utc timestamp NOT NULL DEFAULT now(),
    modified_on_utc timestamp NULL
);

CREATE TABLE IF NOT EXISTS diagnostics.data_integrity_runs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    run_type varchar(50) NOT NULL,
    environment varchar(50) NOT NULL,
    module_name varchar(100) NULL,

    started_on_utc timestamp NOT NULL DEFAULT now(),
    completed_on_utc timestamp NULL,

    status varchar(50) NOT NULL,

    total_rules int NOT NULL DEFAULT 0,
    executed_rules int NOT NULL DEFAULT 0,
    failed_rules int NOT NULL DEFAULT 0,

    total_findings int NOT NULL DEFAULT 0,
    critical_count int NOT NULL DEFAULT 0,
    high_count int NOT NULL DEFAULT 0,
    medium_count int NOT NULL DEFAULT 0,
    low_count int NOT NULL DEFAULT 0,

    company_id uuid NULL,
    organization_id uuid NULL,

    report_file_path text NULL,
    json_file_path text NULL,

    summary_json jsonb NULL,
    error_message text NULL
);

CREATE TABLE IF NOT EXISTS diagnostics.data_integrity_findings (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    run_id uuid NOT NULL,
    rule_code varchar(50) NOT NULL,
    module_name varchar(100) NOT NULL,
    category varchar(100) NULL,
    severity varchar(50) NOT NULL,

    organization_id uuid NULL,
    company_id uuid NULL,

    source_type varchar(100) NULL,
    source_id varchar(150) NULL,
    source_number varchar(150) NULL,

    related_type varchar(100) NULL,
    related_id varchar(150) NULL,
    related_number varchar(150) NULL,

    message text NOT NULL,
    expected_value text NULL,
    actual_value text NULL,
    difference_value numeric(18, 2) NULL,

    finding_hash varchar(128) NULL,

    status varchar(50) NOT NULL DEFAULT 'Open',
    created_on_utc timestamp NOT NULL DEFAULT now(),

    CONSTRAINT fk_data_integrity_findings_run
        FOREIGN KEY (run_id)
        REFERENCES diagnostics.data_integrity_runs(id)
);

CREATE INDEX IF NOT EXISTS ix_data_integrity_findings_run_id
ON diagnostics.data_integrity_findings(run_id);

CREATE INDEX IF NOT EXISTS ix_data_integrity_findings_rule_code
ON diagnostics.data_integrity_findings(rule_code);

CREATE INDEX IF NOT EXISTS ix_data_integrity_findings_company_id
ON diagnostics.data_integrity_findings(company_id);

CREATE INDEX IF NOT EXISTS ix_data_integrity_findings_source
ON diagnostics.data_integrity_findings(source_type, source_id);

CREATE INDEX IF NOT EXISTS ix_data_integrity_findings_hash
ON diagnostics.data_integrity_findings(finding_hash);

CREATE INDEX IF NOT EXISTS ix_data_integrity_runs_started_on
ON diagnostics.data_integrity_runs(started_on_utc DESC);
```

---

## 11. Source Document Traceability

Data Tula needs every financial transaction to be traceable to its business document.

Recommended columns on `transaction_headers`:

```text
source_type
source_id
source_number
```

Example:

```text
source_type   = SalesInvoice
source_id     = invoice_headers.id
source_number = INV-00000045
```

Proposed migration if missing:

```sql
ALTER TABLE public.transaction_headers
ADD COLUMN IF NOT EXISTS source_type varchar(100) NULL,
ADD COLUMN IF NOT EXISTS source_id uuid NULL,
ADD COLUMN IF NOT EXISTS source_number varchar(150) NULL;

CREATE INDEX IF NOT EXISTS ix_transaction_headers_source
ON public.transaction_headers(source_type, source_id);
```

Important:

```text
Do not blindly apply this migration.
First inspect the current Sales DB transaction_headers table.
If equivalent columns already exist, use existing columns.
```

---

## 12. Configuration Design

Create:

```text
config/data-tula.qa.example.json
```

Example:

```json
{
  "environment": "qa",
  "outputDirectory": "reports",
  "connections": {
    "salesDb": "Host=localhost;Port=5432;Database=dhanman_sales_qa;Username=postgres;Password=postgres;",
    "purchaseDb": "",
    "inventoryDb": "",
    "payrollDb": "",
    "communityDb": "",
    "commonDb": "",
    "diagnosticsDb": "Host=localhost;Port=5432;Database=dhanman_diagnostics_qa;Username=postgres;Password=postgres;"
  },
  "rules": [
    {
      "ruleCode": "SAL-001",
      "ruleName": "Approved sales invoice must have exactly one accounting transaction",
      "module": "Sales",
      "category": "SalesInvoiceLedger",
      "ruleType": "SameDatabaseSqlRule",
      "severity": "Critical",
      "database": "SalesDb",
      "sqlFile": "rules/sales/SAL-001_invoice_must_have_one_transaction.sql",
      "enabled": true
    },
    {
      "ruleCode": "SAL-002",
      "ruleName": "Sales invoice total must match ledger posting amount",
      "module": "Sales",
      "category": "SalesInvoiceLedger",
      "ruleType": "SameDatabaseSqlRule",
      "severity": "Critical",
      "database": "SalesDb",
      "sqlFile": "rules/sales/SAL-002_invoice_total_must_match_ledger.sql",
      "enabled": true
    },
    {
      "ruleCode": "SAL-003",
      "ruleName": "Sales invoice customer must match transaction customer",
      "module": "Sales",
      "category": "SalesInvoiceLedger",
      "ruleType": "SameDatabaseSqlRule",
      "severity": "Critical",
      "database": "SalesDb",
      "sqlFile": "rules/sales/SAL-003_invoice_customer_must_match_transaction.sql",
      "enabled": true
    },
    {
      "ruleCode": "SAL-004",
      "ruleName": "Sales invoice company must match transaction company",
      "module": "Sales",
      "category": "SalesInvoiceLedger",
      "ruleType": "SameDatabaseSqlRule",
      "severity": "Critical",
      "database": "SalesDb",
      "sqlFile": "rules/sales/SAL-004_invoice_company_must_match_transaction.sql",
      "enabled": true
    },
    {
      "ruleCode": "SAL-005",
      "ruleName": "Paid invoice settled amount must match payment records",
      "module": "Sales",
      "category": "SalesInvoiceLedger",
      "ruleType": "SameDatabaseSqlRule",
      "severity": "High",
      "database": "SalesDb",
      "sqlFile": "rules/sales/SAL-005_invoice_settled_amount_must_match_payments.sql",
      "enabled": true
    }
  ]
}
```

Do not commit real credentials.

Use environment variables:

```text
DATATULA_CONNECTIONS__SALESDB
DATATULA_CONNECTIONS__PURCHASEDB
DATATULA_CONNECTIONS__INVENTORYDB
DATATULA_CONNECTIONS__PAYROLLDB
DATATULA_CONNECTIONS__COMMUNITYDB
DATATULA_CONNECTIONS__COMMONDB
DATATULA_CONNECTIONS__DIAGNOSTICSDB
```

---

## 13. Core Models

### 13.1 DataTulaOptions

```csharp
namespace DataTula.Core.Configuration;

public sealed class DataTulaOptions
{
    public string Environment { get; set; } = "qa";
    public string OutputDirectory { get; set; } = "reports";
    public ConnectionOptions Connections { get; set; } = new();
    public List<RuleDefinition> Rules { get; set; } = new();
}
```

### 13.2 ConnectionOptions

```csharp
namespace DataTula.Core.Configuration;

public sealed class ConnectionOptions
{
    public string SalesDb { get; set; } = string.Empty;
    public string PurchaseDb { get; set; } = string.Empty;
    public string InventoryDb { get; set; } = string.Empty;
    public string PayrollDb { get; set; } = string.Empty;
    public string CommunityDb { get; set; } = string.Empty;
    public string CommonDb { get; set; } = string.Empty;
    public string DiagnosticsDb { get; set; } = string.Empty;
}
```

### 13.3 RuleDefinition

```csharp
namespace DataTula.Core.Configuration;

public sealed class RuleDefinition
{
    public string RuleCode { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string RuleType { get; set; } = "SameDatabaseSqlRule";
    public string Severity { get; set; } = "High";
    public string Database { get; set; } = string.Empty;
    public string SqlFile { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
```

### 13.4 DataTulaRunRequest

```csharp
namespace DataTula.Core;

public sealed class DataTulaRunRequest
{
    public string Environment { get; set; } = "qa";
    public string? Module { get; set; }
    public string? RuleCode { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? OrganizationId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string RunType { get; set; } = "Manual";
}
```

### 13.5 DataIntegrityFinding

```csharp
namespace DataTula.Core;

public sealed class DataIntegrityFinding
{
    public string RuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Severity { get; set; } = "High";

    public Guid? OrganizationId { get; set; }
    public Guid? CompanyId { get; set; }

    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public string? SourceNumber { get; set; }

    public string? RelatedType { get; set; }
    public string? RelatedId { get; set; }
    public string? RelatedNumber { get; set; }

    public string Message { get; set; } = string.Empty;
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public decimal? DifferenceValue { get; set; }

    public string? FindingHash { get; set; }
}
```

### 13.6 DataTulaRunResult

```csharp
namespace DataTula.Core;

public sealed class DataTulaRunResult
{
    public Guid RunId { get; set; }

    public int TotalRules { get; set; }
    public int ExecutedRules { get; set; }
    public int FailedRules { get; set; }

    public int TotalFindings { get; set; }
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }

    public string? HtmlReportPath { get; set; }
    public string? JsonReportPath { get; set; }

    public List<DataIntegrityFinding> Findings { get; set; } = new();
}
```

---

## 14. Core Interfaces

### 14.1 IRuleExecutor

```csharp
namespace DataTula.Core;

public interface IRuleExecutor
{
    Task<IReadOnlyList<DataIntegrityFinding>> ExecuteAsync(
        RuleDefinition rule,
        DataTulaRunRequest request,
        CancellationToken cancellationToken);
}
```

### 14.2 IRuleRunner

```csharp
namespace DataTula.Core;

public interface IRuleRunner
{
    Task<DataTulaRunResult> RunAsync(
        DataTulaRunRequest request,
        CancellationToken cancellationToken);
}
```

### 14.3 IRunRepository

```csharp
namespace DataTula.Core;

public interface IRunRepository
{
    Task<Guid> CreateRunAsync(
        DataTulaRunRequest request,
        CancellationToken cancellationToken);

    Task SaveFindingsAsync(
        Guid runId,
        IReadOnlyList<DataIntegrityFinding> findings,
        CancellationToken cancellationToken);

    Task CompleteRunAsync(
        Guid runId,
        DataTulaRunResult result,
        string status,
        CancellationToken cancellationToken);

    Task FailRunAsync(
        Guid runId,
        Exception exception,
        CancellationToken cancellationToken);
}
```

### 14.4 IReportGenerator

```csharp
namespace DataTula.Core;

public interface IReportGenerator
{
    Task<string> GenerateHtmlAsync(
        DataTulaRunResult result,
        CancellationToken cancellationToken);

    Task<string> GenerateJsonAsync(
        DataTulaRunResult result,
        CancellationToken cancellationToken);
}
```

### 14.5 IConnectionFactory

```csharp
namespace DataTula.Core;

public interface IConnectionFactory
{
    string GetConnectionString(string databaseName);
}
```

---

## 15. Implementation Classes

### DataTula.Core

```text
Configuration/
  DataTulaOptions.cs
  ConnectionOptions.cs
  RuleDefinition.cs

Models/
  DataTulaRunRequest.cs
  DataTulaRunResult.cs
  DataIntegrityFinding.cs

Abstractions/
  IRuleExecutor.cs
  IRuleRunner.cs
  IRunRepository.cs
  IReportGenerator.cs
  IConnectionFactory.cs

Services/
  RuleRunner.cs
  FindingHashService.cs
```

### DataTula.Postgres

```text
PostgresConnectionFactory.cs
SqlRuleExecutor.cs
PostgresRunRepository.cs
```

### DataTula.Reports

```text
HtmlReportGenerator.cs
JsonReportGenerator.cs
```

### DataTula.Console

```text
Program.cs
CommandLineOptions.cs
```

---

## 16. RuleRunner Logic

Pseudo-flow:

```text
1. Create run record in diagnostics.data_integrity_runs.
2. Load enabled rules from configuration.
3. Filter by module if provided.
4. Filter by rule code if provided.
5. For each rule:
   a. If rule type is SameDatabaseSqlRule, execute SQL file.
   b. If rule type is MultiDatabaseCSharpRule, execute C# rule.
   c. Generate finding hashes.
   d. Save findings.
   e. Continue even if one rule fails.
6. Generate HTML report.
7. Generate JSON report.
8. Complete run record.
9. Return exit code.
```

Run status rules:

```text
Running
Completed
CompletedWithFindings
CompletedWithRuleErrors
Failed
```

---

## 17. Finding Hash

Purpose:

```text
Identify the same finding across multiple runs.
Reduce duplicate noise.
Allow future Reviewed/Ignored/Resolved workflows.
```

Hash input:

```text
rule_code
company_id
source_type
source_id
related_type
related_id
expected_value
actual_value
```

C#:

```csharp
public static string CreateHash(DataIntegrityFinding finding)
{
    var raw = string.Join("|",
        finding.RuleCode,
        finding.CompanyId,
        finding.SourceType,
        finding.SourceId,
        finding.RelatedType,
        finding.RelatedId,
        finding.ExpectedValue,
        finding.ActualValue);

    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}
```

---

## 18. Standard SQL Rule Output

Every SQL rule must return these columns:

```text
rule_code
module_name
category
severity
organization_id
company_id
source_type
source_id
source_number
related_type
related_id
related_number
message
expected_value
actual_value
difference_value
```

This allows generic mapping.

---

## 19. First Use Case: Sales Invoice to Ledger

Rules:

```text
SAL-001: Every approved/posted invoice must have exactly one accounting transaction.
SAL-002: Invoice total amount must match the journal posting amount.
SAL-003: Invoice customer_id must match transaction customer_id.
SAL-004: Invoice company_id must match transaction company_id.
SAL-005: Paid invoice settled_amount must match payment records.
```

Important:

Before coding SQL, inspect actual table/column names.

Verify:

```text
invoice_headers
invoice_payments
transaction_headers
journal_entries
invoice_status_id
entry_type
source_type
source_id
source_number
document_number
customer_id
company_id
total_amount
settled_amount
```

---

## 20. SQL Rules for First Use Case

### 20.1 SAL-001

File:

```text
rules/sales/SAL-001_invoice_must_have_one_transaction.sql
```

```sql
SELECT
    'SAL-001' AS rule_code,
    'Sales' AS module_name,
    'SalesInvoiceLedger' AS category,
    'Critical' AS severity,

    NULL::uuid AS organization_id,
    ih.company_id AS company_id,

    'SalesInvoice' AS source_type,
    ih.id::text AS source_id,
    ih.invoice_number AS source_number,

    'TransactionHeader' AS related_type,
    NULL::text AS related_id,
    NULL::text AS related_number,

    'Approved or posted sales invoice must have exactly one accounting transaction.' AS message,
    '1 transaction' AS expected_value,
    COUNT(th.id)::text AS actual_value,
    NULL::numeric AS difference_value
FROM public.invoice_headers ih
LEFT JOIN public.transaction_headers th
    ON th.source_type = 'SalesInvoice'
   AND th.source_id = ih.id
WHERE ih.is_deleted = false
  AND (@CompanyId::uuid IS NULL OR ih.company_id = @CompanyId::uuid)
  AND (@FromDate::timestamp IS NULL OR ih.invoice_date >= @FromDate::timestamp)
  AND (@ToDate::timestamp IS NULL OR ih.invoice_date <= @ToDate::timestamp)
  -- TODO: Add actual approved/posted status filter.
GROUP BY ih.id, ih.invoice_number, ih.company_id
HAVING COUNT(th.id) <> 1;
```

### 20.2 SAL-002

File:

```text
rules/sales/SAL-002_invoice_total_must_match_ledger.sql
```

```sql
WITH invoice_ledger AS (
    SELECT
        ih.id AS invoice_id,
        ih.invoice_number,
        ih.company_id,
        ih.total_amount,
        COALESCE(SUM(
            CASE
                WHEN je.entry_type = 'D' THEN je.amount
                ELSE 0
            END
        ), 0) AS ledger_debit_total
    FROM public.invoice_headers ih
    JOIN public.transaction_headers th
        ON th.source_type = 'SalesInvoice'
       AND th.source_id = ih.id
    JOIN public.journal_entries je
        ON je.transaction_id = th.id
    WHERE ih.is_deleted = false
      AND (@CompanyId::uuid IS NULL OR ih.company_id = @CompanyId::uuid)
      AND (@FromDate::timestamp IS NULL OR ih.invoice_date >= @FromDate::timestamp)
      AND (@ToDate::timestamp IS NULL OR ih.invoice_date <= @ToDate::timestamp)
      -- TODO: Add actual approved/posted status filter.
    GROUP BY ih.id, ih.invoice_number, ih.company_id, ih.total_amount
)
SELECT
    'SAL-002' AS rule_code,
    'Sales' AS module_name,
    'SalesInvoiceLedger' AS category,
    'Critical' AS severity,

    NULL::uuid AS organization_id,
    company_id,

    'SalesInvoice' AS source_type,
    invoice_id::text AS source_id,
    invoice_number AS source_number,

    'JournalEntry' AS related_type,
    NULL::text AS related_id,
    NULL::text AS related_number,

    'Sales invoice total must match ledger debit posting amount.' AS message,
    total_amount::text AS expected_value,
    ledger_debit_total::text AS actual_value,
    total_amount - ledger_debit_total AS difference_value
FROM invoice_ledger
WHERE total_amount <> ledger_debit_total;
```

Note:

```text
Later improve SAL-002 to compare specifically against customer receivable account, not all debit entries.
```

### 20.3 SAL-003

File:

```text
rules/sales/SAL-003_invoice_customer_must_match_transaction.sql
```

```sql
SELECT
    'SAL-003' AS rule_code,
    'Sales' AS module_name,
    'SalesInvoiceLedger' AS category,
    'Critical' AS severity,

    NULL::uuid AS organization_id,
    ih.company_id AS company_id,

    'SalesInvoice' AS source_type,
    ih.id::text AS source_id,
    ih.invoice_number AS source_number,

    'TransactionHeader' AS related_type,
    th.id::text AS related_id,
    th.document_number AS related_number,

    'Sales invoice customer_id must match transaction header customer_id.' AS message,
    ih.customer_id::text AS expected_value,
    th.customer_id::text AS actual_value,
    NULL::numeric AS difference_value
FROM public.invoice_headers ih
JOIN public.transaction_headers th
    ON th.source_type = 'SalesInvoice'
   AND th.source_id = ih.id
WHERE ih.is_deleted = false
  AND (@CompanyId::uuid IS NULL OR ih.company_id = @CompanyId::uuid)
  AND (@FromDate::timestamp IS NULL OR ih.invoice_date >= @FromDate::timestamp)
  AND (@ToDate::timestamp IS NULL OR ih.invoice_date <= @ToDate::timestamp)
  AND ih.customer_id <> th.customer_id;
```

### 20.4 SAL-004

File:

```text
rules/sales/SAL-004_invoice_company_must_match_transaction.sql
```

```sql
SELECT
    'SAL-004' AS rule_code,
    'Sales' AS module_name,
    'SalesInvoiceLedger' AS category,
    'Critical' AS severity,

    NULL::uuid AS organization_id,
    ih.company_id AS company_id,

    'SalesInvoice' AS source_type,
    ih.id::text AS source_id,
    ih.invoice_number AS source_number,

    'TransactionHeader' AS related_type,
    th.id::text AS related_id,
    th.document_number AS related_number,

    'Sales invoice company_id must match transaction header company_id.' AS message,
    ih.company_id::text AS expected_value,
    th.company_id::text AS actual_value,
    NULL::numeric AS difference_value
FROM public.invoice_headers ih
JOIN public.transaction_headers th
    ON th.source_type = 'SalesInvoice'
   AND th.source_id = ih.id
WHERE ih.is_deleted = false
  AND (@CompanyId::uuid IS NULL OR ih.company_id = @CompanyId::uuid)
  AND (@FromDate::timestamp IS NULL OR ih.invoice_date >= @FromDate::timestamp)
  AND (@ToDate::timestamp IS NULL OR ih.invoice_date <= @ToDate::timestamp)
  AND ih.company_id <> th.company_id;
```

### 20.5 SAL-005

File:

```text
rules/sales/SAL-005_invoice_settled_amount_must_match_payments.sql
```

```sql
WITH payment_summary AS (
    SELECT
        ih.id AS invoice_id,
        ih.invoice_number,
        ih.company_id,
        ih.total_amount,
        ih.settled_amount,
        COALESCE(SUM(ip.amount), 0) AS payment_total
    FROM public.invoice_headers ih
    LEFT JOIN public.invoice_payments ip
        ON ip.invoice_id = ih.id
       AND ip.is_deleted = false
    WHERE ih.is_deleted = false
      AND (@CompanyId::uuid IS NULL OR ih.company_id = @CompanyId::uuid)
      AND (@FromDate::timestamp IS NULL OR ih.invoice_date >= @FromDate::timestamp)
      AND (@ToDate::timestamp IS NULL OR ih.invoice_date <= @ToDate::timestamp)
    GROUP BY ih.id, ih.invoice_number, ih.company_id, ih.total_amount, ih.settled_amount
)
SELECT
    'SAL-005' AS rule_code,
    'Sales' AS module_name,
    'SalesInvoiceLedger' AS category,
    'High' AS severity,

    NULL::uuid AS organization_id,
    company_id,

    'SalesInvoice' AS source_type,
    invoice_id::text AS source_id,
    invoice_number AS source_number,

    'InvoicePayment' AS related_type,
    NULL::text AS related_id,
    NULL::text AS related_number,

    'Invoice settled_amount must match sum of invoice payment records.' AS message,
    settled_amount::text AS expected_value,
    payment_total::text AS actual_value,
    settled_amount - payment_total AS difference_value
FROM payment_summary
WHERE settled_amount <> payment_total;
```

---

## 21. Report Design

HTML report sections:

```text
1. Header
2. Run metadata
3. Summary cards
4. Rule summary
5. Findings table
6. Rule errors, if any
```

Header:

```text
Data Tula Report
Environment: QA
Module: Sales
Run Type: Manual
Started On
Completed On
Status
```

Summary cards:

```text
Total Rules
Executed Rules
Failed Rules
Total Findings
Critical
High
Medium
Low
```

Findings table:

```text
Severity
Rule Code
Module
Company ID
Source Type
Source Number
Message
Expected
Actual
Difference
```

---

## 22. Logging

Use Serilog.

Log to:

```text
Console
logs/data-tula-.log
```

Log:

```text
Run started
Configuration loaded
Rule started
Rule completed
Rule finding count
Rule failed
Report generated
Run completed
Run failed
```

Never log DB passwords.

---

## 23. Jenkins Integration Plan

Later add Jenkins stage:

```groovy
stage('Run Data Tula') {
    steps {
        sh '''
          dotnet run --project data-tula/src/DataTula.Console \
            -- run --env qa --module sales
        '''
    }
    post {
        always {
            archiveArtifacts artifacts: 'data-tula/reports/**/*.*', fingerprint: true
        }
    }
}
```

Recommended Jenkins credentials:

```text
DATATULA_CONNECTIONS__SALESDB_QA
DATATULA_CONNECTIONS__PURCHASEDB_QA
DATATULA_CONNECTIONS__INVENTORYDB_QA
DATATULA_CONNECTIONS__PAYROLLDB_QA
DATATULA_CONNECTIONS__COMMONDB_QA
DATATULA_CONNECTIONS__DIAGNOSTICSDB_QA
```

---

## 24. Implementation Sequence

### Step 1: Create branch

```bash
git checkout -b feature/data-tula-v0.1
```

### Step 2: Create solution and projects

Create all projects listed above.

### Step 3: Add config loading

Load:

```text
config/data-tula.qa.json
Environment variables
CLI args
```

### Step 4: Add diagnostics schema script

Create `database/diagnostics_schema.sql`.

### Step 5: Implement core models and interfaces

Implement models and interfaces from this plan.

### Step 6: Implement PostgresConnectionFactory

It should return connection strings by logical database name:

```text
SalesDb
PurchaseDb
InventoryDb
PayrollDb
CommunityDb
CommonDb
DiagnosticsDb
```

### Step 7: Implement SqlRuleExecutor

Responsibilities:

```text
Read SQL file
Open connection to selected database
Bind parameters
Execute query
Map rows to DataIntegrityFinding
Return findings
```

Parameters:

```text
CompanyId
OrganizationId
FromDate
ToDate
```

### Step 8: Implement PostgresRunRepository

Responsibilities:

```text
Insert run
Insert findings
Complete run
Fail run
```

Only writes allowed are to diagnostics tables.

### Step 9: Implement RuleRunner

Coordinate entire run.

### Step 10: Implement HTML and JSON reports

Generate report files under:

```text
reports/yyyyMMdd-HHmmss/
```

### Step 11: Add Sales SQL rules

Add SAL-001 to SAL-005.

### Step 12: Add README

Include:

```text
Purpose
Setup
Configuration
Run command
Reports
Exit codes
```

### Step 13: Build and test

```bash
dotnet build

dotnet test

dotnet run --project src/DataTula.Console -- run --env qa --module sales
```

---

## 25. Acceptance Criteria

The command:

```bash
dotnet run --project src/DataTula.Console -- run --env qa --module sales
```

must:

```text
1. Load configuration.
2. Connect to Sales DB.
3. Connect to Diagnostics DB.
4. Create one data_integrity_runs record.
5. Execute enabled Sales rules.
6. Store findings in data_integrity_findings.
7. Generate HTML report.
8. Generate JSON report.
9. Update run status.
10. Return correct exit code.
```

Exit code:

```text
0 = no critical findings
1 = critical findings found
2 = runtime failure
```

---

## 26. First Agent Prompt

Use this prompt with Claude/Codex:

```text
We need to implement Data Tula v0.1 as a separate business-data reconciliation framework under the Dgtula tools area.

Important: Do not mix this with db-tula. db-tula is only for schema comparison. data-tula is for business data reconciliation.

Before coding SQL, inspect the actual Sales project/database table and column names for:
- invoice_headers
- invoice_payments
- transaction_headers
- journal_entries
- invoice_status_id
- entry_type
- source_type/source_id/source_number or equivalent mapping
- document_number
- customer_id
- company_id
- total_amount
- settled_amount

Create a branch first:
feature/data-tula-v0.1

Then create a .NET 9 solution under dgtula-tools/data-tula with these projects:
- DataTula.Console
- DataTula.Core
- DataTula.Postgres
- DataTula.Rules
- DataTula.Reports
- DataTula.Tests

Implement:
1. Multi-database configuration support.
2. SQL-file-based same-database rules.
3. Diagnostics schema script.
4. Run tracking.
5. Findings storage.
6. HTML report.
7. JSON report.
8. Sales rules SAL-001 to SAL-005.

Do not implement auto-fix.
Do not update business data.
Only write to diagnostics tables.

First use case:
Sales Invoice to Ledger reconciliation.

Rules:
SAL-001: Approved/posted invoice must have exactly one transaction.
SAL-002: Invoice total must match ledger posting amount.
SAL-003: Invoice customer_id must match transaction customer_id.
SAL-004: Invoice company_id must match transaction company_id.
SAL-005: Invoice settled_amount must match payment records.

The final command should work:
dotnet run --project src/DataTula.Console -- run --env qa --module sales

Return codes:
0 = no critical findings
1 = critical findings exist
2 = runtime failure
```

---

## 27. Future Rule Groups

After v0.1:

### Purchase

```text
PUR-001: Approved bill must have exactly one transaction.
PUR-002: Bill total must match ledger posting.
PUR-003: Bill vendor_id must match transaction vendor_id.
PUR-004: Bill company_id must match transaction company_id.
PUR-005: Bill settled_amount must match bill payments.
```

### Inventory/Product

```text
INV-001: Product must have product_details.
INV-002: Product must have organization_product.
INV-003: Product must have company_product.
INV-004: Yarn product must have yarn-specific required fields.
INV-005: Vendor-product mapping must exist for mill-linked product.
```

### Common Party Mapping

```text
COM-001: Same GSTIN customer/vendor should map to same party.
COM-002: Customer with vendor role must have vendor mapping.
COM-003: Vendor with customer role must have customer mapping.
```

### Payroll

```text
PAY-001: Salary run must have accounting transaction.
PAY-002: Salary expense must match ledger posting.
PAY-003: Salary payable must match employee payable.
PAY-004: Payroll payment must match bank/cash posting.
```

### Bank

```text
BNK-001: Bank transfer must have debit and credit bank entries.
BNK-002: Bank payment must reduce bank ledger.
BNK-003: Bank receipt must increase bank ledger.
BNK-004: Reconciled bank transaction must have matching payment.
```

---

## 28. Final Recommendation

Build Data Tula in layers:

```text
v0.1: CLI + SQL rules + Sales invoice reconciliation
v0.2: Jenkins nightly run + archived reports
v0.3: Purchase and payment reconciliation
v0.4: Product/inventory checks
v0.5: Cross-database C# rules
v0.6: Dgtula Admin UI Data Integrity Center
v1.0: Repair suggestions with manual approval
```

The most important foundation is:

```text
Every financial transaction must have clear source document mapping.
```

Without source mapping, reconciliation becomes guesswork.


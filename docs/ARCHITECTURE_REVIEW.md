# dbtula: Production-Grade Architecture Review

> Reviewed: 2026-05-28  
> Reviewer: Senior Database Tooling Architect / PostgreSQL Expert  
> Scope: Full codebase review — correctness, performance, architecture, CI/CD, testing, build-vs-buy

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Current Approach Assessment](#current-approach-assessment)
3. [Major Risks and Gaps — Six Production Bugs](#major-risks-and-gaps--six-production-bugs)
4. [Recommended Architecture](#recommended-architecture)
5. [PostgreSQL Metadata Extraction Strategy](#postgresql-metadata-extraction-strategy)
6. [Fast Comparison Algorithm](#fast-comparison-algorithm)
7. [Safe Sync Script Generation](#safe-sync-script-generation)
8. [Report Design](#report-design)
9. [CI/CD Integration Plan](#cicd-integration-plan)
10. [Safety Guardrails](#safety-guardrails)
11. [Extensibility](#extensibility)
12. [Testing Plan](#testing-plan)
13. [Build vs Buy Recommendation](#build-vs-buy-recommendation)
14. [Step-by-Step Refactoring Roadmap](#step-by-step-refactoring-roadmap)
15. [Quick-Reference: Files to Change in Phase 1](#quick-reference-files-to-change-in-phase-1)

---

## Executive Summary

dbtula is a well-conceived, actively-used tool with a clean layered architecture, genuine semantic comparison logic, and a working CI/CD pipeline. It solves a real problem and is already more sophisticated than most in-house schema diff tools at this stage. However, it has **six production-blocking defects** that will cause incorrect results today, a **fundamental performance architecture problem** that will not scale, and **critical missing object coverage** for a financial system (check constraints, enums, domains, FK cascade rules).

The custom `pg_get_tabledef()` function installed on live databases is the single most urgent thing to remove.

**The tool is worth continuing to build. Do not switch to an alternative.** The reasoning is in [Build vs Buy](#build-vs-buy-recommendation).

---

## Current Approach Assessment

| Dimension | Rating | Finding |
|---|---|---|
| Architecture layer separation | Good | Core / Infra / CLI split is clean |
| Provider abstraction | Good | `IDatabaseSchemaProvider` is the right shape |
| Semantic comparison | Good | Structural signatures, order-independence |
| Canonicalization concept | Good | `IgnoreOwnership` is exactly right |
| Index query correctness | **Bug** | Column order not preserved; no validity filter |
| Materialized view detection | **Bug** | String heuristic (`_mv`) will misfire |
| PK/index validation | **Bug** | Both are unimplemented placeholders |
| Canonicalization regex | **Bug** | Destroys type casts and qualified names |
| Per-table query pattern | **Bad** | N+1 across tables; kills performance at scale |
| Sequence comparison | Incomplete | Existence only, no definition comparison |
| Missing object coverage | Incomplete | No CHECK constraints, enums, domains, FK actions, numeric precision |
| Custom `pg_get_tabledef` install | **Critical risk** | Mutates the database it is comparing |
| CI/CD pipeline | Functional | Works; has efficiency problems |
| Tests | None | Zero test coverage |

---

## Major Risks and Gaps — Six Production Bugs

### Bug 1 — Index column order is non-deterministic (Correctness)

**Location:** `SchemaFetcher.GetIndexesAsync()`, line 270

```sql
-- WRONG — ANY(ix.indkey) is a set membership test, not ordered
JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
```

`ANY(ix.indkey)` returns matching rows in heap order, not index column order. An index on `(city, state)` can return columns as `[state, city]`. The structural key built from that list is wrong, meaning `idx(col_a, col_b)` and `idx(col_b, col_a)` may compare as equal.

**Fix** — replace with ordered unnest:

```sql
SELECT
    t.relname AS table_name,
    i.relname AS index_name,
    am.amname AS index_type,
    ix.indisunique AS is_unique,
    ix.indisvalid AS is_valid,
    a.attname AS column_name,
    cols.ord AS column_position,
    pg_get_expr(ix.indpred, ix.indrelid, true) AS predicate
FROM pg_class t
JOIN pg_index ix ON t.oid = ix.indrelid
JOIN pg_class i ON i.oid = ix.indexrelid
JOIN pg_am am ON i.relam = am.oid
JOIN pg_namespace ns ON ns.oid = t.relnamespace
CROSS JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS cols(attnum, ord)
JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = cols.attnum
WHERE ns.nspname = 'public'
  AND ix.indisvalid = true
  AND ix.indisprimary = false    -- PKs are compared separately
  AND a.attnum > 0               -- exclude expression index columns
ORDER BY t.relname, i.relname, cols.ord;
```

---

### Bug 2 — Materialized view detection is a string heuristic (Correctness)

**Location:** `SchemaComparer.cs`, lines 1010–1018

```csharp
// WRONG — string heuristic; any table named payment_mv_ledger is misclassified
var isMatView = tableName.ToLower().Contains("_mv") || tableName.ToLower().Contains("matview");
return Task.FromResult(isMatView);
```

A real table named `payment_mv_ledger` is wrongly classified as a materialized view and its primary key comparison is silently skipped. This is a heuristic that must not exist in a comparison tool.

**Fix** — query `pg_class` once upfront and cache:

```sql
SELECT relname
FROM pg_class
JOIN pg_namespace n ON n.oid = relnamespace
WHERE relkind = 'm'
  AND n.nspname = 'public';
```

Cache this as a `HashSet<string>` per provider at the start of comparison. Inject it into `CompareTablesAsync` as a parameter.

---

### Bug 3 — PK and index validation are unimplemented placeholders (Correctness)

**Location:** `SchemaComparer.cs`, lines 946–998

```csharp
// PLACEHOLDER — never actually validates anything
private static Task<bool> IsValidPrimaryKeyAsync(...)
{
    return Task.FromResult(pk.Columns.Any());  // always returns true if PK has columns
}

private static Task<bool> IsValidIndexAsync(...)
{
    return Task.FromResult(index.Columns.Any());  // always returns true
}
```

The code comments explicitly say "placeholder for enhanced catalog queries." Broken indexes in PostgreSQL (e.g., after a failed `CREATE INDEX CONCURRENTLY`) are invisible to the tool.

**Fix:**
- `IsValidIndexAsync`: check `indisvalid` from the corrected index query (Bug 1 fix includes this column)
- `IsValidPrimaryKeyAsync`: the PK constraint fetched from `pg_constraint` is always valid if it exists; the issue is only inherited/partitioned table PKs, which can be filtered by checking `conislocal = true`

---

### Bug 4 — Canonicalizer regex corrupts SQL bodies (Correctness)

**Location:** `DefinitionCanonicalizer.cs`, line 54

```csharp
// DANGEROUS — removes ALL word.dot patterns everywhere, including inside function bodies
result = Regex.Replace(result, @"\b\w+\.", "", RegexOptions.IgnoreCase);
```

This strip removes:
- Type casts inside plpgsql: `t.column_name` becomes `column_name`
- Schema-qualified builtins: `pg_catalog.int4` becomes `int4`
- Table-qualified column refs in SQL functions: `orders.amount` becomes `amount`

Two identical functions may compare as different, or two different functions as equal, depending on what text happens to match.

**Fix** — replace wildcard strip with explicit line-level removals only:

```csharp
private static string RemoveOwnershipReferences(string ddl, string dbKind)
{
    var result = ddl;

    if (dbKind.Equals("postgres", StringComparison.OrdinalIgnoreCase))
    {
        // Remove full OWNER TO statements (line-level, safe)
        result = Regex.Replace(result,
            @"ALTER\s+(TABLE|SEQUENCE|FUNCTION|PROCEDURE|VIEW)\s+[^;]+\s+OWNER\s+TO\s+\S+;",
            "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // Remove SET search_path lines only
        result = Regex.Replace(result,
            @"^\s*SET\s+search_path\s*=\s*[^;]+;\s*$",
            "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // Remove GRANT/REVOKE lines only
        result = Regex.Replace(result,
            @"^\s*(GRANT|REVOKE)\s+[^;]+;\s*$",
            "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // Only strip the 'public.' schema prefix when it appears immediately
        // after a DDL keyword — NOT inside function bodies
        result = Regex.Replace(result,
            @"(?<=\b(TABLE|VIEW|FUNCTION|PROCEDURE|SEQUENCE|INDEX|TRIGGER)\s+)public\.",
            "", RegexOptions.IgnoreCase);
    }

    return result;
}
```

---

### Bug 5 — Custom `pg_get_tabledef` mutates the compared database (Safety / Critical)

**Location:** `SchemaFetcher.EnsurePgGetTableDefFunctionExistsAsync()`

This method installs a PL/pgSQL function into the `public` schema of the database being compared. Problems:

1. **DDL write to a production database** during what is supposed to be a read-only comparison
2. **Security violation** if the comparison role has `CONNECT` + `SELECT` only — it fails loudly with a permission error
3. **Wrong results** — the custom function only includes column names, types, nullability, and the PK. It misses: column defaults for `GENERATED ALWAYS AS IDENTITY`, foreign keys, indexes, check constraints, and type cast expressions

**Fix** — delete `EnsurePgGetTableDefFunctionExistsAsync` and its call site entirely. All the structural data the tool needs is already fetched via catalog queries. The CREATE TABLE script is not needed for correctness — it is only used for the side-by-side DDL diff display, which can be assembled from structured catalog data.

---

### Bug 6 — Numeric precision/scale not fetched; type representation inconsistent (Correctness)

**Location:** `SchemaFetcher.GetColumnsAsync()`

```sql
-- Missing numeric_precision and numeric_scale
SELECT column_name, data_type, character_maximum_length, is_nullable, column_default
FROM information_schema.columns
```

`numeric(10,2)` and `numeric(18,4)` both return data type `"numeric"` and compare as equal. This is wrong for a financial system where decimal precision is critical.

**Fix** — add the missing columns to the query and to `ColumnDefinition`:

```sql
SELECT
    column_name,
    data_type,
    udt_name,
    character_maximum_length,
    numeric_precision,
    numeric_scale,
    datetime_precision,
    is_nullable,
    column_default,
    is_generated,
    identity_generation
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name = @tableName
ORDER BY ordinal_position;
```

Add to `ColumnDefinition.cs`:

```csharp
public int? NumericPrecision { get; set; }
public int? NumericScale { get; set; }
public int? DateTimePrecision { get; set; }
```

Update `Equals()` and `GetHashCode()` to include these fields.

---

## Recommended Architecture

The current architecture is close to right. It needs **one layer added** (bulk metadata snapshot) and **one class split** (SchemaComparer is a 1000-line monolith).

### Target project structure

```
B2A.DbTula.sln
├── src/
│   ├── B2A.DbTula.Core/
│   │   ├── Abstractions/
│   │   │   ├── IDatabaseSchemaProvider.cs     (add ProviderKind property)
│   │   │   ├── IDatabaseSchemaSnapshot.cs     (NEW: bulk snapshot contract)
│   │   │   └── ISchemaComparer.cs
│   │   ├── Models/
│   │   │   ├── SchemaSnapshot.cs              (NEW: all objects for one DB)
│   │   │   ├── ColumnDefinition.cs            (add NumericPrecision, NumericScale)
│   │   │   ├── ForeignKeyDefinition.cs        (add OnDelete, OnUpdate)
│   │   │   ├── CheckConstraintDefinition.cs   (NEW)
│   │   │   ├── EnumTypeDefinition.cs          (NEW)
│   │   │   ├── DbSequenceDefinition.cs        (replace string name with full model)
│   │   │   └── ...existing models
│   │   ├── Comparison/
│   │   │   ├── TableComparer.cs               (NEW: extracted from SchemaComparer)
│   │   │   ├── FunctionComparer.cs            (NEW)
│   │   │   ├── SequenceComparer.cs            (NEW)
│   │   │   └── SchemaComparer.cs              (orchestrator only, ~100 lines)
│   │   └── Utilities/
│   │       ├── DefinitionCanonicalizer.cs     (fix regex — see Bug 4)
│   │       └── DataTypeNormalizer.cs          (NEW: varchar ↔ character varying)
│   │
│   ├── B2A.DbTula.Infrastructure.Postgres/
│   │   ├── BulkSchemaFetcher.cs               (NEW: all tables in ~8 queries)
│   │   ├── SchemaFetcher.cs                   (keep for per-object DDL scripts)
│   │   ├── PostgresSchemaProvider.cs          (implement IDatabaseSchemaSnapshot)
│   │   └── PostgresDatabaseConnection.cs
│   │
│   ├── B2A.DbTula.Infrastructure.MySql/       (unchanged for now)
│   │
│   └── B2A.DbTula.Cli/
│       ├── Program.cs
│       ├── CliOptions.cs
│       ├── SyncScriptGenerator.cs             (NEW: split from comparison)
│       ├── Reports/
│       │   ├── HtmlReportGenerator.cs
│       │   └── MarkdownReportGenerator.cs     (NEW: for CI/PR comments)
│       └── Services/
│           └── BatchProcessor.cs
│
└── tests/
    ├── B2A.DbTula.Core.Tests/                 (unit tests, no DB)
    ├── B2A.DbTula.Integration.Tests/          (Docker Postgres via Testcontainers)
    └── B2A.DbTula.GoldenFile.Tests/           (golden file tests for sync scripts)
```

### New interface: `IDatabaseSchemaSnapshot`

```csharp
// B2A.DbTula.Core/Abstractions/IDatabaseSchemaSnapshot.cs
public interface IDatabaseSchemaSnapshot
{
    Task<SchemaSnapshot> TakeSnapshotAsync(CancellationToken ct = default);
}

public record SchemaSnapshot
{
    public IReadOnlyList<string> TableNames { get; init; } = [];
    public IReadOnlyDictionary<string, List<ColumnDefinition>> ColumnsByTable { get; init; } = new Dictionary<string, List<ColumnDefinition>>();
    public IReadOnlyDictionary<string, List<PrimaryKeyDefinition>> PrimaryKeysByTable { get; init; } = new Dictionary<string, List<PrimaryKeyDefinition>>();
    public IReadOnlyDictionary<string, List<ForeignKeyDefinition>> ForeignKeysByTable { get; init; } = new Dictionary<string, List<ForeignKeyDefinition>>();
    public IReadOnlyDictionary<string, List<IndexDefinition>> IndexesByTable { get; init; } = new Dictionary<string, List<IndexDefinition>>();
    public IReadOnlyDictionary<string, List<UniqueConstraintDefinition>> UniqueConstraintsByTable { get; init; } = new Dictionary<string, List<UniqueConstraintDefinition>>();
    public IReadOnlyDictionary<string, List<CheckConstraintDefinition>> CheckConstraintsByTable { get; init; } = new Dictionary<string, List<CheckConstraintDefinition>>();
    public IReadOnlyList<DbFunctionDefinition> Functions { get; init; } = [];
    public IReadOnlyList<DbFunctionDefinition> Procedures { get; init; } = [];
    public IReadOnlyList<DbViewDefinition> Views { get; init; } = [];
    public IReadOnlyList<DbTriggerDefinition> Triggers { get; init; } = [];
    public IReadOnlyList<DbSequenceDefinition> Sequences { get; init; } = [];
    public HashSet<string> MaterializedViewNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset CapturedAt { get; init; }
}
```

### Fix provider-type detection

Replace this fragile pattern (breaks if class is renamed or wrapped in a decorator):

```csharp
// FRAGILE
provider.GetType().Name.Contains("Postgres", StringComparison.OrdinalIgnoreCase)
```

With a strongly-typed property on the interface:

```csharp
// B2A.DbTula.Core/Enums/DbProviderKind.cs
public enum DbProviderKind { Postgres, MySql, SqlServer }

// B2A.DbTula.Core/Abstractions/IDatabaseSchemaProvider.cs
public interface IDatabaseSchemaProvider
{
    DbProviderKind ProviderKind { get; }
    // ...existing methods
}

// Usage in SchemaComparer
var dbKind = sourceProvider.ProviderKind == DbProviderKind.Postgres ? "postgres" : "mysql";
```

---

## PostgreSQL Metadata Extraction Strategy

### Bulk columns query (replaces per-table calls)

```sql
SELECT
    c.table_name,
    c.column_name,
    c.ordinal_position,
    c.data_type,
    c.udt_name,
    c.character_maximum_length,
    c.numeric_precision,
    c.numeric_scale,
    c.datetime_precision,
    c.is_nullable,
    c.column_default,
    c.is_generated,
    c.identity_generation
FROM information_schema.columns c
JOIN pg_class pc ON pc.relname = c.table_name
JOIN pg_namespace pn ON pn.oid = pc.relnamespace
WHERE c.table_schema = 'public'
  AND pn.nspname = 'public'
  AND pc.relkind IN ('r', 'p')   -- regular and partitioned tables only
ORDER BY c.table_name, c.ordinal_position;
```

Group by `table_name` in C# after one round-trip. Replaces ~100 queries with 1.

### Bulk primary keys query

```sql
SELECT
    rel.relname AS table_name,
    con.conname AS constraint_name,
    att.attname AS column_name,
    array_position(con.conkey, att.attnum) AS column_position
FROM pg_constraint con
JOIN pg_class rel ON rel.oid = con.conrelid
JOIN pg_namespace ns ON ns.oid = rel.relnamespace
JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = ANY(con.conkey)
WHERE con.contype = 'p'
  AND ns.nspname = 'public'
ORDER BY rel.relname, con.conname, column_position;
```

### Bulk foreign keys query (with cascade actions)

```sql
SELECT
    rel.relname AS table_name,
    con.conname AS constraint_name,
    src_att.attname AS column_name,
    ref_rel.relname AS referenced_table,
    ref_att.attname AS referenced_column,
    src.ord AS column_position,
    CASE con.confupdtype
        WHEN 'a' THEN 'NO ACTION'   WHEN 'r' THEN 'RESTRICT'
        WHEN 'c' THEN 'CASCADE'     WHEN 'n' THEN 'SET NULL'
        WHEN 'd' THEN 'SET DEFAULT' END AS on_update,
    CASE con.confdeltype
        WHEN 'a' THEN 'NO ACTION'   WHEN 'r' THEN 'RESTRICT'
        WHEN 'c' THEN 'CASCADE'     WHEN 'n' THEN 'SET NULL'
        WHEN 'd' THEN 'SET DEFAULT' END AS on_delete
FROM pg_constraint con
JOIN pg_class rel ON rel.oid = con.conrelid
JOIN pg_namespace ns ON ns.oid = rel.relnamespace
JOIN pg_class ref_rel ON ref_rel.oid = con.confrelid
JOIN LATERAL unnest(con.conkey)  WITH ORDINALITY AS src(attnum, ord) ON true
JOIN LATERAL unnest(con.confkey) WITH ORDINALITY AS ref(attnum, ord) ON ref.ord = src.ord
JOIN pg_attribute src_att ON src_att.attrelid = con.conrelid  AND src_att.attnum = src.attnum
JOIN pg_attribute ref_att ON ref_att.attrelid = con.confrelid AND ref_att.attnum = ref.attnum
WHERE con.contype = 'f'
  AND ns.nspname = 'public'
ORDER BY rel.relname, con.conname, src.ord;
```

### Bulk indexes query (corrected, ordered)

```sql
SELECT
    t.relname AS table_name,
    i.relname AS index_name,
    am.amname AS index_type,
    ix.indisunique AS is_unique,
    ix.indisvalid AS is_valid,
    a.attname AS column_name,
    cols.ord AS column_position,
    pg_get_expr(ix.indpred, ix.indrelid, true) AS predicate
FROM pg_class t
JOIN pg_index ix ON t.oid = ix.indrelid
JOIN pg_class i ON i.oid = ix.indexrelid
JOIN pg_am am ON i.relam = am.oid
JOIN pg_namespace ns ON ns.oid = t.relnamespace
CROSS JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS cols(attnum, ord)
JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = cols.attnum
WHERE ns.nspname = 'public'
  AND ix.indisvalid = true
  AND ix.indisprimary = false   -- PKs are compared separately
  AND a.attnum > 0              -- skip expression index columns for now
ORDER BY t.relname, i.relname, cols.ord;
```

### Bulk check constraints query

```sql
SELECT
    rel.relname AS table_name,
    con.conname AS constraint_name,
    pg_get_constraintdef(con.oid, true) AS check_clause,
    con.conislocal AS is_local
FROM pg_constraint con
JOIN pg_class rel ON rel.oid = con.conrelid
JOIN pg_namespace ns ON ns.oid = rel.relnamespace
WHERE con.contype = 'c'
  AND ns.nspname = 'public'
  AND con.conislocal = true     -- exclude inherited constraints
ORDER BY rel.relname, con.conname;
```

### Enum types query

```sql
SELECT
    t.typname AS enum_name,
    e.enumlabel AS enum_value,
    e.enumsortorder AS sort_order
FROM pg_type t
JOIN pg_namespace n ON n.oid = t.typnamespace
JOIN pg_enum e ON e.enumtypid = t.oid
WHERE t.typtype = 'e'
  AND n.nspname = 'public'
ORDER BY t.typname, e.enumsortorder;
```

### Sequences query (with full definition)

```sql
SELECT
    s.sequencename,
    s.data_type,
    s.start_value,
    s.increment_by,
    s.min_value,
    s.max_value,
    s.cache_size,
    s.cycle
FROM pg_sequences s
WHERE s.schemaname = 'public'
ORDER BY s.sequencename;
```

### Materialized views (for detection cache)

```sql
SELECT relname
FROM pg_class
JOIN pg_namespace n ON n.oid = relnamespace
WHERE relkind = 'm'
  AND n.nspname = 'public';
```

---

## Fast Comparison Algorithm

### Current approach: O(tables × 6 round-trips), sequential

```
For each table (sequential loop):
  → GetColumns(table)           — 1 DB round-trip
  → GetPrimaryKeys(table)       — 1 DB round-trip
  → GetForeignKeys(table)       — 1 DB round-trip
  → GetIndexes(table)           — 1 DB round-trip
  → GetUniqueConstraints(table) — 1 DB round-trip
  → GetCreateScript(table)      — 1 DB round-trip (+ installs custom function!)

Total for 100 tables: 600+ round-trips, all sequential
```

### Proposed approach: O(1 snapshot per database), parallel

```
Parallel:
  [Task A] sourceProvider.TakeSnapshotAsync()   — ~13 bulk queries
  [Task B] targetProvider.TakeSnapshotAsync()   — ~13 bulk queries

await Task.WhenAll(snapshotA, snapshotB);

Compare in memory using Dictionary<string, T> lookups.
Total: 26 round-trips regardless of table count.
```

### BulkSchemaFetcher pattern

```csharp
// B2A.DbTula.Infrastructure.Postgres/BulkSchemaFetcher.cs
public class BulkSchemaFetcher
{
    private readonly DatabaseConnection _connection;

    public async Task<SchemaSnapshot> TakeSnapshotAsync(CancellationToken ct = default)
    {
        // All bulk queries run in parallel against the same connection pool
        var tablesTask          = FetchTableNamesAsync(ct);
        var columnsTask         = FetchAllColumnsAsync(ct);
        var primaryKeysTask     = FetchAllPrimaryKeysAsync(ct);
        var foreignKeysTask     = FetchAllForeignKeysAsync(ct);
        var indexesTask         = FetchAllIndexesAsync(ct);
        var uniqueConsTask      = FetchAllUniqueConstraintsAsync(ct);
        var checkConsTask       = FetchAllCheckConstraintsAsync(ct);
        var functionsTask       = FetchAllFunctionsAsync(ct);
        var proceduresTask      = FetchAllProceduresAsync(ct);
        var viewsTask           = FetchAllViewsAsync(ct);
        var triggersTask        = FetchAllTriggersAsync(ct);
        var sequencesTask       = FetchAllSequencesAsync(ct);
        var matviewsTask        = FetchMaterializedViewNamesAsync(ct);

        await Task.WhenAll(
            tablesTask, columnsTask, primaryKeysTask, foreignKeysTask,
            indexesTask, uniqueConsTask, checkConsTask, functionsTask,
            proceduresTask, viewsTask, triggersTask, sequencesTask, matviewsTask);

        return new SchemaSnapshot
        {
            TableNames                = tablesTask.Result,
            ColumnsByTable            = columnsTask.Result,
            PrimaryKeysByTable        = primaryKeysTask.Result,
            ForeignKeysByTable        = foreignKeysTask.Result,
            IndexesByTable            = indexesTask.Result,
            UniqueConstraintsByTable  = uniqueConsTask.Result,
            CheckConstraintsByTable   = checkConsTask.Result,
            Functions                 = functionsTask.Result,
            Procedures                = proceduresTask.Result,
            Views                     = viewsTask.Result,
            Triggers                  = triggersTask.Result,
            Sequences                 = sequencesTask.Result,
            MaterializedViewNames     = matviewsTask.Result,
            CapturedAt                = DateTimeOffset.UtcNow
        };
    }
}
```

### Refactored SchemaComparer.CompareAsync

```csharp
public async Task<IList<ComparisonResult>> CompareAsync(
    IDatabaseSchemaProvider sourceProvider,
    IDatabaseSchemaProvider targetProvider,
    ComparisonOptions? options = null,
    CancellationToken ct = default)
{
    options ??= new ComparisonOptions();

    // Take both snapshots in parallel — the biggest single performance win
    var sourceSnapshotTask = ((IDatabaseSchemaSnapshot)sourceProvider).TakeSnapshotAsync(ct);
    var targetSnapshotTask = ((IDatabaseSchemaSnapshot)targetProvider).TakeSnapshotAsync(ct);
    await Task.WhenAll(sourceSnapshotTask, targetSnapshotTask);

    var source = sourceSnapshotTask.Result;
    var target = targetSnapshotTask.Result;

    var results = new List<ComparisonResult>();

    // All comparisons are now pure in-memory Dictionary lookups — no more DB calls
    results.AddRange(_tableComparer.Compare(source, target, options));
    results.AddRange(_functionComparer.Compare(source.Functions, target.Functions, options));
    results.AddRange(_procedureComparer.Compare(source.Procedures, target.Procedures, options));
    results.AddRange(_viewComparer.Compare(source.Views, target.Views, options));
    results.AddRange(_triggerComparer.Compare(source.Triggers, target.Triggers, options));
    results.AddRange(_sequenceComparer.Compare(source.Sequences, target.Sequences, options));

    return results;
}
```

---

## Safe Sync Script Generation

### Separation of concerns

Sync script generation is currently mixed into comparison logic (sub-result `CreateScript` fields built inline). This must be a separate class that consumes `IList<ComparisonResult>` and produces an ordered, categorized SQL output.

### SyncScriptGenerator

```csharp
// B2A.DbTula.Cli/SyncScriptGenerator.cs
public class SyncScriptGenerator
{
    public SyncScript Generate(IList<ComparisonResult> results, SyncScriptOptions options)
    {
        var script = new SyncScript();

        // Safe changes — always generated
        script.SafeChanges.AddRange(GenerateCreateMissingTables(results));
        script.SafeChanges.AddRange(GenerateAddMissingColumns(results));        // nullable or WITH DEFAULT only
        script.SafeChanges.AddRange(GenerateAddMissingIndexes(results));        // CONCURRENTLY if configured
        script.SafeChanges.AddRange(GenerateAddMissingForeignKeys(results));
        script.SafeChanges.AddRange(GenerateAddMissingSequences(results));
        script.SafeChanges.AddRange(GenerateCreateOrReplaceFunctions(results));
        script.SafeChanges.AddRange(GenerateCreateOrReplaceViews(results));
        script.SafeChanges.AddRange(GenerateAddMissingCheckConstraints(results));

        // Risky changes — require --allow-risky
        if (options.IncludeRiskyChanges)
        {
            script.RiskyChanges.AddRange(GenerateAlterColumnType(results));     // may need USING
            script.RiskyChanges.AddRange(GenerateDropMissingIndexes(results));
        }

        // Destructive changes — require --allow-destructive
        if (options.AllowDestructive)
        {
            script.DestructiveChanges.AddRange(GenerateDropMissingColumns(results));
            script.DestructiveChanges.AddRange(GenerateDropMissingTables(results));
        }

        return script;
    }
}
```

### Script ordering rules (dependency-safe)

Scripts must be emitted in this exact order to avoid FK and dependency violations:

| Order | Statement type | Reason |
|---|---|---|
| 1 | `CREATE SEQUENCE` | Referenced in column defaults |
| 2 | `CREATE TABLE` | Topologically sorted by FK deps |
| 3 | `ALTER TABLE ADD COLUMN` | After table exists |
| 4 | `CREATE INDEX` | After table exists |
| 5 | `ALTER TABLE ADD CONSTRAINT PRIMARY KEY` | After columns exist |
| 6 | `ALTER TABLE ADD CONSTRAINT FOREIGN KEY` | After all referenced tables exist |
| 7 | `ALTER TABLE ADD CONSTRAINT UNIQUE` | After columns exist |
| 8 | `ALTER TABLE ADD CONSTRAINT CHECK` | After columns exist |
| 9 | `CREATE OR REPLACE FUNCTION / PROCEDURE` | After types exist |
| 10 | `CREATE OR REPLACE VIEW` | After tables and functions exist |
| 11 | `CREATE TRIGGER` | After table and function exist |

Use Kahn's algorithm for topological table ordering (sort tables with no FK deps first, then tables whose referenced tables are already included).

### Safety classification table

| Change type | Category | Reason |
|---|---|---|
| ADD TABLE | Safe | Additive |
| ADD COLUMN (nullable or with DEFAULT) | Safe | Additive, no lock |
| ADD COLUMN (NOT NULL, no DEFAULT) | Risky | Full table lock |
| CREATE INDEX (CONCURRENTLY) | Safe | No lock |
| CREATE INDEX (without CONCURRENTLY) | Risky | Lock during build |
| ADD FK CONSTRAINT | Safe | Additive |
| ADD CHECK CONSTRAINT | Risky | Validates all existing rows |
| ALTER COLUMN TYPE | Risky | Possible data loss, needs USING |
| DROP COLUMN | Risky | Data loss |
| DROP TABLE | Destructive | Permanent data loss |
| DROP INDEX | Risky | Performance impact |
| CREATE OR REPLACE FUNCTION | Safe | Replaces in-place |

### Output format

```sql
-- ============================================================
-- dbtula Sync Script
-- Source: QA  | Snapshot: 2026-05-28T12:00:00Z
-- Target: PROD | Snapshot: 2026-05-28T12:00:01Z
-- Generated:    2026-05-28T12:00:05Z
-- ⚠  REVIEW EVERY STATEMENT BEFORE EXECUTING ON PRODUCTION
-- ============================================================

-- SECTION: SAFE CHANGES (additive, non-destructive)
-- These statements add missing objects. Safe to apply after review.

-- [TABLE: payment_methods] Missing in PROD
CREATE TABLE "payment_methods" (
    "id"   integer NOT NULL,
    "name" character varying(255) NOT NULL,
    CONSTRAINT "payment_methods_pkey" PRIMARY KEY ("id")
);

-- [INDEX: idx_invoices_customer_id] Missing in PROD
CREATE INDEX CONCURRENTLY "idx_invoices_customer_id" ON "invoices" ("customer_id");

-- ============================================================
-- SECTION: RISKY CHANGES (modifications — test on a copy first)
-- ⚠  Run these in a transaction. Verify with SELECT COUNT(*) before committing.
-- ============================================================

-- [COLUMN: invoices.amount] Type differs: source=numeric(18,4) target=numeric(10,2)
-- ⚠  Data truncation possible if values exceed numeric(10,2) range.
-- ALTER TABLE "invoices" ALTER COLUMN "amount" TYPE numeric(18,4);  -- REVIEW REQUIRED
```

---

## Report Design

### Header section

```
┌─────────────────────────────────────────────────────────────┐
│ Dhanman Schema Drift Report                                 │
│ Sales DB: QA → PROD   |   Generated: 2026-05-28 12:00 UTC  │
│                                                             │
│ ✅ 142 Matched  ⚠️  8 Mismatched  ❌ 3 Missing in PROD     │
│ 🚨 DRIFT DETECTED — 11 objects require attention            │
└─────────────────────────────────────────────────────────────┘
```

### Tab structure

- **Summary** — counts, risk matrix, quick links to all mismatches
- **Tables** — per-table drill-down with columns, PKs, FKs, indexes, checks
- **Functions & Procedures** — side-by-side diff per function
- **Views** — side-by-side diff per view
- **Triggers** — side-by-side diff per trigger
- **Sequences** — definition comparison
- **Enums & Domains** — (Phase 3)

### Per-object row display

- Matched objects → gray (collapsible, hidden by default)
- Mismatched objects → amber with expand-to-diff
- Missing in PROD → red with sync script inline (one-click copy)
- Missing in QA → yellow (may be intentional, lower urgency)

### Risk summary panel (top of page)

- Objects requiring destructive changes
- Objects with risky type modifications
- Objects safe to add immediately
- Recommended apply order

### Exit codes for CI

| Code | Meaning |
|---|---|
| `0` | All objects match |
| `1` | Mismatches detected (non-destructive drift) |
| `2` | Destructive drift — objects missing in target |
| `3` | Comparison failed (connection error, query error) |

---

## CI/CD Integration Plan

### Problem 1 — `dotnet run` per comparison (12 separate processes)

The Jenkinsfile runs 12 separate `dotnet run` invocations. Each spawns a new process and JIT-compiles. On a cold machine this adds 3–5 seconds per invocation plus full restore overhead.

**Fix** — publish once, invoke binary 12 times:

```groovy
stage('Build CLI Tool') {
    steps {
        sh '''
            dotnet publish src/B2A.DbTula.Cli/B2A.DbTula.Cli.csproj \
                --configuration Release \
                --runtime linux-x64 \
                --self-contained true \
                -p:PublishSingleFile=true \
                -o ./publish/
            chmod +x ./publish/B2A.DbTula.Cli
        '''
    }
}

stage('QA vs PROD Comparison') {
    steps {
        sh '''
            DBTULA=./publish/B2A.DbTula.Cli
            mkdir -p gh-pages/qa-vs-prod

            $DBTULA --source "$COMMONDB_QA" --target "$COMMONDB_PROD" \
                --source-label QA --target-label PROD \
                --title "Common (QA vs PROD)" \
                --out gh-pages/qa-vs-prod/common.html

            # ... repeat for other services
        '''
    }
}
```

### Problem 2 — No CI failure on drift

The pipeline always succeeds even when production drift is detected. Drift is invisible to anyone not reading the HTML report manually.

**Fix** — add `--fail-on-drift` flag (exits 1 on mismatch) and handle in Jenkins:

```groovy
stage('QA vs PROD Comparison') {
    steps {
        script {
            def driftDetected = false
            def services = ['common', 'community', 'inventory', 'payroll', 'purchase', 'sales']

            services.each { svc ->
                def exitCode = sh(
                    script: """
                        ./publish/B2A.DbTula.Cli \
                          --source "${env["${svc.toUpperCase()}DB_QA"]}" \
                          --target "${env["${svc.toUpperCase()}DB_PROD"]}" \
                          --fail-on-drift \
                          --source-label QA --target-label PROD \
                          --title "${svc.capitalize()} Schema (QA vs PROD)" \
                          --out gh-pages/qa-vs-prod/${svc}.html
                    """,
                    returnStatus: true
                )
                if (exitCode == 1 || exitCode == 2) {
                    echo "⚠️  Drift detected in ${svc} (exit code ${exitCode})"
                    driftDetected = true
                } else if (exitCode >= 3) {
                    error("Comparison failed for ${svc}")
                }
            }

            if (driftDetected) {
                currentBuild.result = 'UNSTABLE'
                // Reports still deploy — team can see exactly what drifted
            }
        }
    }
}
```

### Problem 3 — .NET SDK downloaded on every build

```groovy
// SLOW: downloads from internet every run
sh '''
    wget https://dot.net/v1/dotnet-install.sh
    ./dotnet-install.sh --channel 9.0
'''
```

**Fix** — use a Docker agent with .NET pre-installed:

```groovy
pipeline {
    agent {
        docker {
            image 'mcr.microsoft.com/dotnet/sdk:9.0-alpine'
            args '-v /var/run/docker.sock:/var/run/docker.sock'
        }
    }
    // Remove the 'Setup .NET' stage entirely
}
```

### Recommended pipeline shape

```
Checkout
  ↓
Restore & Publish binary (once)
  ↓
┌────────────────────────────┬────────────────────────────┐
│  QA vs PROD Comparison     │  QA vs TEST Comparison     │  ← parallel
│  (6+ services)             │  (6+ services)             │
└────────────────────────────┴────────────────────────────┘
  ↓ (both complete)
Archive HTML reports as Jenkins artifacts
  ↓
Deploy to OVH
  ↓
If driftDetected → mark UNSTABLE + notify Slack/email
```

---

## Safety Guardrails

### Mandatory for production use

1. **Read-only database role** — the comparison role should have `CONNECT` + `SELECT` on `pg_catalog.*` and `information_schema.*` only. `REVOKE CREATE ON SCHEMA public` from this role. Remove `EnsurePgGetTableDefFunctionExistsAsync` entirely (see Bug 5).

2. **`--read-only` flag** — when set, refuse to generate any sync script. Only produce the HTML report. This should be the default when `--target-label PROD`.

3. **`--fail-on-drift` flag** — non-zero exit code when mismatches exist, enabling CI gate.

4. **`--dry-run` for sync** — print sync script to stdout only. Never execute against the database.

5. **Destructive changes disabled by default** — the sync script generator produces DROP TABLE / DROP COLUMN only when `--allow-destructive` is explicitly passed.

6. **PROD guard** — when `--target-label PROD` and `--sync` are both set, require explicit `--i-understand-this-is-production` flag.

7. **Snapshot timestamp in report** — always display when each database snapshot was taken. Stale connections can produce misleading results.

---

## Extensibility

### Adding a new database provider (e.g., SQL Server)

The factory is the only registration point. No changes to Core or CLI.

```csharp
// 1. New project: B2A.DbTula.Infrastructure.SqlServer
public class SqlServerSchemaProvider : IDatabaseSchemaProvider, IDatabaseSchemaSnapshot
{
    public DbProviderKind ProviderKind => DbProviderKind.SqlServer;

    // Use sys.* catalog views for metadata
    // Implement TakeSnapshotAsync() using bulk queries against sys.columns, sys.indexes, etc.
}

// 2. Register in SchemaProviderFactory.cs — only change needed outside the new project
return dbType switch
{
    DbType.Postgres  => new PostgresSchemaProvider(...),
    DbType.MySql     => new MySqlSchemaProvider(...),
    DbType.SqlServer => new SqlServerSchemaProvider(...),   // add here
    _ => throw new NotSupportedException($"DB type {dbType} not supported")
};
```

---

## Testing Plan

### Level 1 — Unit tests (`B2A.DbTula.Core.Tests`)

No database required. Test pure logic.

```csharp
// DefinitionCanonicalizer does not corrupt qualified names inside function bodies
[Fact]
public void Canonicalizer_DoesNotCorruptQualifiedNames()
{
    var input = "CREATE FUNCTION foo() RETURNS text AS $$ SELECT t.col::text FROM tbl t; $$ LANGUAGE sql;";
    var result = DefinitionCanonicalizer.CanonicalizeDefinition(
        input, "postgres", new ComparisonOptions { IgnoreOwnership = true });
    Assert.Contains("t.col::text", result);
}

// IndexDefinition.GetStructuralKey preserves column order
[Fact]
public void IndexStructuralKey_PreservesColumnOrder()
{
    var idx1 = new IndexDefinition { Columns = ["city", "state"], IndexType = "btree" };
    var idx2 = new IndexDefinition { Columns = ["state", "city"], IndexType = "btree" };
    Assert.NotEqual(idx1.GetStructuralKey(), idx2.GetStructuralKey());
}

// Numeric precision change is detected
[Fact]
public void ColumnDefinition_DetectsNumericPrecisionChange()
{
    var src = new ColumnDefinition { Name = "amount", DataType = "numeric", NumericPrecision = 18, NumericScale = 4 };
    var tgt = new ColumnDefinition { Name = "amount", DataType = "numeric", NumericPrecision = 10, NumericScale = 2 };
    Assert.NotEqual(src, tgt);
}

// FK cascade rule change is detected
[Fact]
public void ForeignKey_DetectsCascadeRuleChange()
{
    var src = new ForeignKeyDefinition { ColumnName = "customer_id", ReferencedTable = "customers", OnDelete = "CASCADE" };
    var tgt = new ForeignKeyDefinition { ColumnName = "customer_id", ReferencedTable = "customers", OnDelete = "NO ACTION" };
    Assert.NotEqual(src.GetStructuralKey(), tgt.GetStructuralKey());
}
```

### Level 2 — Integration tests (`B2A.DbTula.Integration.Tests`)

Use `Testcontainers.PostgreSql` (NuGet package `Testcontainers.PostgreSql`) to spin up a real Postgres instance per test class. No mocking.

```csharp
public class BulkFetcherTests : IAsyncLifetime
{
    private PostgreSqlContainer _pg = null!;
    private DatabaseConnection _conn = null!;

    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await _pg.StartAsync();
        _conn = new DatabaseConnection(_pg.GetConnectionString(), ...);
        await SeedTestSchema(_conn);
    }

    public async Task DisposeAsync() => await _pg.DisposeAsync();

    [Fact]
    public async Task IndexQuery_PreservesColumnOrder()
    {
        await _conn.ExecuteCommandAsync(
            "CREATE INDEX idx_test ON orders (status, created_at, customer_id);");

        var snapshot = await new BulkSchemaFetcher(_conn).TakeSnapshotAsync();
        var idx = snapshot.IndexesByTable["orders"].First(i => i.Name == "idx_test");

        Assert.Equal(new[] { "status", "created_at", "customer_id" }, idx.Columns);
    }

    [Fact]
    public async Task MatviewDetection_UsesSystemCatalog_NotStringHeuristic()
    {
        await _conn.ExecuteCommandAsync(
            "CREATE MATERIALIZED VIEW payment_mv AS SELECT 1 AS id;");
        await _conn.ExecuteCommandAsync(
            "CREATE TABLE payment_mv_ledger (id int);");

        var snapshot = await new BulkSchemaFetcher(_conn).TakeSnapshotAsync();

        Assert.Contains("payment_mv", snapshot.MaterializedViewNames);
        Assert.DoesNotContain("payment_mv_ledger", snapshot.MaterializedViewNames);
    }

    [Fact]
    public async Task ForeignKey_FetchesCascadeActions()
    {
        await _conn.ExecuteCommandAsync(@"
            CREATE TABLE parents (id int PRIMARY KEY);
            CREATE TABLE children (
                id int PRIMARY KEY,
                parent_id int REFERENCES parents(id) ON DELETE CASCADE ON UPDATE RESTRICT
            );");

        var snapshot = await new BulkSchemaFetcher(_conn).TakeSnapshotAsync();
        var fk = snapshot.ForeignKeysByTable["children"].First();

        Assert.Equal("CASCADE",  fk.OnDelete);
        Assert.Equal("RESTRICT", fk.OnUpdate);
    }
}
```

### Level 3 — Golden file tests (`B2A.DbTula.GoldenFile.Tests`)

Fix a known schema state, run comparison, assert the sync script exactly matches a committed `.golden.sql` file. Catches regressions in script generation.

```csharp
[Fact]
public async Task SyncScript_MissingTable_MatchesGoldenFile()
{
    // Arrange: source has table, target does not
    var results = new List<ComparisonResult>
    {
        new() {
            ObjectType = SchemaObjectType.Table,
            Name = "payment_methods",
            Status = ComparisonStatus.MissingInTarget,
            // ... sub-results with column definitions
        }
    };

    // Act
    var script = new SyncScriptGenerator().Generate(results, SyncScriptOptions.SafeOnly);

    // Assert
    var golden = await File.ReadAllTextAsync("GoldenFiles/missing_table_payment_methods.golden.sql");
    Assert.Equal(golden.Trim(), script.SafeSection.Trim());
}
```

### Test coverage priorities

| Scenario | Priority |
|---|---|
| Column added in source, missing in target | P0 |
| Column type changed (safe cast) | P0 |
| Column numeric precision reduced | P0 |
| FK missing in target | P0 |
| FK cascade rule changed | P0 |
| Index column order (correct detection) | P0 |
| Materialized view detection via `pg_class` | P0 |
| Function body changed | P1 |
| Sequence definition changed (increment, max) | P1 |
| Check constraint missing | P1 |
| Enum value added | P1 |
| Canonicalization does not corrupt function bodies | P1 |
| Partial index predicate comparison | P2 |
| Ownership removal is noise-free | P2 |

---

## Build vs Buy Recommendation

### Honest evaluation of alternatives

| Tool | Strengths | Why it does not replace dbtula |
|---|---|---|
| **migra** (Python) | Excellent Postgres diff, uses pg_dump | Raw ALTER output only; no HTML report; no multi-DB batch; no C#/.NET integration; Python dependency |
| **Liquibase** | Mature, multi-DB, CI-ready | Change-set model (tracks history), not point-in-time drift detection; requires managing XML/YAML changelogs; significant operational overhead |
| **Flyway** | Simple, migration-friendly | Migration-only; no environment-to-environment comparison |
| **Atlas** | Modern, declarative HCL-based | Great for greenfield; brownfield inspection requires schema "pulling" and plan generation; no existing C# integration |
| **pg_dump + diff** | Zero dependencies | Raw text diff is unreadable; cannot classify changes or produce structured reports |
| **SchemaZen** | Structured SQL Server scripts | SQL Server only |
| **Skeema** | MySQL schema management | MySQL only |

### Recommendation: continue building dbtula

The reasons:

1. **Multi-service batch** — you compare 6–10 databases per Jenkins run, generating one report per service, all from one pipeline invocation. No off-the-shelf tool supports this natively with your Jenkins + OVH model.

2. **HTML reports with drill-down** — no tool generates a per-service tabbed HTML report with side-by-side SQL diff. This is genuinely custom.

3. **CI fail-on-drift gate** — your requirement is that the Jenkins build goes UNSTABLE when production drift is detected. This needs a tool you control.

4. **Most of the hard work is done** — provider abstraction, semantic comparison, canonicalization, HTML generation, credential management, and batch processing are already built. The gap between current state and production grade is fixing six bugs and adding bulk fetching: roughly 2–3 weeks, not a rewrite.

5. **Full ownership** — for a financial system (Dhanman), you want to understand exactly what your comparison tool does and does not detect. Black-box tools carry hidden assumptions.

**One recommendation alongside dbtula:** use `migra` as a validation oracle during integration testing. Run dbtula and migra against the same two databases. Any object that migra reports as different but dbtula reports as matching is a bug to investigate. This gives you an independent correctness check at low cost.

---

## Step-by-Step Refactoring Roadmap

### Phase 1: Fix production bugs (1–2 weeks) — do this first

These six bugs cause incorrect comparison results today on real databases.

| # | Task | File(s) |
|---|---|---|
| 1 | Remove `EnsurePgGetTableDefFunctionExistsAsync` and its call site entirely | `SchemaFetcher.cs`, `GetTableDefinitionAsync` |
| 2 | Fix index query — replace `ANY(ix.indkey)` with `unnest ... WITH ORDINALITY`, add `indisvalid = true`, add `indisprimary = false` | `SchemaFetcher.GetIndexesAsync` |
| 3 | Fix materialized view detection — query `pg_class WHERE relkind = 'm'`, cache as `HashSet<string>` | `SchemaComparer.IsMaterializedViewAsync` |
| 4 | Fix canonicalizer regex — replace wildcard `\b\w+\.` strip with explicit line-level OWNER/GRANT removals | `DefinitionCanonicalizer.cs` |
| 5 | Add `NumericPrecision`, `NumericScale`, `DateTimePrecision` to column query and `ColumnDefinition.Equals()` | `SchemaFetcher.GetColumnsAsync`, `ColumnDefinition.cs` |
| 6 | Add `OnDelete`, `OnUpdate` to FK query and `ForeignKeyDefinition.GetStructuralKey()` | `SchemaFetcher.GetForeignKeysAsync`, `ForeignKeyDefinition.cs` |

### Phase 2: Performance (1 week)

| # | Task |
|---|---|
| 7 | Add `SchemaSnapshot` record and `IDatabaseSchemaSnapshot` interface to Core |
| 8 | Implement `BulkSchemaFetcher` in Postgres infrastructure with all bulk queries |
| 9 | Make `PostgresSchemaProvider` implement `IDatabaseSchemaSnapshot` |
| 10 | Refactor `SchemaComparer.CompareAsync` to take two snapshots in parallel, then compare in-memory |
| 11 | Add `DbProviderKind` enum and `ProviderKind` property to `IDatabaseSchemaProvider`; remove reflection-based detection |

### Phase 3: Missing coverage (2 weeks)

| # | Task |
|---|---|
| 12 | Add `CheckConstraintDefinition` model + bulk query + comparison |
| 13 | Replace sequence existence-only comparison with full definition comparison (increment, min, max, cycle, type) |
| 14 | Add `EnumTypeDefinition` model + bulk query + comparison |
| 15 | Add `DataTypeNormalizer` — normalize `character varying` ↔ `varchar`, `integer` ↔ `int4` |

### Phase 4: Reporting (1 week)

| # | Task |
|---|---|
| 16 | Add `--fail-on-drift` CLI flag with exit codes 0/1/2/3 |
| 17 | Add risk classification to HTML report (safe / risky / destructive sections) |
| 18 | Add per-service summary index page linking all service reports with traffic-light status |
| 19 | Add Markdown report output for embedding in GitHub PR comments or Slack |

### Phase 5: Safe sync script generation (2 weeks)

| # | Task |
|---|---|
| 20 | Implement `SyncScriptGenerator` with safe/risky/destructive categorisation |
| 21 | Implement topological table ordering (Kahn's algorithm on FK dependencies) |
| 22 | Add `--generate-sync`, `--safe-only`, `--allow-risky`, `--allow-destructive` CLI flags |
| 23 | Golden file tests for sync script output |

### Phase 6: Testing infrastructure (1 week, run in parallel with phases above)

| # | Task |
|---|---|
| 24 | Create `B2A.DbTula.Core.Tests` — unit tests for canonicalizer, structural keys, column/FK equality |
| 25 | Create `B2A.DbTula.Integration.Tests` — use `Testcontainers.PostgreSql`; test bulk fetcher, index order, FK actions, check constraints, matview detection |
| 26 | Create `B2A.DbTula.GoldenFile.Tests` — golden file tests for sync script generation |

### Phase 7: CI/CD hardening (1 week)

| # | Task |
|---|---|
| 27 | Switch Jenkinsfile to `dotnet publish` (single-file binary) instead of `dotnet run` |
| 28 | Use Docker agent (`mcr.microsoft.com/dotnet/sdk:9.0`) — remove `wget dotnet-install.sh` stage |
| 29 | Add `--fail-on-drift` to QA vs PROD stage; mark build `UNSTABLE` on drift |
| 30 | Archive HTML reports as Jenkins artifacts (accessible without navigating to OVH) |
| 31 | Run QA vs PROD and QA vs TEST stages in parallel |

---

## Quick-Reference: Files to Change in Phase 1

| File | Change Required |
|---|---|
| `SchemaFetcher.cs` | Replace index query with `unnest ... WITH ORDINALITY` version. Delete `EnsurePgGetTableDefFunctionExistsAsync` and its call in `GetTableDefinitionAsync`. Add `numeric_precision`, `numeric_scale` to column query. |
| `SchemaComparer.cs` | Replace `IsMaterializedViewAsync` string heuristic with real `pg_class` query. Remove or properly implement `IsValidPrimaryKeyAsync` and `IsValidIndexAsync`. |
| `DefinitionCanonicalizer.cs` | Replace wildcard `\b\w+\.` regex removal with explicit line-level OWNER/GRANT/SET removals only. |
| `ColumnDefinition.cs` | Add `NumericPrecision int?`, `NumericScale int?`, `DateTimePrecision int?` properties. Update `Equals()` and `GetHashCode()`. |
| `ForeignKeyDefinition.cs` | Add `OnDelete string` and `OnUpdate string` properties. Update `GetStructuralKey()` to include them. |
| `SchemaFetcher.GetForeignKeysAsync` | Add `confupdtype`/`confdeltype` decode columns to the FK query (see bulk FK query above). |
| `IDatabaseSchemaProvider.cs` | Add `DbProviderKind ProviderKind { get; }` property. |
| `PostgresSchemaProvider.cs` | Implement `ProviderKind => DbProviderKind.Postgres`. |
| `MySqlSchemaProvider.cs` | Implement `ProviderKind => DbProviderKind.MySql`. |

---

*This document was generated from a full codebase review of the `B2A.DbTula.*` solution as of commit `8d320df`. It should be updated after each phase completes.*

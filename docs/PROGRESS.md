# dbtula Implementation Progress

> This file is updated at the end of every Claude Code session.  
> **Always read this first when resuming work.**  
> Full architecture review and rationale: `docs/ARCHITECTURE_REVIEW.md`

---

## How to Resume

1. Open Claude Code CLI in `C:\Users\bhara\Source\repos\db-tula`
2. Say: **"Resume dbtula implementation — check PROGRESS.md"**
3. Claude will read this file + memory files and pick up exactly where work stopped

---

## Phase 1 — Fix 6 Production Bugs (Current Phase)

| # | Task | Status | Notes |
|---|---|---|---|
| 1 | Bug 5: Remove `EnsurePgGetTableDefFunctionExistsAsync` | ⏳ In Progress | `SchemaFetcher.cs` |
| 2 | Bug 1: Fix index query column order | ⏳ Pending | `SchemaFetcher.GetIndexesAsync` |
| 3 | Bug 2: Fix materialized view detection | ⏳ Pending | `SchemaComparer.IsMaterializedViewAsync` |
| 4 | Bug 4: Fix canonicalizer regex | ⏳ Pending | `DefinitionCanonicalizer.cs` |
| 5 | Bug 6a: Add NumericPrecision/Scale to ColumnDefinition | ⏳ Pending | `ColumnDefinition.cs` + query |
| 6 | Bug 6b: Add OnDelete/OnUpdate to ForeignKeyDefinition | ⏳ Pending | `ForeignKeyDefinition.cs` + query |
| 7 | Add DbProviderKind + ProviderKind property | ⏳ Pending | Replace reflection-based type detection |

## Phase 2 — Performance (Bulk Snapshot)

| # | Task | Status |
|---|---|---|
| 8 | Add SchemaSnapshot + IDatabaseSchemaSnapshot to Core | ⏳ Pending |
| 9 | Implement BulkSchemaFetcher for Postgres | ⏳ Pending |
| 10 | Refactor SchemaComparer to consume SchemaSnapshot | ⏳ Pending |

## Phase 3 — Missing Coverage

| # | Task | Status |
|---|---|---|
| 11 | Add CheckConstraintDefinition model + comparison | ⏳ Pending |
| 12 | Full sequence definition comparison | ⏳ Pending |
| 13 | Add EnumTypeDefinition model + comparison | ⏳ Pending |

## Phase 4 — Reporting

| # | Task | Status |
|---|---|---|
| 14 | Add --fail-on-drift exit codes | ⏳ Pending |

## Phase 5 — Sync Script Generation

| # | Task | Status |
|---|---|---|
| 15 | Implement SyncScriptGenerator | ⏳ Pending |

## Phase 6 — Tests

| # | Task | Status |
|---|---|---|
| 16 | Create B2A.DbTula.Core.Tests (unit tests) | ⏳ Pending |
| 17 | Create B2A.DbTula.Integration.Tests (Docker Postgres) | ⏳ Pending |

## Phase 7 — CI/CD Hardening

| # | Task | Status |
|---|---|---|
| 18 | Harden Jenkinsfile | ⏳ Pending |

---

## Session Log

| Date | Completed | Notes |
|---|---|---|
| 2026-05-28 | Architecture review, memory files, task creation | Starting Phase 1 implementation |

---

## Key File Locations

| What | Where |
|---|---|
| Architecture review | `docs/ARCHITECTURE_REVIEW.md` |
| Memory files | `C:\Users\bhara\.claude\projects\C--Users-bhara-Source-repos-db-tula\memory\` |
| Postgres SQL queries | `src/B2A.DbTula.Infrastructure.Postgres/SchemaFetcher.cs` |
| Comparison logic | `src/B2A.DbTula.Cli/SchemaComparer.cs` |
| Canonicalizer | `src/B2A.DbTula.Core/Utilities/DefinitionCanonicalizer.cs` |
| Column model | `src/B2A.DbTula.Core/Models/ColumnDefinition.cs` |
| FK model | `src/B2A.DbTula.Core/Models/ForeignKeyDefinition.cs` |
| Provider interface | `src/B2A.DbTula.Core/Abstractions/IDatabaseSchemaProvider.cs` |
| Jenkins pipeline | `Jenkinsfile` (repo root) |

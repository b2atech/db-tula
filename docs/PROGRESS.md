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
| 1 | Bug 5: Remove `EnsurePgGetTableDefFunctionExistsAsync` | ✅ Done | commit `386f565` |
| 2 | Bug 1: Fix index query column order | ✅ Done | commit `386f565` |
| 3 | Bug 2: Fix materialized view detection | ✅ Done | commit `386f565` |
| 4 | Bug 4: Fix canonicalizer regex | ✅ Done | commit `386f565` |
| 5 | Bug 6a: Add NumericPrecision/Scale to ColumnDefinition | ✅ Done | commit `386f565` |
| 6 | Bug 6b: Add OnDelete/OnUpdate to ForeignKeyDefinition | ✅ Done | commit `386f565` |
| 7 | Add DbProviderKind + ProviderKind property | ✅ Done | commit `386f565` |

## Phase 2 — Performance (Bulk Snapshot)

| # | Task | Status |
|---|---|---|
| 8 | Add SchemaSnapshot + IDatabaseSchemaSnapshot to Core | ✅ Done | commit `a378aa6` |
| 9 | Implement BulkSchemaFetcher for Postgres | ✅ Done | commit `a378aa6` |
| 10 | Refactor SchemaComparer to consume SchemaSnapshot | ✅ Done | commit `a378aa6` |

## Phase 3 — Missing Coverage

| # | Task | Status |
|---|---|---|
| 11 | Add CheckConstraintDefinition model + comparison | ✅ Done | commit `a378aa6` |
| 12 | Full sequence definition comparison | ✅ Done | commit `a378aa6` |
| 13 | Add EnumTypeDefinition model + comparison | ✅ Done | commit `629110e` |

## Phase 4 — Reporting

| # | Task | Status | Notes |
|---|---|---|---|
| 14 | Add --fail-on-drift exit codes | ✅ Done | commit `629110e` |

## Phase 5 — Sync Script Generation

| # | Task | Status | Notes |
|---|---|---|---|
| 15 | Implement SyncScriptGenerator | ✅ Done | commit `629110e` |

## Phase 6 — Tests

| # | Task | Status | Notes |
|---|---|---|---|
| 16 | Create B2A.DbTula.Core.Tests (unit tests) | ✅ Done | 38/38 passing — commit `629110e` |
| 17 | Create B2A.DbTula.Integration.Tests (Docker Postgres) | ✅ Done | 11 tests — commit `629110e` |

## Phase 7 — CI/CD Hardening

| # | Task | Status | Notes |
|---|---|---|---|
| 18 | Harden Jenkinsfile | ✅ Done | commit `629110e` |

---

## Session Log

| Date | Completed | Notes |
|---|---|---|
| 2026-05-28 | Architecture review, memory files, task creation | — |
| 2026-05-28 | **Phase 1 complete** — all 7 tasks (6 bugs + ProviderKind) | commit `386f565`, 0 build errors |
| 2026-05-28 | **Phase 2+3 complete** — bulk snapshot (26 queries total), check constraints, full sequence comparison | commit `a378aa6`, 0 build errors |
| 2026-05-28 | **ALL PHASES COMPLETE (3-7)** — enums, exit codes, sync generator, 38 unit tests, 11 integration tests, Jenkinsfile | commit `629110e` |

---

## Phase 8 — Web Platform (db-tula Web UI)

New initiative started 2026-05-31. Goal: React web app with Google OAuth, on-demand comparisons, live results, admin sync apply.

| # | Task | Status | Notes |
|---|---|---|---|
| 19 | Create `B2A.DbTula.Api` project (ASP.NET Core Web API + SignalR) | ✅ Done | Added to B2A.DbTula.sln |
| 20 | EF Core models (AppUser, RegisteredDatabase, ComparisonRun, SyncApplyLog) | ✅ Done | Migration: `InitialCreate` |
| 21 | Google OAuth → JWT issuance (httpOnly cookie) | ✅ Done | `AuthController`, `AuthService` |
| 22 | `/api/databases` CRUD (credential encryption via Data Protection) | ✅ Done | `DatabasesController` |
| 23 | `/api/comparisons` + `ComparisonWorker` (background service + SignalR hub) | ✅ Done | Reuses existing SchemaComparer + SyncScriptGenerator |
| 24 | `/api/comparisons/{id}/apply-safe` (Admin only, write account, audit log) | ✅ Done | `ComparisonsController` |
| 25 | `/api/admin` (user management, audit log) | ✅ Done | `AdminController` |
| 26 | React frontend scaffold (Vite + TypeScript + Tailwind) | ✅ Done | `web/dbtula-web/` |
| 27 | React pages: Login, Dashboard, NewComparison, ComparisonResult, Databases, Admin | ✅ Done | Build passes clean |

**Remaining to configure before running:**
- Create `appsettings.Development.json` in `src/B2A.DbTula.Api/` with real DB credentials, JWT secret, Google Client ID
- Create `web/dbtula-web/.env` with `VITE_GOOGLE_CLIENT_ID=...`
- Create `dbtula_app` Postgres database on the app server (NOT prod)
- Run `dotnet ef database update` to apply migration
- Set up Google OAuth Client ID at console.cloud.google.com (add `http://localhost:5173` as authorized origin)
- Set up a write Postgres role for the target DBs with no DROP privilege

**Architecture decisions made:**
- API references Cli project directly (no separate Comparison library) — zero changes to existing code
- Comparisons use read-only credentials; sync apply uses separate write account (registered as `isWriteAccount=true`)
- SAFE changes only via UI; RISKY/DESTRUCTIVE = download SQL only
- JWT stored in httpOnly cookie, not localStorage
- First Google sign-in automatically becomes Admin

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





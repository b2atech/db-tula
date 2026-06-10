# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

**db-tula** is a database schema comparison tool for PostgreSQL and MySQL. It compares two schemas, reports structural differences, and generates migration/sync scripts. It ships in two forms that share the same comparison engine:

- A **CLI** (`B2A.DbTula.Cli`, published as the `dbtula` dotnet tool) for local/CI use, producing HTML reports.
- A **web platform** (ASP.NET Core API + React SPA) deployed at https://dbtula.dgtula.com, with auth, persisted runs, scheduled/batch comparisons, and live progress over SignalR.

Comparison is **ownership-agnostic by default** — owners, definers, schema/db prefixes, grants, and comments are normalized away so the same logical object compares equal across environments and across database engines.

## Build, test, run

Everything targets **.NET 9.0**. Solution: `B2A.DbTula.sln`.

```sh
dotnet build B2A.DbTula.sln              # build all projects
dotnet test                              # run all tests (xUnit)
dotnet test tests/B2A.DbTula.Core.Tests  # run one test project
dotnet test --filter "FullyQualifiedName~SchemaComparer"   # run a subset by name
```

Run the CLI locally:

```sh
dotnet run --project src/B2A.DbTula.Cli -- --help
dotnet run --project src/B2A.DbTula.Cli -- \
  --source "<conn>" --target "<conn>" --sourceType postgres --targetType mysql --out report.html
```

Run the API: `dotnet run --project src/B2A.DbTula.Api` (Swagger enabled in Development).

Web SPA (`web/dbtula-web`, Vite + React 19 + TS + Tailwind v4 + shadcn/ui):

```sh
npm install        # use install, NOT ci — lock file drifts
npm run dev        # local dev server
npm run build      # tsc -b && vite build
npm run lint
```

## Architecture

The dependency direction is **Core ← Infrastructure ← Cli ← Api**. Core has no DB-engine dependencies.

- **`B2A.DbTula.Core`** — engine-agnostic contracts and models. The central abstraction is `IDatabaseSchemaProvider` (reads tables, columns, keys, indexes, constraints, sequences, functions, procedures, views, triggers, enum types) plus `IDatabaseSchemaSnapshot` and `ISchemaComparer`. Models in `Models/` are the canonical, normalized representation of every comparable object. `Utilities/DefinitionCanonicalizer` strips ownership/definer/prefix/comment noise — this is the heart of ownership-agnostic comparison; changes here affect every object type.

- **`B2A.DbTula.Infrastructure.Postgres` / `.MySql`** — engine-specific implementations of the Core abstractions. `BulkSchemaFetcher` loads an entire schema into a `SchemaSnapshot` in bulk (performance path) rather than per-object round trips; `SchemaProvider` does per-object reads.

- **`B2A.DbTula.Cli`** — orchestration shared by CLI and API. `SchemaComparer` walks two snapshots and produces a `ComparisonResult` tree; `SyncScriptGenerator` emits migration SQL classified by risk (Safe / Risky / Destructive); `SchemaLinter` flags schema issues; `BatchProcessor` runs multi-DB jobs from JSON config. `Factories/SchemaProviderFactory` and `ComparisonRunnerFactory` build the right provider/runner for a `DbType`. CLI entry is `Program.cs` driven by `CliOptions`.

- **`B2A.DbTula.Api`** — ASP.NET Core 9 web API. **It reuses the CLI's `SchemaComparer`/`SyncScriptGenerator` directly** (see `Workers/ComparisonWorker.cs`) — do not fork comparison logic into the API. Comparisons run asynchronously: a request enqueues a run Id on a `Channel<Guid>`, `ComparisonWorker` (a `BackgroundService`) processes it and streams progress to clients via `ComparisonHub` (SignalR group `run-{runId}`). `StuckRunCleanupService` times out runs stuck longer than ~15 min. EF Core (`Data/AppDbContext`) on PostgreSQL persists Users, RegisteredDatabases, ComparisonRuns, ComparisonProfiles, BatchRuns, DriftMetrics, SyncStatements, SyncApplyLogs, AllowedEmails.

- **`web/dbtula-web`** — React SPA. `src/api/client.ts` is the single API client; `src/pages/` are the routed screens (Dashboard, NewComparison, ComparisonResult, History, Databases, Profiles, Admin, Login); `@microsoft/signalr` subscribes to live run progress; `@tanstack/react-query` handles server state.

## Conventions and gotchas

- **Auth:** Google OAuth → JWT stored in `sessionStorage` and sent as a `Bearer` header (deliberately not a cookie). Access is gated by an `AllowedEmails` allowlist managed via the Admin screen.
- **Connection-string secrets** are AES-256 encrypted at rest (`Services/CredentialService`) using a key supplied via env var. Production secrets live in systemd `Environment=` lines, **not** in `appsettings.Production.json`.
- **Drift exit codes:** the CLI supports `--fail-on-drift` for CI gating.
- **Sync scripts are risk-classified.** The web app only auto-applies Safe scripts; Risky/Destructive require authenticated manual download. Preserve this distinction when touching `SyncScriptGenerator`.
- **EF migrations:** generate against the API project; deploy migrations use `--configuration Release` (not `--no-build`).
- Namespaces are mostly `B2A.DbTula.*`, but Core's abstractions use `B2a.DbTula.Core.Abstractions` (lowercase `a`) — match the existing namespace of the file you edit.

## Project status & resuming work

`docs/PROGRESS.md` tracks implementation state and is updated at the end of each session — **read it first when resuming.** Deeper design rationale is in `docs/ARCHITECTURE_REVIEW.md`. Batch processing is documented in `BATCH_PROCESSING.md` / `QUICK_START_BATCH.md`. Deployment is via `Jenkinsfile` (deploy on commit; scheduled comparisons run nightly) with nginx config generated by the `gen_nginx*.sh` scripts.

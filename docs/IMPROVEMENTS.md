# db-tula — Improvement Backlog

> Documented 2026-06-01. Items are grouped by area and roughly prioritised within each group.
> None of these are bugs — the platform is functional. These are quality-of-life, reliability, and feature improvements.

---

## 🔴 High Priority (production reliability)

### 1. Refresh token / session persistence
**Problem:** JWT expires after 8 hours. If a user leaves the tab open overnight, they silently lose their session and API calls fail with 401 — no error shown, page just stops working.  
**Fix:** Add a refresh-token endpoint (`POST /api/auth/refresh`) that issues a new JWT without requiring Google re-auth. Store refresh token in sessionStorage alongside the access token. Intercept 401s in the API client and auto-refresh before retrying.

### 2. API error boundary in React
**Problem:** If the API is down or returns an unexpected error, React components crash silently or show blank sections with no user feedback.  
**Fix:** Wrap the app in a React `ErrorBoundary`. Add a global error handler in the API client that shows a toast notification on 5xx errors. Show a banner if `/api/auth/me` fails (API unreachable).

### 3. Comparison run timeout / retry
**Problem:** If a comparison run crashes mid-way (SEGV, OOM, DB timeout), the run stays in `Running` status forever with no way to retry from the UI.  
**Fix:** Add a background cleanup job that marks runs as `Failed` if they've been `Running` for more than 15 minutes. Add a "Retry" button on failed runs in the UI.

### 4. SEGV crash investigation
**Problem:** The API has crashed with `status=11/SEGV` during large comparison runs (likely memory pressure from serializing 325+ object comparison results into one JSON blob).  
**Fix:** Profile memory usage during a large run. Consider streaming the result incrementally rather than holding the full result in memory at once. Set a memory limit on the systemd service (`MemoryMax=512M`).

---

## 🟡 Medium Priority (UX improvements)

### 5. Pagination on comparison results table
**Problem:** All 325 objects load into the browser at once. For larger DBs (500+ objects) this becomes slow to render and filter.  
**Fix:** Server-side paginate the `resultJson` — instead of storing the full JSON blob, store per-object rows in a `ComparisonResultItem` table. API returns pages of 50 with filter params.

### 6. Email notification on drift
**Problem:** The CLI sends drift emails nightly (via `EmailService`), but UI-triggered comparisons do not send any notification.  
**Fix:** Wire up `EmailService` (already exists in the CLI project) into `ComparisonWorker`. After a run completes with drift > 0, send a summary email to all Admin-role users.

### 7. Profile grouping / batch run status
**Problem:** "Run All" queues 9 comparisons sequentially but there's no aggregate view showing "4 of 9 done, 2 failed".  
**Fix:** Add a `BatchRun` concept — one batch run ID groups multiple comparison runs. Dashboard shows batch progress as a progress bar. Email is sent once per batch, not per run.

### 8. Comparison diff for tables — column-level detail
**Problem:** The diff modal for table mismatches shows sub-results (columns, FKs, indexes) but the detail is text-only. Hard to read which specific column changed type.  
**Fix:** Parse sub-result details into structured display — show a two-column before/after table for column type changes, formatted FK rules etc.

### 9. Sync script preview before apply
**Problem:** Admin clicks "Apply X selected" without seeing the final SQL that will run. It's visible via expand-per-row but no "show all selected SQL" summary.  
**Fix:** Add a "Preview" button that opens a modal showing the combined SQL of all checked statements before applying.

### 10. Dark mode
**Problem:** Dark sidebar with light content area — looks good but no full dark mode option.  
**Fix:** Add a dark/light mode toggle using Tailwind's `dark:` variant. Store preference in localStorage.

---

## 🟢 Low Priority (nice to have)

### 11. Scheduled comparison per profile (not just nightly all-at-once)
**Problem:** All 9 profiles run at midnight IST together. If Common takes 30 seconds, all 9 take ~5 minutes sequentially.  
**Fix:** Add a `CronExpression` field to `ComparisonProfile`. Each profile runs on its own schedule. Jenkins calls the trigger endpoint; the API queues only due profiles.

### 12. Write account sync for all safe changes (current: per-run)
**Problem:** "Apply Safe Changes" applies the safe script from one specific run. If a new column was added in QA 3 runs ago and hasn't been synced yet, admin has to find that run manually.  
**Fix:** Add an "Apply all pending safe changes" flow that consolidates safe statements across all unsynced runs for a profile into a single deduplicated script.

### 13. QA vs UAT profiles
**Problem:** Only QA vs PROD comparisons are registered. The `batch-qa-vs-test.json` exists in the CLI but no UAT databases are registered in the UI.  
**Fix:** Add QA UAT database registrations and QA vs UAT profiles (once UAT credentials are available).

### 14. Run history search / filter
**Problem:** History page lists runs newest-first with no way to search by profile name or filter by status.  
**Fix:** Add a search input and status filter dropdown to the Run History page. Add date-range filter.

### 15. Certbot auto-renewal verification
**Problem:** The Let's Encrypt cert expires 2026-08-29. Certbot's systemd timer should auto-renew, but this hasn't been verified.  
**Fix:** Add a Jenkins stage (monthly) that SSHes into the server and runs `certbot renew --dry-run` to confirm renewal works. Alert on failure.

### 16. Structured logging / observability
**Problem:** Logs go to systemd journal only. No way to query "how many runs failed last week" or "average comparison duration per profile".  
**Fix:** Add structured logging with Serilog (already used in CLI). Write to a file sink or push to a logging service. Add a `/api/admin/metrics/raw` endpoint for admin diagnostics.

### 17. Multi-tenant / organisation support
**Problem:** Currently single-tenant — one set of databases for all users. No concept of teams or isolated environments.  
**Fix:** Add an `Organisation` entity. Users belong to organisations. DB registrations and profiles are scoped to organisations. Different teams can manage their own DB sets.

### 18. CLI + API connection string sharing
**Problem:** Database connection strings are registered separately in the UI (encrypted AES) AND in Jenkins as credentials for the CLI. Two places to maintain.  
**Fix:** Add a `GET /api/databases/export-batch-config` endpoint (Admin only) that exports the registered databases as a `batch-qa-vs-prod.json` with connection strings (one-time plaintext export, secured). Jenkins can pull this instead of maintaining its own credentials.

### 19. Mobile-responsive layout
**Problem:** The sidebar takes 240px — on a tablet or small laptop it crowds the content area significantly.  
**Fix:** Add a collapsible sidebar with hamburger toggle on screens < 1024px. Sidebar collapses to icon-only mode on medium screens.

### 20. Comparison result caching
**Problem:** Every time you navigate to `/results/{id}`, the full `resultJson` (potentially 500KB+) is fetched from the API and parsed again.  
**Fix:** React Query already caches it in memory for the session. For persistent caching: add `staleTime: Infinity` on completed runs (they never change) and use `localStorage` for the most recent 5 run results.

---

## 🔧 Technical Debt

| Item | What | Why |
|---|---|---|
| `B2A.DbTula.Cli.DbType` collision | `DbType` enum name clashes with our `Data.DbKind` — both in scope | Rename CLI's `DbType` to `CliDbType` in a future CLI refactor |
| `resultJson` TEXT column | 325 objects as one JSON blob in a TEXT column | Migrate to `ComparisonResultItem` table rows for queryability |
| No integration tests for API | ComparisonWorker and controllers have no tests | Add xUnit tests with Testcontainers for the API layer |
| JWT secret in appsettings | `"Secret": "dbtula-jwt-secret-2026-b2a-tech-32chars!!"` is weak and in plaintext | Move to environment variable or Azure Key Vault |
| AES key in appsettings | `EncryptionKey` is in `appsettings.Production.json` on server | Move to environment variable: `AUTH__ENCRYPTIONKEY` |
| No database index on `ComparisonRuns.StartedAt` | Listing recent runs does a sequential scan | Add index via EF Core migration |
| `ComparisonWorker` SEGV | Root cause not identified — may be RazorLight or Npgsql native code | Profile with dotnet-trace on a large run |

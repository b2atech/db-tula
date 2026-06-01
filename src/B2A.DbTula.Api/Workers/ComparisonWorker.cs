using System.Text.Json;
using System.Text.Json.Serialization;
using B2A.DbTula.Cli.Services;
using System.Threading.Channels;
using B2A.DbTula.Api.Data;
using B2A.DbTula.Api.Hubs;
using B2A.DbTula.Api.Services;
using B2A.DbTula.Cli;
using B2A.DbTula.Cli.Factories;
using B2A.DbTula.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace B2A.DbTula.Api.Workers;

public class ComparisonWorker(
    Channel<Guid> queue,
    IServiceScopeFactory scopeFactory,
    IHubContext<ComparisonHub> hub,
    ILogger<ComparisonWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var runId in queue.Reader.ReadAllAsync(ct))
        {
            await ProcessAsync(runId, ct);
        }
    }

    private async Task ProcessAsync(Guid runId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var creds = scope.ServiceProvider.GetRequiredService<CredentialService>();

        var run = await db.ComparisonRuns
            .Include(r => r.SourceDb)
            .Include(r => r.TargetDb)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);

        if (run is null) return;

        run.Status = RunStatus.Running;
        await db.SaveChangesAsync(ct);

        async Task SendProgress(string msg)
        {
            logger.LogInformation("[Run {RunId}] {Msg}", runId, msg);
            await hub.Clients.Group($"run-{runId}").SendAsync("progress", new { type = "progress", message = msg }, ct);
        }

        try
        {
            await SendProgress("Decrypting credentials...");
            var sourceCs = creds.Decrypt(run.SourceDb.ConnectionStringEncrypted);
            var targetCs = creds.Decrypt(run.TargetDb.ConnectionStringEncrypted);

            await SendProgress("Connecting to source database...");
            var sourceProvider = SchemaProviderFactory.Create(
                Map(run.SourceDb.DbType), sourceCs,
                (_, _, msg, _) => _ = SendProgress(msg));

            await SendProgress("Connecting to target database...");
            var targetProvider = SchemaProviderFactory.Create(
                Map(run.TargetDb.DbType), targetCs,
                (_, _, msg, _) => _ = SendProgress(msg));

            await SendProgress("Taking schema snapshots (parallel)...");
            var comparer = new SchemaComparer();
            var results = await comparer.CompareAsync(
                sourceProvider, targetProvider,
                progressLogger: (_, _, msg, _) => _ = SendProgress(msg),
                options: new() { SourceLabel = run.SourceDb.Name, TargetLabel = run.TargetDb.Name });

            SchemaLinter.Annotate(results);

            await SendProgress("Generating sync scripts...");
            var gen = new SyncScriptGenerator();
            var syncScript = gen.Generate(results, SyncScriptOptions.Full, comparer.LastSourceSnapshot);

            run.SyncScriptSafe = RenderSection(syncScript.Safe, "SAFE CHANGES");
            run.SyncScriptRisky = RenderSection(syncScript.Risky, "RISKY CHANGES");
            run.SyncScriptDestructive = RenderSection(syncScript.Destructive, "DESTRUCTIVE CHANGES");

            var summary = new
            {
                match = results.Count(r => r.Status == Core.Enums.ComparisonStatus.Match),
                mismatch = results.Count(r => r.Status == Core.Enums.ComparisonStatus.Mismatch),
                missingInTarget = results.Count(r => r.Status == Core.Enums.ComparisonStatus.MissingInTarget),
                missingInSource = results.Count(r => r.Status == Core.Enums.ComparisonStatus.MissingInSource),
            };

            // Serialize with camelCase + string enums so frontend can parse directly
            var jsonOpts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
            run.ResultJson = JsonSerializer.Serialize(results, jsonOpts);
            run.SummaryJson = JsonSerializer.Serialize(summary);
            run.Status = RunStatus.Completed;
            run.CompletedAt = DateTime.UtcNow;

            // Persist per-object-type drift metrics for charting
            await SendProgress("Saving drift metrics...");
            var metricsByType = results
                .GroupBy(r => r.ObjectType.ToString())
                .Select(g => new DriftMetric
                {
                    ComparisonRunId = run.Id,
                    RunDate = DateTime.UtcNow,
                    ObjectType = g.Key,
                    MatchCount = g.Count(r => r.Status == Core.Enums.ComparisonStatus.Match),
                    MismatchCount = g.Count(r => r.Status == Core.Enums.ComparisonStatus.Mismatch),
                    MissingInTargetCount = g.Count(r => r.Status == Core.Enums.ComparisonStatus.MissingInTarget),
                    MissingInSourceCount = g.Count(r => r.Status == Core.Enums.ComparisonStatus.MissingInSource),
                });
            db.DriftMetrics.AddRange(metricsByType);

            // Persist individual sync statements for statement-level approval
            var allStatements = syncScript.Safe
                .Select((s, i) => new DbSyncStatement { ComparisonRunId = run.Id, Category = "Safe", ObjectType = s.ObjectType, ObjectName = s.ObjectName, Sql = s.Sql, Comment = s.Comment, OrderIndex = i })
                .Concat(syncScript.Risky.Select((s, i) => new DbSyncStatement { ComparisonRunId = run.Id, Category = "Risky", ObjectType = s.ObjectType, ObjectName = s.ObjectName, Sql = s.Sql, Comment = s.Comment, OrderIndex = i }))
                .Concat(syncScript.Destructive.Select((s, i) => new DbSyncStatement { ComparisonRunId = run.Id, Category = "Destructive", ObjectType = s.ObjectType, ObjectName = s.ObjectName, Sql = s.Sql, Comment = s.Comment, OrderIndex = i, IsApproved = false }));
            db.SyncStatements.AddRange(allStatements);

            await db.SaveChangesAsync(ct);
            await hub.Clients.Group($"run-{runId}").SendAsync("done", new { type = "done", status = "Completed", summary }, ct);

            // Email admins if drift detected
            var drift = (summary.mismatch + summary.missingInTarget + summary.missingInSource);
            if (drift > 0)
                await TrySendDriftEmailAsync(db, run, results, summary, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Comparison run {RunId} failed", runId);
            run.Status = RunStatus.Failed;
            run.ErrorMessage = ex.Message;
            run.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await hub.Clients.Group($"run-{runId}").SendAsync("done", new { type = "done", status = "Failed", error = ex.Message }, ct);
        }
    }

    private async Task TrySendDriftEmailAsync(AppDbContext db, ComparisonRun run, IList<B2A.DbTula.Core.Models.ComparisonResult> results, dynamic summary, CancellationToken ct)
    {
        try
        {
            var emailConfig = EmailService.ReadFromEnvironment();
            if (emailConfig is null) return;

            // Override To: with all Admin emails from DB
            var adminEmails = await db.Users
                .Where(u => u.Role == UserRole.Admin)
                .Select(u => u.Email)
                .ToListAsync(ct);
            if (!adminEmails.Any()) return;

            emailConfig = emailConfig with { To = adminEmails.ToArray() };

            var title = run.Profile?.Name ?? $"{run.SourceDb?.Name} → {run.TargetDb?.Name}";
            var body = EmailService.BuildDriftEmailBody(
                title, run.SourceDb?.Name ?? "Source", run.TargetDb?.Name ?? "Target",
                results, summary.match, summary.mismatch, summary.missingInTarget, summary.missingInSource);

            await EmailService.SendDriftReportAsync(emailConfig,
                $"⚠️ Schema Drift — {title} ({summary.mismatch + summary.missingInTarget + summary.missingInSource} issue(s))",
                body, null);

            logger.LogInformation("Drift email sent for run {RunId}", run.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send drift email for run {RunId}", run.Id);
        }
    }

    private static string RenderSection(List<SyncStatement> stmts, string title)
    {
        if (!stmts.Any()) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"-- {title}");
        sb.AppendLine();
        foreach (var s in stmts)
        {
            sb.AppendLine($"-- [{s.ObjectType}: {s.ObjectName}] {s.Comment}");
            sb.AppendLine(s.Sql.Trim());
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static B2A.DbTula.Cli.DbType Map(DbKind kind) => kind switch
    {
        DbKind.Postgres => B2A.DbTula.Cli.DbType.Postgres,
        DbKind.MySql => B2A.DbTula.Cli.DbType.MySql,
        _ => throw new NotSupportedException()
    };
}

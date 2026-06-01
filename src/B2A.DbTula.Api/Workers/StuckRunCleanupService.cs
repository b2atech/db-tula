using B2A.DbTula.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace B2A.DbTula.Api.Workers;

public class StuckRunCleanupService(IServiceScopeFactory scopeFactory, ILogger<StuckRunCleanupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait 1 minute after startup before first check
        await Task.Delay(TimeSpan.FromMinutes(1), ct);

        while (!ct.IsCancellationRequested)
        {
            await CleanupAsync(ct);
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff = DateTime.UtcNow.AddMinutes(-15);
        var stuck = await db.ComparisonRuns
            .Where(r => r.Status == RunStatus.Running && r.StartedAt < cutoff)
            .ToListAsync(ct);

        if (!stuck.Any()) return;

        foreach (var run in stuck)
        {
            run.Status = RunStatus.Failed;
            run.CompletedAt = DateTime.UtcNow;
            run.ErrorMessage = "Run timed out after 15 minutes — likely crashed. Use Retry to run again.";
            logger.LogWarning("Marked stuck run {RunId} as Failed (started {StartedAt})", run.Id, run.StartedAt);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Cleaned up {Count} stuck run(s)", stuck.Count);
    }
}

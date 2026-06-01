using B2A.DbTula.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.Channels;
using B2A.DbTula.Api.Hubs;

namespace B2A.DbTula.Api.Controllers;

[ApiController]
[Route("api/batch-runs")]
[Authorize]
public class BatchRunController(AppDbContext db, Channel<Guid> queue, IHubContext<ComparisonHub> hub) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> RunAll()
    {
        var userId = GetUserId();
        var profiles = await db.Profiles.ToListAsync();
        if (!profiles.Any()) return BadRequest("No profiles configured");

        var batch = new BatchRun
        {
            Name = $"All profiles — {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            TotalRuns = profiles.Count,
            InitiatedById = userId
        };
        db.BatchRuns.Add(batch);

        foreach (var profile in profiles)
        {
            var run = new ComparisonRun
            {
                ProfileId = profile.Id,
                SourceDbId = profile.SourceDbId,
                TargetDbId = profile.TargetDbId,
                InitiatedById = userId,
                BatchRunId = batch.Id
            };
            db.ComparisonRuns.Add(run);
        }

        await db.SaveChangesAsync();

        // Queue all runs
        foreach (var run in db.ComparisonRuns.Local.Where(r => r.BatchRunId == batch.Id))
            await queue.Writer.WriteAsync(run.Id);

        return Accepted(new { batchRunId = batch.Id, totalRuns = profiles.Count });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var batch = await db.BatchRuns.FindAsync(id);
        if (batch is null) return NotFound();

        var runs = await db.ComparisonRuns
            .Where(r => r.BatchRunId == id)
            .Select(r => new { r.Id, r.Status, r.ProfileId })
            .ToListAsync();

        var completed = runs.Count(r => r.Status == RunStatus.Completed);
        var failed = runs.Count(r => r.Status == RunStatus.Failed);
        var running = runs.Count(r => r.Status == RunStatus.Running || r.Status == RunStatus.Pending);

        var isComplete = running == 0 && (completed + failed) == batch.TotalRuns;
        if (isComplete && batch.CompletedAt == null)
        {
            batch.CompletedAt = DateTime.UtcNow;
            batch.CompletedRuns = completed;
            batch.FailedRuns = failed;
            await db.SaveChangesAsync();
        }

        return Ok(new
        {
            batch.Id, batch.Name, batch.TotalRuns,
            completedRuns = completed, failedRuns = failed, runningRuns = running,
            batch.StartedAt, batch.CompletedAt,
            isComplete,
            runs
        });
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var batches = await db.BatchRuns
            .OrderByDescending(b => b.StartedAt)
            .Take(10)
            .Select(b => new { b.Id, b.Name, b.TotalRuns, b.CompletedRuns, b.FailedRuns, b.StartedAt, b.CompletedAt })
            .ToListAsync();
        return Ok(batches);
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value!);
}

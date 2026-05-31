using B2A.DbTula.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Channels;

namespace B2A.DbTula.Api.Controllers;

/// <summary>
/// Called by Jenkins after the nightly CLI batch run.
/// Triggers all profiles via API so results appear in the UI dashboard.
/// Auth: X-Api-Key header (stored as Jenkins credential DBTULA_API_KEY).
/// </summary>
[ApiController]
[Route("api/scheduled")]
public class ScheduledRunController(
    AppDbContext db,
    Channel<Guid> queue,
    IConfiguration config) : ControllerBase
{
    [HttpPost("trigger-all")]
    public async Task<IActionResult> TriggerAll()
    {
        // Validate API key
        var expectedKey = config["Auth:ScheduledApiKey"];
        if (string.IsNullOrEmpty(expectedKey))
            return StatusCode(503, "Scheduled trigger not configured");

        var providedKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (providedKey != expectedKey)
            return Unauthorized("Invalid API key");

        // Find or create a service user for scheduled runs
        var serviceUser = await db.Users.FirstOrDefaultAsync(u => u.Email == "scheduler@dbtula.internal");
        if (serviceUser is null)
        {
            serviceUser = new AppUser
            {
                Email = "scheduler@dbtula.internal",
                GoogleId = "scheduler-internal",
                Name = "Scheduled Run",
                Role = UserRole.Operator
            };
            db.Users.Add(serviceUser);
            await db.SaveChangesAsync();
        }

        var profiles = await db.Profiles
            .Include(p => p.SourceDb)
            .Include(p => p.TargetDb)
            .ToListAsync();

        var runIds = new List<Guid>();
        foreach (var profile in profiles)
        {
            var run = new ComparisonRun
            {
                ProfileId = profile.Id,
                SourceDbId = profile.SourceDbId,
                TargetDbId = profile.TargetDbId,
                InitiatedById = serviceUser.Id
            };
            db.ComparisonRuns.Add(run);
            await db.SaveChangesAsync();
            await queue.Writer.WriteAsync(run.Id);
            runIds.Add(run.Id);
        }

        return Ok(new
        {
            triggered = runIds.Count,
            runIds,
            message = $"Queued {runIds.Count} comparison runs"
        });
    }
}

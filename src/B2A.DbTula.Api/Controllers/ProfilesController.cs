using B2A.DbTula.Api.Data;
using B2A.DbTula.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.Channels;

namespace B2A.DbTula.Api.Controllers;

[ApiController]
[Route("api/profiles")]
[Authorize]
public class ProfilesController(AppDbContext db, Channel<Guid> queue) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var profiles = await db.Profiles
            .Include(p => p.SourceDb)
            .Include(p => p.TargetDb)
            .Include(p => p.Runs.OrderByDescending(r => r.StartedAt).Take(1))
            .OrderBy(p => p.Name)
            .ToListAsync();

        var dtos = profiles.Select(p =>
        {
            var last = p.Runs.FirstOrDefault();
            return new ProfileDto(
                p.Id, p.Name, p.Description,
                p.SourceDbId, p.SourceDb.Name,
                p.TargetDbId, p.TargetDb.Name,
                p.IgnoreOwnership, p.CronExpression, p.CreatedAt,
                last?.Id, last?.Status.ToString(), last?.StartedAt, last?.SummaryJson);
        });

        return Ok(dtos);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateProfileRequest req)
    {
        var userId = GetUserId();
        var profile = new ComparisonProfile
        {
            Name = req.Name,
            Description = req.Description,
            SourceDbId = req.SourceDbId,
            TargetDbId = req.TargetDbId,
            IgnoreOwnership = req.IgnoreOwnership,
            CronExpression = req.CronExpression,
            CreatedById = userId
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        return Created($"/api/profiles/{profile.Id}", profile.Id);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateProfileRequest req)
    {
        var p = await db.Profiles.FindAsync(id);
        if (p is null) return NotFound();
        p.Name = req.Name;
        p.Description = req.Description;
        p.SourceDbId = req.SourceDbId;
        p.TargetDbId = req.TargetDbId;
        p.IgnoreOwnership = req.IgnoreOwnership;
        p.CronExpression = req.CronExpression;
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var p = await db.Profiles.FindAsync(id);
        if (p is null) return NotFound();
        db.Profiles.Remove(p);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/run")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Run(Guid id)
    {
        var profile = await db.Profiles.FindAsync(id);
        if (profile is null) return NotFound();

        var userId = GetUserId();
        var run = new ComparisonRun
        {
            ProfileId = profile.Id,
            SourceDbId = profile.SourceDbId,
            TargetDbId = profile.TargetDbId,
            InitiatedById = userId
        };
        db.ComparisonRuns.Add(run);
        await db.SaveChangesAsync();
        await queue.Writer.WriteAsync(run.Id);

        return Accepted(new { runId = run.Id });
    }

    [HttpGet("{id:guid}/pending-sync")]
    public async Task<IActionResult> PendingSync(Guid id)
    {
        var profile = await db.Profiles.Include(p => p.TargetDb).FirstOrDefaultAsync(p => p.Id == id);
        if (profile is null) return NotFound();

        // All unapplied safe statements across completed runs for this profile
        // Deduplicated: for each objectName keep only the latest run's statement
        var statements = await db.SyncStatements
            .Where(s => s.Category == "Safe" && !s.IsApplied
                && db.ComparisonRuns.Any(r => r.Id == s.ComparisonRunId
                    && r.ProfileId == id && r.Status == RunStatus.Completed))
            .Join(db.ComparisonRuns, s => s.ComparisonRunId, r => r.Id,
                (s, r) => new { s, r.StartedAt })
            .OrderByDescending(x => x.StartedAt)
            .Select(x => x.s)
            .ToListAsync();

        // Deduplicate by objectName: keep latest
        var deduped = statements
            .GroupBy(s => $"{s.ObjectType}:{s.ObjectName}")
            .Select(g => g.First())
            .ToList();

        return Ok(new { profileId = id, pendingCount = deduped.Count, statements = deduped });
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value!);
}

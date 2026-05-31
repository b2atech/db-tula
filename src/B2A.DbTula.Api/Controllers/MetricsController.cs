using B2A.DbTula.Api.Data;
using B2A.DbTula.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace B2A.DbTula.Api.Controllers;

[ApiController]
[Route("api/metrics")]
[Authorize]
public class MetricsController(AppDbContext db) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var runs = await db.ComparisonRuns.Where(r => r.StartedAt >= cutoff).ToListAsync();
        var driftRuns = runs.Count(r => r.SummaryJson != null &&
            (System.Text.Json.JsonDocument.Parse(r.SummaryJson).RootElement.GetProperty("mismatch").GetInt32() > 0 ||
             System.Text.Json.JsonDocument.Parse(r.SummaryJson).RootElement.GetProperty("missingInTarget").GetInt32() > 0));
        var applied = await db.SyncApplyLogs.SumAsync(l => l.SuccessCount);
        var dbs = await db.Databases.CountAsync();

        return Ok(new MetricsSummaryDto(runs.Count, driftRuns, applied, dbs));
    }

    [HttpGet("drift-trend")]
    public async Task<IActionResult> DriftTrend([FromQuery] int days = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);

        // Pull raw rows to memory — EF can't translate .Date.ToString() + GroupBy to Postgres SQL
        var raw = await db.DriftMetrics
            .Where(m => m.RunDate >= cutoff)
            .Select(m => new { m.RunDate, m.MismatchCount, m.MissingInTargetCount })
            .ToListAsync();

        var metrics = raw
            .GroupBy(m => m.RunDate.Date)
            .Select(g => new DriftTrendPoint(
                g.Key.ToString("yyyy-MM-dd"),
                g.Sum(m => m.MismatchCount),
                g.Sum(m => m.MissingInTargetCount)))
            .OrderBy(p => p.Date)
            .ToList();

        return Ok(metrics);
    }

    [HttpGet("drift-trend-by-profile")]
    public async Task<IActionResult> DriftTrendByProfile([FromQuery] int days = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);

        var raw = await db.DriftMetrics
            .Where(m => m.RunDate >= cutoff)
            .Join(db.ComparisonRuns, m => m.ComparisonRunId, r => r.Id,
                (m, r) => new { m.RunDate, m.MismatchCount, m.MissingInTargetCount, r.ProfileId })
            .Join(db.Profiles, x => x.ProfileId, p => p.Id,
                (x, p) => new { x.RunDate, x.MismatchCount, x.MissingInTargetCount, ProfileName = p.Name })
            .ToListAsync();

        // Group by profile → { profileName, points: [{date, total}] }
        var result = raw
            .GroupBy(x => x.ProfileName)
            .Select(g => new
            {
                profile = g.Key,
                points = g
                    .GroupBy(x => x.RunDate.Date)
                    .Select(d => new
                    {
                        date = d.Key.ToString("yyyy-MM-dd"),
                        drift = d.Sum(x => x.MismatchCount + x.MissingInTargetCount)
                    })
                    .OrderBy(p => p.date)
                    .ToList()
            })
            .OrderBy(x => x.profile)
            .ToList();

        return Ok(result);
    }

    [HttpGet("db-health")]
    public async Task<IActionResult> DbHealth()
    {
        var profiles = await db.Profiles
            .Include(p => p.SourceDb)
            .Include(p => p.TargetDb)
            .ToListAsync();

        var results = new List<DbHealthDto>();
        foreach (var p in profiles)
        {
            var last = await db.ComparisonRuns
                .Where(r => r.ProfileId == p.Id && r.Status == RunStatus.Completed)
                .OrderByDescending(r => r.CompletedAt)
                .FirstOrDefaultAsync();

            string status = "Unknown";
            int totalDrift = 0;

            if (last?.SummaryJson != null)
            {
                var doc = System.Text.Json.JsonDocument.Parse(last.SummaryJson).RootElement;
                var mismatch = doc.GetProperty("mismatch").GetInt32();
                var missing = doc.GetProperty("missingInTarget").GetInt32();
                var missingSource = doc.GetProperty("missingInSource").GetInt32();
                totalDrift = mismatch + missing + missingSource;
                status = totalDrift == 0 ? "Healthy" : "Drift";
            }

            results.Add(new DbHealthDto(
                p.Id, p.Name, p.SourceDb.Name, p.TargetDb.Name,
                status, totalDrift, last?.CompletedAt, last?.Id));
        }

        return Ok(results);
    }
}

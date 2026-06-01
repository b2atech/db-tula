using System.Text;
using System.Threading.Channels;
using B2A.DbTula.Api.Data;
using B2A.DbTula.Api.Models;
using B2A.DbTula.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Security.Claims;

namespace B2A.DbTula.Api.Controllers;

[ApiController]
[Route("api/comparisons")]
[Authorize]
public class ComparisonsController(
    AppDbContext db,
    Channel<Guid> queue,
    CredentialService creds) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var items = await db.ComparisonRuns
            .Include(r => r.SourceDb)
            .Include(r => r.TargetDb)
            .Include(r => r.Profile)
            .OrderByDescending(r => r.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ComparisonRunDto(
                r.Id, r.ProfileId, r.Profile != null ? r.Profile.Name : null,
                r.SourceDbId, r.SourceDb.Name,
                r.TargetDbId, r.TargetDb.Name,
                r.Status, r.StartedAt, r.CompletedAt,
                r.SummaryJson, r.ErrorMessage))
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> StartRun([FromBody] StartComparisonRequest req)
    {
        var sourceExists = await db.Databases.AnyAsync(d => d.Id == req.SourceDbId);
        var targetExists = await db.Databases.AnyAsync(d => d.Id == req.TargetDbId);
        if (!sourceExists || !targetExists) return BadRequest("Invalid source or target database ID");

        var userId = GetUserId();
        var run = new ComparisonRun
        {
            SourceDbId = req.SourceDbId,
            TargetDbId = req.TargetDbId,
            InitiatedById = userId
        };
        db.ComparisonRuns.Add(run);
        await db.SaveChangesAsync();

        await queue.Writer.WriteAsync(run.Id);

        return Accepted(new { runId = run.Id });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDetail(Guid id)
    {
        var run = await db.ComparisonRuns
            .Include(r => r.SourceDb)
            .Include(r => r.TargetDb)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (run is null) return NotFound();

        var profileName = run.ProfileId.HasValue
            ? (await db.Profiles.FindAsync(run.ProfileId))?.Name
            : null;

        return Ok(new ComparisonRunDetailDto(
            run.Id, run.ProfileId, profileName,
            run.SourceDbId, run.SourceDb.Name,
            run.TargetDbId, run.TargetDb.Name,
            run.Status, run.StartedAt, run.CompletedAt,
            run.ResultJson, run.SummaryJson,
            !string.IsNullOrEmpty(run.SyncScriptSafe),
            !string.IsNullOrEmpty(run.SyncScriptRisky),
            !string.IsNullOrEmpty(run.SyncScriptDestructive),
            run.ErrorMessage));
    }

    [HttpGet("{id:guid}/sync-script")]
    public async Task<IActionResult> DownloadSyncScript(Guid id, [FromQuery] string category = "safe")
    {
        var run = await db.ComparisonRuns.FindAsync(id);
        if (run is null) return NotFound();

        var sql = category.ToLower() switch
        {
            "safe" => run.SyncScriptSafe,
            "risky" => run.SyncScriptRisky,
            "destructive" => run.SyncScriptDestructive,
            _ => null
        };

        if (string.IsNullOrEmpty(sql)) return NotFound("No script available for this category");

        var bytes = Encoding.UTF8.GetBytes(sql);
        return File(bytes, "text/plain", $"sync-{category}-{id:N}.sql");
    }

    [HttpPost("{id:guid}/apply-safe")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ApplySafe(Guid id)
    {
        var run = await db.ComparisonRuns
            .Include(r => r.TargetDb)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (run is null) return NotFound();
        if (run.Status != RunStatus.Completed) return BadRequest("Run must be completed before applying changes");
        if (string.IsNullOrEmpty(run.SyncScriptSafe)) return BadRequest("No safe script available");

        // Find write account for the target database
        var writeAccount = await db.Databases
            .FirstOrDefaultAsync(d => d.ReadAccountId == run.TargetDbId && d.IsWriteAccount);
        if (writeAccount is null)
            return BadRequest("No write account configured for the target database. Register a write account and link it to the target read account.");

        var cs = creds.Decrypt(writeAccount.ConnectionStringEncrypted);
        var stmts = ParseStatements(run.SyncScriptSafe);

        var successes = 0;
        var failures = 0;
        var errors = new List<string>();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        foreach (var stmt in stmts)
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = stmt;
                await cmd.ExecuteNonQueryAsync();
                successes++;
            }
            catch (Exception ex)
            {
                failures++;
                errors.Add($"[FAILED] {stmt.Split('\n')[0].Trim()}: {ex.Message}");
            }
        }

        var log = new SyncApplyLog
        {
            ComparisonRunId = run.Id,
            AppliedById = GetUserId(),
            TargetDbId = run.TargetDbId,
            SqlExecuted = run.SyncScriptSafe,
            SuccessCount = successes,
            FailureCount = failures,
            ErrorDetails = errors.Any() ? string.Join("\n", errors) : null
        };
        db.SyncApplyLogs.Add(log);
        await db.SaveChangesAsync();

        return Ok(new ApplySafeResult(successes, failures, errors));
    }

    [HttpPost("{id:guid}/retry")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Retry(Guid id)
    {
        var original = await db.ComparisonRuns.FindAsync(id);
        if (original is null) return NotFound();
        if (original.Status == RunStatus.Running || original.Status == RunStatus.Pending)
            return BadRequest("Run is still in progress");

        var retry = new ComparisonRun
        {
            ProfileId = original.ProfileId,
            SourceDbId = original.SourceDbId,
            TargetDbId = original.TargetDbId,
            InitiatedById = GetUserId()
        };
        db.ComparisonRuns.Add(retry);
        await db.SaveChangesAsync();
        await queue.Writer.WriteAsync(retry.Id);

        return Accepted(new { runId = retry.Id });
    }

    [HttpGet("{id:guid}/statements")]
    public async Task<IActionResult> GetStatements(Guid id, [FromQuery] string? category = null)
    {
        var q = db.SyncStatements.Where(s => s.ComparisonRunId == id);
        if (!string.IsNullOrEmpty(category))
            q = q.Where(s => s.Category == category);

        var stmts = await q.OrderBy(s => s.Category).ThenBy(s => s.OrderIndex)
            .Select(s => new SyncStatementDto(
                s.Id, s.Category, s.ObjectType, s.ObjectName,
                s.Sql, s.Comment, s.OrderIndex, s.IsApproved, s.IsApplied, s.AppliedAt))
            .ToListAsync();

        return Ok(stmts);
    }

    [HttpPatch("{id:guid}/statements/{sid:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ToggleStatement(Guid id, Guid sid, [FromBody] ToggleStatementRequest req)
    {
        var stmt = await db.SyncStatements.FirstOrDefaultAsync(s => s.Id == sid && s.ComparisonRunId == id);
        if (stmt is null) return NotFound();
        stmt.IsApproved = req.IsApproved;
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{id:guid}/apply-approved")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ApplyApproved(Guid id)
    {
        var run = await db.ComparisonRuns.Include(r => r.TargetDb).FirstOrDefaultAsync(r => r.Id == id);
        if (run is null) return NotFound();
        if (run.Status != RunStatus.Completed) return BadRequest("Run must be completed");

        var writeAccount = await db.Databases
            .FirstOrDefaultAsync(d => d.ReadAccountId == run.TargetDbId && d.IsWriteAccount);
        if (writeAccount is null)
            return BadRequest("No write account configured for target database");

        var approvedStmts = await db.SyncStatements
            .Where(s => s.ComparisonRunId == id && s.Category == "Safe" && s.IsApproved && !s.IsApplied)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();

        if (!approvedStmts.Any()) return BadRequest("No approved statements to apply");

        var cs = creds.Decrypt(writeAccount.ConnectionStringEncrypted);
        var userId = GetUserId();
        var successes = 0; var failures = 0;
        var errors = new List<string>();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        foreach (var stmt in approvedStmts)
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = stmt.Sql;
                await cmd.ExecuteNonQueryAsync();
                stmt.IsApplied = true;
                stmt.AppliedAt = DateTime.UtcNow;
                stmt.AppliedById = userId;
                successes++;
            }
            catch (Exception ex)
            {
                failures++;
                errors.Add($"[{stmt.ObjectType}: {stmt.ObjectName}] {ex.Message}");
            }
        }

        var log = new SyncApplyLog
        {
            ComparisonRunId = run.Id,
            AppliedById = userId,
            TargetDbId = run.TargetDbId,
            SqlExecuted = string.Join("\n\n", approvedStmts.Select(s => s.Sql)),
            SuccessCount = successes,
            FailureCount = failures,
            ErrorDetails = errors.Any() ? string.Join("\n", errors) : null
        };
        db.SyncApplyLogs.Add(log);
        await db.SaveChangesAsync();

        return Ok(new ApplySafeResult(successes, failures, errors));
    }

    private static List<string> ParseStatements(string sql)
    {
        // Split on semicolons, skip comment-only lines
        var statements = new List<string>();
        var current = new StringBuilder();

        foreach (var line in sql.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (trimmed.StartsWith("--")) { current.AppendLine(trimmed); continue; }

            current.AppendLine(trimmed);
            if (trimmed.TrimEnd().EndsWith(';'))
            {
                var s = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(s))
                    statements.Add(s);
                current.Clear();
            }
        }

        return statements;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value!);
}

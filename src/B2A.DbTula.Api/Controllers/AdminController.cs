using B2A.DbTula.Api.Data;
using B2A.DbTula.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace B2A.DbTula.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(AppDbContext db) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        var users = await db.Users
            .OrderBy(u => u.Email)
            .Select(u => new UserDto(u.Id, u.Email, u.Name, u.Role))
            .ToListAsync();
        return Ok(users);
    }

    [HttpPut("users/{id:guid}/role")]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateRoleRequest req)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        user.Role = req.Role;
        await db.SaveChangesAsync();
        return Ok(new UserDto(user.Id, user.Email, user.Name, user.Role));
    }

    [HttpGet("allowed-emails")]
    public async Task<IActionResult> ListAllowedEmails()
    {
        var emails = await db.AllowedEmails
            .Include(e => e.AddedBy)
            .OrderBy(e => e.Email)
            .Select(e => new { e.Id, e.Email, AddedBy = e.AddedBy.Name, e.AddedAt })
            .ToListAsync();
        return Ok(emails);
    }

    [HttpPost("allowed-emails")]
    public async Task<IActionResult> AddAllowedEmail([FromBody] string email)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value!);

        if (await db.AllowedEmails.AnyAsync(e => e.Email.ToLower() == email.ToLower()))
            return Conflict("Email already in allowlist");

        db.AllowedEmails.Add(new AllowedEmail { Email = email.ToLower().Trim(), AddedById = userId });
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("allowed-emails/{id:guid}")]
    public async Task<IActionResult> RemoveAllowedEmail(Guid id)
    {
        var entry = await db.AllowedEmails.FindAsync(id);
        if (entry is null) return NotFound();
        db.AllowedEmails.Remove(entry);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("audit-log")]
    public async Task<IActionResult> AuditLog([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var logs = await db.SyncApplyLogs
            .Include(l => l.AppliedBy)
            .Include(l => l.TargetDb)
            .OrderByDescending(l => l.AppliedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new AuditLogDto(
                l.Id, l.ComparisonRunId,
                l.AppliedBy.Name, l.AppliedAt,
                l.TargetDb.Name,
                l.SuccessCount, l.FailureCount, l.ErrorDetails))
            .ToListAsync();
        return Ok(logs);
    }
}

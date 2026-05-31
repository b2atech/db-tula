using B2A.DbTula.Api.Data;
using B2A.DbTula.Api.Models;
using B2A.DbTula.Api.Services;
using B2A.DbTula.Cli.Factories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace B2A.DbTula.Api.Controllers;

[ApiController]
[Route("api/databases")]
[Authorize]
public class DatabasesController(AppDbContext db, CredentialService creds) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var items = await db.Databases
            .OrderBy(d => d.Name)
            .Select(d => new DatabaseDto(d.Id, d.Name, d.DbType, d.Environment, d.IsWriteAccount, d.ReadAccountId, d.CreatedAt))
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] RegisterDatabaseRequest req)
    {
        var userId = GetUserId();
        var entry = new RegisteredDatabase
        {
            Name = req.Name,
            DbType = req.DbType,
            Environment = req.Environment,
            ConnectionStringEncrypted = creds.Encrypt(req.ConnectionString),
            IsWriteAccount = req.IsWriteAccount,
            ReadAccountId = req.ReadAccountId,
            CreatedById = userId
        };
        db.Databases.Add(entry);
        await db.SaveChangesAsync();
        return Created($"/api/databases/{entry.Id}",
            new DatabaseDto(entry.Id, entry.Name, entry.DbType, entry.Environment, entry.IsWriteAccount, entry.ReadAccountId, entry.CreatedAt));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] RegisterDatabaseRequest req)
    {
        var entry = await db.Databases.FindAsync(id);
        if (entry is null) return NotFound();

        entry.Name = req.Name;
        entry.DbType = req.DbType;
        entry.Environment = req.Environment;
        entry.IsWriteAccount = req.IsWriteAccount;
        entry.ReadAccountId = req.ReadAccountId;
        if (!string.IsNullOrWhiteSpace(req.ConnectionString))
            entry.ConnectionStringEncrypted = creds.Encrypt(req.ConnectionString);

        await db.SaveChangesAsync();
        return Ok(new DatabaseDto(entry.Id, entry.Name, entry.DbType, entry.Environment, entry.IsWriteAccount, entry.ReadAccountId, entry.CreatedAt));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var entry = await db.Databases.FindAsync(id);
        if (entry is null) return NotFound();
        db.Databases.Remove(entry);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/test")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> TestConnection(Guid id)
    {
        var entry = await db.Databases.FindAsync(id);
        if (entry is null) return NotFound();

        try
        {
            var cs = creds.Decrypt(entry.ConnectionStringEncrypted);
            var provider = SchemaProviderFactory.Create(Map(entry.DbType), cs, (_, _, _, _) => { });
            // Call a lightweight operation to test connectivity
            var tables = await provider.GetTablesAsync();
            return Ok(new { success = true, tableCount = tables.Count });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, error = ex.Message });
        }
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value!);

    private static Cli.DbType Map(DbKind kind) => kind switch
    {
        DbKind.Postgres => Cli.DbType.Postgres,
        DbKind.MySql => Cli.DbType.MySql,
        _ => throw new NotSupportedException()
    };
}

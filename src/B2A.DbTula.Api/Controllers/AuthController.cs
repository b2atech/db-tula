using B2A.DbTula.Api.Data;
using B2A.DbTula.Api.Models;
using B2A.DbTula.Api.Services;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace B2A.DbTula.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService authService, IConfiguration config, AppDbContext db) : ControllerBase
{
    [HttpPost("google")]
    public async Task<IActionResult> GoogleSignIn([FromBody] GoogleAuthRequest req)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(req.IdToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [config["Google:ClientId"]!]
                });
        }
        catch
        {
            return Unauthorized("Invalid Google token");
        }

        // Whitelist check: DB table first, fallback to config if table is empty
        var anyInDb = await db.AllowedEmails.AnyAsync();
        if (anyInDb)
        {
            var isAllowed = await db.AllowedEmails
                .AnyAsync(e => e.Email.ToLower() == payload.Email.ToLower());
            if (!isAllowed) return Forbid();
        }
        else
        {
            // Fallback to config (used when DB table is empty — prevents full lockout)
            var configList = config.GetSection("Auth:AllowedEmails").Get<string[]>() ?? [];
            if (configList.Length > 0 &&
                !configList.Contains(payload.Email, StringComparer.OrdinalIgnoreCase))
                return Forbid();
        }

        var (user, _) = await authService.FindOrCreateUserAsync(payload);
        var jwt = authService.IssueJwt(user);

        // Return token in body — frontend stores in sessionStorage and sends as Bearer header
        return Ok(new { token = jwt, user = new UserDto(user.Id, user.Email, user.Name, user.Role) });
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("dbtula_token");
        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var id = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value!);
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value!;
        var name = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "";
        var role = Enum.Parse<Data.UserRole>(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "Viewer");
        return Ok(new UserDto(id, email, name, role));
    }
}

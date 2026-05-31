using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using B2A.DbTula.Api.Data;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace B2A.DbTula.Api.Services;

public class AuthService(AppDbContext db, IConfiguration config)
{
    public async Task<(AppUser user, bool isNew)> FindOrCreateUserAsync(GoogleJsonWebSignature.Payload payload)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleId == payload.Subject);
        if (user is not null)
            return (user, false);

        user = new AppUser
        {
            GoogleId = payload.Subject,
            Email = payload.Email,
            Name = payload.Name ?? payload.Email,
            // First user to sign in becomes Admin automatically
            Role = await db.Users.AnyAsync() ? UserRole.Viewer : UserRole.Admin
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user, true);
    }

    public string IssueJwt(AppUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

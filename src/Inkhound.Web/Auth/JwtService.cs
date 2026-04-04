using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Inkhound.Web.Auth;

public class JwtService(IConfiguration config)
{
    private string Secret  => config["Auth:JwtSecret"] ?? throw new InvalidOperationException("JWT secret not configured.");
    private int    Minutes => int.TryParse(config["Auth:JwtExpiryMinutes"], out var m) ? m : 480;

    public (string Token, DateTime ExpiresAt) Generate(UserRecord user)
    {
        var key       = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var creds     = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(Minutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,  user.Id),
            new Claim(JwtRegisteredClaimNames.Name, user.Login),
            new Claim(ClaimTypes.Role,              user.Role),
            new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer:             "Inkhound",
            audience:           "Inkhound",
            claims:             claims,
            expires:            expiresAt,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

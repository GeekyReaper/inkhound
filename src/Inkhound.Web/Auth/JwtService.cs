using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Inkhound.Web.Auth;

public class JwtService(SymmetricSecurityKey signingKey, IConfiguration config)
{
    private int Minutes => int.TryParse(config["Auth:JwtExpiryMinutes"], out var m) ? m : 480;

    public (string Token, DateTime ExpiresAt) Generate(UserRecord user)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(Minutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer             = "Inkhound",
            Audience           = "Inkhound",
            Expires            = expiresAt,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
            Subject            = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub,  user.Id),
                new Claim(JwtRegisteredClaimNames.Name, user.Login),
                new Claim("role",                        user.Role),
                new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString())
            })
        };
        var handler = new JwtSecurityTokenHandler();
        return (handler.WriteToken(handler.CreateToken(descriptor)), expiresAt);
    }
}

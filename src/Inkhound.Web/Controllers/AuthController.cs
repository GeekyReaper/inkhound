using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Inkhound.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IUserStore users, JwtService jwt) : ControllerBase
{
    public record LoginRequest(string Login, string Password);
    public record LoginResponse(string Token, DateTime ExpiresAt, string Role);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await users.ValidateAsync(req.Login, req.Password);
        if (user is null)
            return Unauthorized(new { message = "Invalid credentials." });

        var (token, expiresAt) = jwt.Generate(user);
        return Ok(new LoginResponse(token, expiresAt, user.Role));
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me() => Ok(new
    {
        Id    = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        Login = User.Identity?.Name,
        Role  = User.FindFirst(ClaimTypes.Role)?.Value
    });
}

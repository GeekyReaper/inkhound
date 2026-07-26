using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Inkhound.Core;
using Inkhound.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/auth")]

public class AuthController(InkhoundManager manager, JwtService jwt) : ControllerBase
{
    public record LoginRequest(string Login, string Password);
    public record LoginResponse(string Token, DateTime ExpiresAt, string Role);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await manager.ValidateUserAsync(req.Login, req.Password);
        if (user is null)
            return Unauthorized(new { message = "Invalid credentials." });

        var (token, expiresAt) = jwt.Generate(user);
        return Ok(new LoginResponse(token, expiresAt, "admin"));
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me() => Ok(new
    {
        Id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
        // Le claim JWT "name" n'est pas remappé vers ClaimTypes.Name par le handler JWT par défaut
        // (contrairement à "sub" → NameIdentifier et "role" → Role) — on le lit donc littéralement
        // en priorité ; User.Identity?.Name reste le fallback pour les schemes ApiKey/OpenAccess qui,
        // eux, posent directement ClaimTypes.Name.
        Login = User.FindFirst(JwtRegisteredClaimNames.Name)?.Value ?? User.Identity?.Name,
        Role = User.FindFirst(ClaimTypes.Role)?.Value
    });
}

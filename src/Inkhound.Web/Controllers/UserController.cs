using Inkhound.Core;
using Inkhound.Core.Models;
using Inkhound.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

// MaxAgeRating : plafond de classification d'âge (Guest). Création : absent/vide = aucune limite.
// Mise à jour : absent (null) = inchangé, chaîne vide = retire la limite.
public record CreateUserRequest(string Login, string Password, string Role = UserRoles.Guest, string? MaxAgeRating = null);
public record UpdateUserRequest(string? Login, string? Password, string? Role = null, string? MaxAgeRating = null);

[ApiController]
[Route("api/users")]
[Authorize(Roles = UserRoles.Admin)]
public class UserController(InkhoundManager manager) : ControllerBase
{
    private record UserDto(Guid Id, string Login, string Role, string? MaxAgeRating, DateTime CreatedAt);
    private static UserDto ToDto(User u) => new(u.Id, u.Login, u.Role, u.MaxAgeRating?.ToString(), u.CreatedAt);

    private static bool TryParseMaxAgeRating(string? value, out AgeRating? rating)
    {
        rating = null;
        if (string.IsNullOrEmpty(value)) return true;
        if (!Enum.TryParse<AgeRating>(value, out var parsed)) return false;
        rating = parsed;
        return true;
    }

    // GET /api/users
    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok((await manager.GetUsersAsync()).Select(ToDto));

    // GET /api/users/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await manager.GetUserByIdAsync(id);
        return user is null ? NotFound() : Ok(ToDto(user));
    }

    // POST /api/users
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        if (!TryParseMaxAgeRating(request.MaxAgeRating, out var max))
            return BadRequest(new { message = $"Invalid AgeRating value: {request.MaxAgeRating}" });
        var created = await manager.CreateUserAsync(request.Login, request.Password, request.Role, max);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    // PUT /api/users/{id}
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request)
    {
        if (!TryParseMaxAgeRating(request.MaxAgeRating, out var max))
            return BadRequest(new { message = $"Invalid AgeRating value: {request.MaxAgeRating}" });
        var clear = request.MaxAgeRating is { Length: 0 };
        return Ok(ToDto(await manager.UpdateUserAsync(
            id, request.Login, request.Password, request.Role, max, clear)));
    }

    // DELETE /api/users/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await manager.DeleteUserAsync(id);
        return NoContent();
    }
}

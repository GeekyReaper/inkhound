using Inkhound.Core;
using Inkhound.Core.Models;
using Inkhound.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

public record CreateUserRequest(string Login, string Password, string Role = UserRoles.Guest);
public record UpdateUserRequest(string? Login, string? Password, string? Role = null);

[ApiController]
[Route("api/users")]
[Authorize(Roles = UserRoles.Admin)]
public class UserController(InkhoundManager manager) : ControllerBase
{
    private record UserDto(Guid Id, string Login, string Role, DateTime CreatedAt);
    private static UserDto ToDto(User u) => new(u.Id, u.Login, u.Role, u.CreatedAt);

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
        var created = await manager.CreateUserAsync(request.Login, request.Password, request.Role);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    // PUT /api/users/{id}
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request)
        => Ok(ToDto(await manager.UpdateUserAsync(id, request.Login, request.Password, request.Role)));

    // DELETE /api/users/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await manager.DeleteUserAsync(id);
        return NoContent();
    }
}

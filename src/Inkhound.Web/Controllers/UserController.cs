using Inkhound.Core;
using Inkhound.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

public record CreateUserRequest(string Login, string Password);
public record UpdateUserRequest(string? Login, string? Password);

[ApiController]
[Route("api/users")]
[Authorize]
public class UserController(InkhoundManager manager) : ControllerBase
{
    private record UserDto(Guid Id, string Login, DateTime CreatedAt);
    private static UserDto ToDto(User u) => new(u.Id, u.Login, u.CreatedAt);

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
        var created = await manager.CreateUserAsync(request.Login, request.Password);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    // PUT /api/users/{id}
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request)
        => Ok(ToDto(await manager.UpdateUserAsync(id, request.Login, request.Password)));

    // DELETE /api/users/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await manager.DeleteUserAsync(id);
        return NoContent();
    }
}

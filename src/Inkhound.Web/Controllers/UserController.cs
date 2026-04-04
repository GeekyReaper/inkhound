using System.IdentityModel.Tokens.Jwt;
using Inkhound.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UserController(IUserStore users) : ControllerBase
{
    private record UserDto(string Id, string Login, string Role);
    private static UserDto ToDto(UserRecord u) => new(u.Id, u.Login, u.Role);

    // GET /api/users — admin only
    [HttpGet]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> GetAll()
        => Ok((await users.GetAllAsync()).Select(ToDto));

    // GET /api/users/{id} — admin: all / guest: self only
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        if (!User.IsInRole("admin") && CallerId() != id) return Forbid();
        var user = await users.GetByIdAsync(id);
        return user is null ? NotFound() : Ok(ToDto(user));
    }

    // POST /api/users — admin only
    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        try
        {
            var created = await users.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (ArgumentException ex)         { return BadRequest(new { message = ex.Message }); }
    }

    // PUT /api/users/{id} — admin or self (guest cannot change own role)
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserRequest request)
    {
        var isAdmin = User.IsInRole("admin");
        if (!isAdmin && CallerId() != id)    return Forbid();
        if (!isAdmin && request.Role != null) return Forbid();

        try
        {
            return Ok(ToDto(await users.UpdateAsync(id, request)));
        }
        catch (KeyNotFoundException)         { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (ArgumentException ex)         { return BadRequest(new { message = ex.Message }); }
    }

    // DELETE /api/users/{id} — admin only, no self-delete
    [HttpDelete("{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(string id)
    {
        if (CallerId() == id)
            return BadRequest(new { message = "Cannot delete your own account." });
        try   { await users.DeleteAsync(id); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    private string? CallerId() =>
        User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
}

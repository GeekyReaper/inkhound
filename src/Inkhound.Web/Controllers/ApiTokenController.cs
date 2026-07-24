using Inkhound.Core;
using Inkhound.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

public record CreateApiTokenRequest(string Name, int? ExpiresInDays);

[ApiController]
[Route("api/apitokens")]
[Authorize(Roles = "admin")]
public class ApiTokenController(InkhoundManager manager) : ControllerBase
{
    private record ApiTokenDto(Guid Id, string Name, string Prefix, DateTime CreatedAt, DateTime? ExpiresAt, DateTime? LastUsedAt);
    private record ApiTokenCreatedDto(Guid Id, string Name, string Prefix, DateTime CreatedAt, DateTime? ExpiresAt, string Token);

    private static ApiTokenDto ToDto(ApiToken t) => new(t.Id, t.Name, t.Prefix, t.CreatedAt, t.ExpiresAt, t.LastUsedAt);

    // GET /api/apitokens
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tokens = await manager.GetApiTokensAsync();
        return Ok(tokens.Select(ToDto));
    }

    // POST /api/apitokens
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApiTokenRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Name is required." });

        var (token, raw) = await manager.CreateApiTokenAsync(req.Name, req.ExpiresInDays);
        return CreatedAtAction(nameof(GetAll),
            new ApiTokenCreatedDto(token.Id, token.Name, token.Prefix, token.CreatedAt, token.ExpiresAt, raw));
    }

    // DELETE /api/apitokens/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await manager.DeleteApiTokenAsync(id);
        return NoContent();
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Inkhound.Core;
using Inkhound.Core.Models;
using Inkhound.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

/// <summary>
/// Demandes d'ajout de volume. Un Guest dépose une demande et ne voit que les siennes ; l'admin voit
/// tout, ajuste la library cible / la classification d'âge, puis approuve ou refuse.
/// </summary>
[ApiController]
[Route("api/volume-requests")]
[Authorize]
public class VolumeRequestController(InkhoundManager manager) : ControllerBase
{
    public record CreateVolumeRequestRequest(
        Guid LibraryId, string Source, string SourceId, string Title, int? Year, string? Image, string? AgeRating);

    public record UpdateVolumeRequestRequest(Guid? LibraryId, string? AgeRating);

    private record VolumeRequestDto(
        Guid Id, Guid RequestedByUserId, string RequestedByLogin, string Source, string SourceId,
        string Title, int? Year, string? Image, Guid LibraryId, string AgeRating, string Status,
        DateTime CreatedAt, DateTime UpdatedAt, DateTime? ResolvedAt, Guid? VolumeId, Guid? JobId = null);

    private static VolumeRequestDto ToDto(VolumeRequest r, Guid? jobId = null) => new(
        r.Id, r.RequestedByUserId, r.RequestedByLogin, r.Source, r.SourceId, r.Title, r.Year, r.Image,
        r.LibraryId, r.AgeRating.ToString(), r.Status.ToString(),
        // SQLite relit les dates en Kind Unspecified : on force UTC pour que le client les interprète bien.
        DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc), DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc),
        r.ResolvedAt is null ? null : DateTime.SpecifyKind(r.ResolvedAt.Value, DateTimeKind.Utc), r.VolumeId, jobId);

    private bool IsAdmin => User.IsInRole(UserRoles.Admin);

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    // Périmètre de lecture/annulation : null pour un admin, l'id du compte pour un Guest.
    private Guid? ScopeUserId => IsAdmin ? null : CurrentUserId ?? Guid.Empty;

    private static bool TryParseAgeRating(string? value, out AgeRating rating)
    {
        rating = AgeRating.Unknown;
        return string.IsNullOrEmpty(value) || Enum.TryParse(value, out rating);
    }

    // POST /api/volume-requests — Guest : demande en attente. Admin : demande auto-approuvée, le volume
    // est créé tout de suite et `jobId` permet de suivre le peuplement de ses issues.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVolumeRequestRequest request)
    {
        if (CurrentUserId is not { } userId) return Forbid();
        if (!TryParseAgeRating(request.AgeRating, out var rating))
            return BadRequest(new { message = $"Invalid AgeRating value: {request.AgeRating}" });

        var login = User.FindFirst(JwtRegisteredClaimNames.Name)?.Value ?? User.Identity?.Name ?? string.Empty;
        try
        {
            if (IsAdmin)
            {
                var (approved, jobId) = await manager.CreateApprovedVolumeRequestAsync(
                    userId, login, request.LibraryId, request.Source, request.SourceId,
                    request.Title, request.Year, request.Image, rating);
                return CreatedAtAction(nameof(GetAll), null, ToDto(approved, jobId));
            }

            var created = await manager.CreateVolumeRequestAsync(
                userId, login, request.LibraryId, request.Source, request.SourceId,
                request.Title, request.Year, request.Image, rating);
            return CreatedAtAction(nameof(GetAll), null, ToDto(created));
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    // GET /api/volume-requests?status= — admin : toutes les demandes ; Guest : les siennes uniquement
    // (le périmètre est imposé côté serveur, jamais par un paramètre client).
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status)
    {
        VolumeRequestStatus? parsed = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<VolumeRequestStatus>(status, out var s))
                return BadRequest(new { message = $"Invalid status value: {status}" });
            parsed = s;
        }
        return Ok((await manager.GetVolumeRequestsAsync(ScopeUserId, parsed)).Select(r => ToDto(r)));
    }

    // PATCH /api/volume-requests/{id} — library cible et/ou classification d'âge d'une demande en attente.
    [HttpPatch("{id:guid}")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateVolumeRequestRequest request)
    {
        AgeRating? rating = null;
        if (!string.IsNullOrEmpty(request.AgeRating))
        {
            if (!Enum.TryParse<AgeRating>(request.AgeRating, out var parsed))
                return BadRequest(new { message = $"Invalid AgeRating value: {request.AgeRating}" });
            rating = parsed;
        }
        try
        {
            var updated = await manager.UpdateVolumeRequestAsync(id, request.LibraryId, rating);
            return updated is null ? NotFound() : Ok(ToDto(updated));
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    // POST /api/volume-requests/{id}/approve — crée le volume dans la library cible.
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Approve(Guid id)
    {
        try
        {
            var approved = await manager.ApproveVolumeRequestAsync(id);
            return approved is null ? NotFound() : Ok(ToDto(approved));
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    // POST /api/volume-requests/{id}/reject
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Reject(Guid id)
    {
        try
        {
            var rejected = await manager.RejectVolumeRequestAsync(id);
            return rejected is null ? NotFound() : Ok(ToDto(rejected));
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    // DELETE /api/volume-requests/{id} — admin : n'importe quelle demande ; Guest : annulation de sa
    // propre demande en attente (la demande d'autrui répond 404).
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            return await manager.DeleteVolumeRequestAsync(id, ScopeUserId) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}

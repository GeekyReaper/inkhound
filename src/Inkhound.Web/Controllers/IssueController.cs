using Inkhound.Core;
using Inkhound.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/volumes/{volumeId:guid}/issues")]
[Authorize(Roles = "admin")]
public class IssueController(InkhoundManager manager) : ControllerBase
{
    private record IssueDto(
        Guid Id,
        Guid VolumeId,
        string ComicVineId,
        int IssueNumber,
        string? Title,
        int? Year,
        string? Description,
        IssueStatus Status,
        List<VolumeAuthor> Authors,
        VolumeImage? Image,
        string? CbzFilename,
        DateTime? PublishedAt);

    private static IssueDto ToDto(Issue i)
        => new(i.Id, i.VolumeId, i.ComicVineId, i.IssueNumber, i.Title, i.Year,
               i.Description, i.Status, i.Authors, i.Image, i.CbzFilename, i.PublishedAt);

    // GET /api/volumes/{volumeId}/issues
    [HttpGet]
    public async Task<IActionResult> GetByVolume(Guid volumeId)
    {
        try
        {
            var volume = await manager.GetVolumeAsync(volumeId);
            if (volume is null) return NotFound();

            var issues = await manager.GetIssuesByVolumeAsync(volumeId);
            return Ok(issues.Select(ToDto));
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }
}

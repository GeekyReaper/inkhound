using Inkhound.Core;
using Inkhound.Core.ComicVine;
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

    private record CvIssueDto(
        string Id, string? IssueNumber, string? Name,
        string? CoverDate, string? ThumbUrl, string? SiteDetailUrl);

    private record CvIssuePageDto(
        IEnumerable<CvIssueDto> Items, int PageNumber, int PageSize,
        int TotalItems, int TotalPages, bool HasNext, bool HasPrev);

    // GET /api/issues/{issueId}
    [HttpGet("/api/issues/{issueId:guid}")]
    public async Task<IActionResult> GetById(Guid issueId)
    {
        var issue = await manager.GetIssueAsync(issueId);
        return issue is null ? NotFound() : Ok(ToDto(issue));
    }

    // GET /api/issues/comicvine?comicVineVolumeId=XXX&page=1&pageSize=10
    [HttpGet("/api/issues/comicvine")]
    public async Task<IActionResult> GetByComicVineVolumeId(
        [FromQuery] int comicVineVolumeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        try
        {
            var result = await manager.ComicVineGetIssuesByVolumeAsync(comicVineVolumeId, page, pageSize);
            return Ok(new CvIssuePageDto(
                result.Items.Select(i => new CvIssueDto(
                    i.Id.ToString(), i.IssueNumber, i.Name,
                    i.CoverDate, i.Image?.ThumbUrl, i.SiteDetailUrl)),
                result.PageNumber, result.PageSize, result.TotalItems,
                result.TotalPages, result.HasNext, result.HasPrev));
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    public record UpdateIssueManuallyRequest(string? Title, int? Year, string? Description, IssueStatus Status);

    // PUT /api/issues/{issueId}
    [HttpPut("/api/issues/{issueId:guid}")]
    public async Task<IActionResult> Update(Guid issueId, [FromBody] UpdateIssueManuallyRequest req)
    {
        var updated = await manager.UpdateIssueManuallyAsync(issueId, req.Title, req.Year, req.Description, req.Status);
        return updated ? NoContent() : NotFound();
    }

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

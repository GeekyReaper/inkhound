using Inkhound.Core;
using Inkhound.Core.ComicVine;
using Inkhound.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/libraries/{libraryId:guid}/volumes")]
[Authorize(Roles = "admin")]
public class VolumeController(InkhoundManager manager) : ControllerBase
{
    private record VolumeSearchDto(
        string SourceId, string SourceType, string Title,
        int? Year, int CountOfIssues, string? Description,
        string? Publisher, VolumeImage? Image,
        string? FirstIssueName, string? LastIssueName,
        string? SiteDetailUrl);

    private record VolumeSearchPageDto(IEnumerable<VolumeSearchDto> Items, int PageNumber, int PageSize, int TotalItems, int TotalPages, bool HasNext, bool HasPrev);

    private static VolumeSearchDto ToSearchDto(CvVolumeStub cv)
    {
        VolumeImage? image = cv.Image is { } img
            ? new(img.IconUrl, img.MediumUrl, img.ScreenUrl, img.ScreenLargeUrl,
                  img.SmallUrl, img.SuperUrl, img.ThumbUrl, img.TinyUrl, img.OriginalUrl, img.ImageTags)
            : null;
        int? year = cv.StartYear != null && int.TryParse(cv.StartYear, out var y) ? y : null;
        return new(cv.Id.ToString(), "ComicVine", cv.Name, year, cv.CountOfIssues,
                   cv.Description, cv.Publisher?.Name, image,
                   cv.FirstIssue?.Name, cv.LastIssue?.Name, cv.SiteDetailUrl);
    }

    // GET /api/volumes/search?name=Batman&page=1&pageSize=10
    [HttpGet("/api/volumes/search")]
    public async Task<IActionResult> Search([FromQuery] string? name, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "name is required." });
        try
        {
            var result = await manager.ComicVineSearchVolumeByNameAsync(name, page, pageSize);
            return Ok(new VolumeSearchPageDto(
                result.Items.Select(ToSearchDto),
                result.PageNumber, result.PageSize, result.TotalItems,
                result.TotalPages, result.HasNext, result.HasPrev));
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    private record VolumeDto(
        Guid Id,
        Guid LibraryId,
        string SourceId,
        string SourceType,
        string Title,
        int? Year,
        string? Description,
        string? Publisher,
        VolumeStatus Status,
        List<string> Genres,
        List<VolumeAuthor> Authors,
        VolumeImage? Image,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private static VolumeDto ToDto(Volume v)
        => new(v.Id, v.LibraryId, v.SourceId, v.SourceType, v.Title, v.Year,
               v.Description, v.Publisher, v.Status, v.Genres, v.Authors,
               v.Image, v.CreatedAt, v.UpdatedAt);

    // GET /api/volumes/{volumeId}
    [HttpGet("/api/volumes/{volumeId:guid}")]
    public async Task<IActionResult> GetById(Guid volumeId)
    {
        try
        {
            var volume = await manager.GetVolumeAsync(volumeId);
            return volume is null ? NotFound() : Ok(ToDto(volume));
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    public record ImportFromDirectoryRequest(string ImportDirectory);

    // POST /api/volumes/{volumeId}/import
    [HttpPost("/api/volumes/{volumeId:guid}/import")]
    public IActionResult ImportFromDirectory(Guid volumeId, [FromBody] ImportFromDirectoryRequest request)
    {
        _ = manager.ImportArchiveFromDirectoryAsync(volumeId, request.ImportDirectory);
        return Accepted(new { message = $"Import started for volume {volumeId}." });
    }

    // GET /api/libraries/{libraryId}/volumes
    [HttpGet]
    public async Task<IActionResult> GetByLibrary(Guid libraryId)
    {
        try
        {
            var library = await manager.GetLibraryAsync(libraryId);
            if (library is null) return NotFound();

            var volumes = await manager.GetVolumesByLibraryAsync(libraryId);
            return Ok(volumes.Select(ToDto));
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }
}

using Inkhound.Core;
using Inkhound.Core.Models;
using Inkhound.Core.Sources;
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
        string SourceId,
        int IssueNumber,
        string? Title,
        int? Year,
        string? Description,
        IssueStatus Status,
        List<VolumeAuthor> Authors,
        VolumeImage? Image,
        string? CbzFilename,
        DateTime? PublishedAt,
        int? AnalysisScore,
        string? AnalysisScoreBand,
        string? AnalysisDominantImageFormat,
        int? AnalysisDominantResolutionWidth,
        int? AnalysisDominantResolutionHeight,
        int? AnalysisPageCount,
        bool? AnalysisHasComicInfo,
        double? AnalysisZipCompressionPercent,
        long? AnalysisFileSizeBytes,
        double? AnalysisAveragePageSizeBytes,
        string? AnalysisFileHash,
        DateTime? AnalyzedAt);

    private static IssueDto ToDto(Issue i)
        => new(i.Id, i.VolumeId, i.SourceId, i.IssueNumber, i.Title, i.Year,
               i.Description, i.Status, i.Authors, i.Image, i.CbzFilename, i.PublishedAt,
               i.AnalysisScore, i.AnalysisScoreBand, i.AnalysisDominantImageFormat,
               i.AnalysisDominantResolutionWidth, i.AnalysisDominantResolutionHeight,
               i.AnalysisPageCount, i.AnalysisHasComicInfo, i.AnalysisZipCompressionPercent,
               i.AnalysisFileSizeBytes, i.AnalysisAveragePageSizeBytes,
               i.AnalysisFileHash, i.AnalyzedAt);

    private record SourceIssueDto(
        string SourceId, string Source, string? Name, string IssueNumber,
        DateTime? CoverDate, string? ImageUrl, string? SiteUrl);

    private record SourceIssuePageDto(
        IEnumerable<SourceIssueDto> Items, int PageNumber, int PageSize,
        int TotalItems, int TotalPages, bool HasNext, bool HasPrev);

    // GET /api/issues/{issueId}
    [HttpGet("/api/issues/{issueId:guid}")]
    public async Task<IActionResult> GetById(Guid issueId)
    {
        var issue = await manager.GetIssueAsync(issueId);
        return issue is null ? NotFound() : Ok(ToDto(issue));
    }

    // GET /api/issues/source?source=comicvine&sourceVolumeId=XXX&page=1&pageSize=10
    [HttpGet("/api/issues/source")]
    public async Task<IActionResult> GetBySourceVolumeId(
        [FromQuery] string source,
        [FromQuery] string sourceVolumeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        try
        {
            var result = await manager.GetIssuesBySourceAsync(source, sourceVolumeId, page, pageSize);
            return Ok(new SourceIssuePageDto(
                result.Items.Select(i => new SourceIssueDto(
                    i.SourceId, i.Source, i.Name, i.IssueNumber,
                    i.CoverDate, i.ImageUrl, i.SiteUrl)),
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

    // POST /api/issues/{issueId}/analyze — lance l'analyse CBZ en tant que Job et retourne
    // immédiatement son JobId ; le frontend suit la progression/les traces via SignalR, puis
    // reçoit l'Issue mise à jour via son abonnement existant à ManagerDataUpdated.
    [HttpPost("/api/issues/{issueId:guid}/analyze")]
    public async Task<IActionResult> Analyze(Guid issueId)
    {
        var job = await manager.LaunchJobAnalyzeIssue(new AnalyzeIssueJobParameters { IssueId = issueId });
        return Accepted(new { jobId = job.JobId });
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

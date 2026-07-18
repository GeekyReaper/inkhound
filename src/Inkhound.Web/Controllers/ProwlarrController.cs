using System.Text.Json;
using Inkhound.Core;
using Inkhound.Core.Analysis;
using Inkhound.Core.Models;
using Inkhound.Core.Prowlarr;
using Inkhound.Core.Scoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

public record ProwlarrGrabRequest(string Guid, int IndexerId, Guid IssueId, int? DownloadClientId);
public record ProwlarrSetIndexersRequest(List<ProwlarrSetIndexersRequest.IndexerItem> Indexers)
{
    public record IndexerItem(int Id, string Name, string Protocol, bool Enable,
                              List<int> SelectedCategoryIds);
}

[ApiController]
[Route("api/prowlarr")]
[Authorize(Roles = "admin")]
public class ProwlarrController(InkhoundManager manager) : ControllerBase
{
    private record ProwlarrCategoryDto(int Id, string Name);
    private record ProwlarrIndexerDto(int Id, string Name, string Protocol, bool Enable,
                                      List<ProwlarrCategoryDto> Categories);
    private record SelectedIndexerDto(int IndexerId, string Name, string Protocol,
                                      List<int> SelectedCategoryIds);
    private record SearchResultDto(
        string Title, string? InfoUrl, long Size,
        int Seeders, int Leechers, string Guid, int IndexerId,
        string? Indexer, string? Protocol, string? DownloadUrl, DateTime? PublishDate,
        List<ProwlarrCategoryDto> Categories,
        string TorrentType,
        string TorrentLabel);
    private record ScoreDetailsDto(float TitleMatch, float IssueNumberMatch, float YearMatch,
        float SizePlausibility, float SeederScore, float FormatScore);
    private record ScoredResultDto(SearchResultDto Result, float Score, ScoreDetailsDto Details);
    private record HistoryItemDto(int Id, string EventType, string SourceTitle, int IndexerId);

    private static SearchResultDto ToResultDto(ProwlarrSearchResult r, TorrentAnalysis analysis)
        => new(r.Title, r.InfoUrl, r.Size, r.Seeders, r.Leechers,
               r.Guid, r.IndexerId, r.Indexer, r.Protocol, r.DownloadUrl, r.PublishDate,
               r.Categories?.Select(c => new ProwlarrCategoryDto(c.Id, c.Name)).ToList() ?? [],
               analysis.Type, analysis.Label);

    private static ScoredResultDto ToScoredDto(ScoredSearchResultTorrent s)
        => new(
            ToResultDto(s.Result, s.Analysis),
            s.Score,
            new ScoreDetailsDto(
                s.Details.TitleMatch, s.Details.IssueNumberMatch, s.Details.YearMatch,
                s.Details.SizePlausibility, s.Details.SeederScore, s.Details.FormatScore));

    // GET /api/prowlarr/indexers
    [HttpGet("indexers")]
    public async Task<IActionResult> GetIndexers()
    {
        var indexers = await manager.GetProwlarrIndexersAsync();
        return Ok(indexers.Select(i => new ProwlarrIndexerDto(
            i.Id, i.Name, i.Protocol, i.Enable,
            i.Capabilities?.Categories.Select(c => new ProwlarrCategoryDto(c.Id, c.Name)).ToList() ?? [])));
    }

    // GET /api/prowlarr/selected-indexers
    [HttpGet("selected-indexers")]
    public async Task<IActionResult> GetSelectedIndexers()
    {
        var selected = await manager.GetSelectedIndexersAsync();
        return Ok(selected.Select(s => new SelectedIndexerDto(
            s.IndexerId, s.Name, s.Protocol,
            string.IsNullOrEmpty(s.CategoriesJson)
                ? []
                : JsonSerializer.Deserialize<List<int>>(s.CategoriesJson) ?? [])));
    }

    // PUT /api/prowlarr/selected-indexers
    [HttpPut("selected-indexers")]
    public async Task<IActionResult> SetSelectedIndexers([FromBody] ProwlarrSetIndexersRequest req)
    {
        var items = req.Indexers
            .Select(i => (new ProwlarrIndexer(i.Id, i.Name, i.Protocol, i.Enable, null), i.SelectedCategoryIds))
            .ToList();
        await manager.SetSelectedIndexersAsync(items);
        return NoContent();
    }

    // POST /api/prowlarr/search/{issueId}
    [HttpPost("search/{issueId:guid}")]
    public async Task<IActionResult> SearchForIssue(Guid issueId, [FromQuery] int[]? indexerIds)
    {
        var results = await manager.LaunchJobSearchMissingIssue(
            new ProwlarrSearchJobParameters { IssueId = issueId, IndexerIds = indexerIds });
        return Ok(results.Select(ToScoredDto));
    }

    // POST /api/prowlarr/grab
    [HttpPost("grab")]
    public async Task<IActionResult> Grab([FromBody] ProwlarrGrabRequest req)
    {
        var success = await manager.GrabSearchResultAsync(
            req.Guid, req.IndexerId, req.IssueId, req.DownloadClientId);
        return success ? Ok(new { message = "Grab initiated." }) : StatusCode(502, new { message = "Grab failed." });
    }

    // GET /api/prowlarr/history
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int limit = 20)
    {
        var history = await manager.GetProwlarrHistoryAsync(limit);
        return Ok(history.Select(h => new HistoryItemDto(h.Id, h.EventType, h.SourceTitle, h.IndexerId)));
    }
}

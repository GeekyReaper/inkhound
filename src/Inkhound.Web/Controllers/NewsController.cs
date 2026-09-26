using Inkhound.Core;
using Inkhound.Core.Bedetheque;
using Inkhound.Core.Models;
using Inkhound.Core.News;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

/// <summary>
/// Module News (page <c>/news</c>) : flux top ventes / nouveautés lus en base (historique), détail
/// live d'un album et de sa série, résolution de la série avant ajout, et lancement manuel du job
/// de rafraîchissement. L'ajout à une bibliothèque passe par l'endpoint classique
/// <c>POST /api/libraries/{id}/volumes</c>.
/// </summary>
[ApiController]
[Route("api/news")]
[Authorize(Roles = "admin")]
public class NewsController(InkhoundManager manager) : ControllerBase
{
    private record LibraryLinkDto(Guid LibraryId, Guid VolumeId);

    private record NewsItemDto(
        string Provider, string AlbumId, string SeriesTitle, string? AlbumNumber, string? AlbumTitle,
        string? SeriesId, string? Publisher, DateOnly? ReleaseDate, NewsCategory? Category, string? Description,
        string? CoverUrl, string? CoverLargeUrl, string? AlbumUrl,
        int? Rank, string? Evolution, int? EvolutionDelta, int? WeeksInChart,
        bool Enriched, IEnumerable<NewsAuthor> Authors, LibraryLinkDto? Library);

    private record TopSalesDto(IEnumerable<string> Periods, string? Period, IEnumerable<NewsItemDto> Items);

    private record PageDto<T>(
        IEnumerable<T> Items, int PageNumber, int PageSize, int TotalItems, int TotalPages, bool HasNext, bool HasPrev);

    private record ReleasesDto(IEnumerable<string> Months, PageDto<NewsItemDto> Page);

    private record AppearanceDto(NewsFeed Feed, string Period, int? Rank);

    private record AlbumDetailDto(
        NewsItemDto? Item, NewsAlbumEnrichment Album, NewsSeriesDetail? Series, LibraryLinkDto? Library,
        IEnumerable<AppearanceDto> Appearances);

    private record ResolvedSeriesDto(string SeriesId, LibraryLinkDto? Library);

    /// <summary>Corps de <c>POST /api/news/refresh</c>.</summary>
    public record RefreshNewsRequest(bool Force = true, int? EnrichBatchSize = null);

    private static LibraryLinkDto? ToDto(InkhoundManager.NewsLibraryLink? link)
        => link is null ? null : new LibraryLinkDto(link.LibraryId, link.VolumeId);

    private static NewsItemDto ToDto(InkhoundManager.NewsItem item)
    {
        var a = item.Album;
        return new NewsItemDto(
            a.Provider, a.AlbumId, a.SeriesTitle, a.AlbumNumber, a.AlbumTitle, a.SeriesId, a.Publisher,
            a.ReleaseDate, a.Category, a.ShortDescription ?? item.Enrichment?.Description,
            a.CoverUrl, a.CoverLargeUrl ?? a.CoverUrl, a.AlbumUrl,
            item.Entry?.Rank, item.Entry?.Evolution, item.Entry?.EvolutionDelta, item.Entry?.WeeksInChart,
            a.EnrichedAtUtc is not null, item.Enrichment?.Authors ?? [], ToDto(item.Library));
    }

    // GET /api/news/top-sales?period=yyyy-MM-dd — classement d'une semaine (la plus récente par défaut).
    [HttpGet("top-sales")]
    public async Task<IActionResult> GetTopSales([FromQuery] string? period)
    {
        try
        {
            var result = await manager.GetNewsTopSalesAsync(period);
            return Ok(new TopSalesDto(result.Periods, result.Period, result.Items.Select(ToDto)));
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    // GET /api/news/releases?category=Manga&month=yyyy-MM&page=1&pageSize=24
    [HttpGet("releases")]
    public async Task<IActionResult> GetReleases(
        [FromQuery] NewsCategory? category, [FromQuery] string? month,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 24)
    {
        try
        {
            var result = await manager.GetNewsReleasesAsync(category, month, page, pageSize);
            var p = result.Page;
            return Ok(new ReleasesDto(result.Months, new PageDto<NewsItemDto>(
                p.Items.Select(ToDto), p.PageNumber, p.PageSize, p.TotalItems, p.TotalPages, p.HasNext, p.HasPrev)));
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    // GET /api/news/albums/{albumId} — album (enrichi en direct si besoin), série, planches, statut bibliothèque.
    [HttpGet("albums/{albumId}")]
    public async Task<IActionResult> GetAlbum(string albumId, CancellationToken ct)
    {
        try
        {
            var d = await manager.GetNewsAlbumDetailAsync(albumId, ct);
            var item = d.Album is null ? null : ToDto(new InkhoundManager.NewsItem(d.Album, null, d.Enrichment, d.Library));
            return Ok(new AlbumDetailDto(item, d.Enrichment, d.Series, ToDto(d.Library),
                d.Appearances.Select(e => new AppearanceDto(e.Feed, e.Period, e.Rank))));
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (BedethequeBlockedException ex) { return StatusCode(503, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    // POST /api/news/albums/{albumId}/resolve-series — id de série (enrichissement à la demande) avant « Add ».
    [HttpPost("albums/{albumId}/resolve-series")]
    public async Task<IActionResult> ResolveSeries(string albumId)
    {
        try
        {
            var (seriesId, library) = await manager.ResolveNewsAlbumSeriesAsync(albumId);
            return Ok(new ResolvedSeriesDto(seriesId, ToDto(library)));
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (BedethequeBlockedException ex) { return StatusCode(503, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    // POST /api/news/refresh — lance le job News ; 409 si un rafraîchissement est déjà en cours.
    [HttpPost("refresh")]
    public IActionResult Refresh([FromBody] RefreshNewsRequest? request)
    {
        var parameters = new RefreshNewsJobParameters
        {
            ForceListRefresh = request?.Force ?? true,
            EnrichBatchSize = request?.EnrichBatchSize ?? manager.GetSchedulerStatus().NewsEnrichBatchSize,
        };
        if (!parameters.IsValid(out var errors))
            return BadRequest(new { message = string.Join(" ", errors) });

        try
        {
            var job = manager.LaunchJobRefreshNews(parameters);
            return Accepted(new { jobId = job.JobId });
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}

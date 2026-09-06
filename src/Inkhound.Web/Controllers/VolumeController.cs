using Inkhound.Core;
using Inkhound.Core.Models;
using Inkhound.Core.Sources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/libraries/{libraryId:guid}/volumes")]
[Authorize(Roles = "admin")]
public class VolumeController(InkhoundManager manager) : ControllerBase
{
    private record VolumeSearchDto(
        string SourceId, string Source, string Title,
        int? Year, int CountOfIssues, string? Description,
        string? Publisher, string? ImageUrl, string? SiteUrl, double Score);

    private record VolumeSearchPageDto(IEnumerable<VolumeSearchDto> Items, int PageNumber, int PageSize, int TotalItems, int TotalPages, bool HasNext, bool HasPrev);

    private static VolumeSearchDto ToSearchDto(SourceVolume v) =>
        new(v.SourceId, v.Source, v.Name, v.StartYear, v.CountOfIssues,
            v.Description, v.Publisher, v.ImageUrl, v.SiteUrl, v.Score);

    private record SourceSearchStatsDto(string Source, int ResultCount, long ElapsedMs, bool Success, string? ErrorMessage);

    private record SearchVolumesJobResultDto(VolumeSearchPageDto Page, IEnumerable<SourceSearchStatsDto> Stats);

    private static SourceSearchStatsDto ToStatsDto(SourceSearchStats s) =>
        new(s.Source, s.ResultCount, s.ElapsedMs, s.Success, s.ErrorMessage);

    public record StartSearchRequest(string Name, int Page = 1, int? PageSize = null);

    // POST /api/volumes/search — lance la recherche multi-source en tant que Job et retourne
    // immédiatement son JobId ; le frontend suit la progression/les traces via SignalR puis
    // récupère le résultat final via GET /api/volumes/search/{jobId}.
    [HttpPost("/api/volumes/search")]
    public IActionResult StartSearch([FromBody] StartSearchRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "name is required." });

        var job = manager.LaunchJobSearchVolumes(new SearchVolumesJobParameters
        {
            Name = req.Name,
            Page = req.Page,
            PageSize = req.PageSize
        });
        return Accepted(new { jobId = job.JobId });
    }

    // GET /api/volumes/search/{jobId} — résultat final d'une recherche lancée via StartSearch ;
    // 404 tant que le job n'est pas terminé (ou s'il a échoué avant de produire un résultat).
    [HttpGet("/api/volumes/search/{jobId:guid}")]
    public IActionResult GetSearchResult(Guid jobId)
    {
        var result = manager.GetSearchJobResult(jobId);
        if (result is null) return NotFound();

        return Ok(new SearchVolumesJobResultDto(
            new VolumeSearchPageDto(
                result.Page.Items.Select(ToSearchDto),
                result.Page.PageNumber, result.Page.PageSize, result.Page.TotalItems,
                result.Page.TotalPages, result.Page.HasNext, result.Page.HasPrev),
            result.Stats.Select(ToStatsDto)));
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
        string AgeRating,
        List<string> Genres,
        List<VolumeAuthor> Authors,
        VolumeImage? Image,
        int CountOfIssues,
        int CountOfDownloadedIssues,
        string? Language,
        string? PublicationStatus,
        string? Origin,
        string? Website,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private static VolumeDto ToDto(Volume v)
        => new(v.Id, v.LibraryId, v.SourceId, v.SourceType, v.Title, v.Year,
               v.Description, v.Publisher, v.Status, v.AgeRating.ToString(), v.Genres, v.Authors,
               v.Image, v.CountOfIssues, v.CountOfDownloadedIssues,
               v.Language, v.PublicationStatus, v.Origin, v.Website,
               v.CreatedAt, v.UpdatedAt);

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

    // FileIssueMap : nom de fichier → IssueId, issu de la popup de revue. Null = appariement auto.
    public record ImportFromDirectoryRequest(string ImportDirectory, Dictionary<string, Guid>? FileIssueMap = null);

    // GET /api/volumes/{volumeId}/import/scan?directory=<path> — aperçu des archives d'un dossier
    // (nom + taille + numéro d'issue déduit) pour alimenter la popup de revue.
    [HttpGet("/api/volumes/{volumeId:guid}/import/scan")]
    public async Task<IActionResult> ScanImport(Guid volumeId, [FromQuery] string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return BadRequest(new { message = "directory is required." });
        try
        {
            var files = await manager.ScanImportDirectoryAsync(volumeId, directory);
            return Ok(new { files });
        }
        catch (DirectoryNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    // POST /api/volumes/{volumeId}/import — lance l'import en Job et retourne son jobId ; suivi via
    // SignalR (JobPanel de la page Volume).
    [HttpPost("/api/volumes/{volumeId:guid}/import")]
    public IActionResult ImportFromDirectory(Guid volumeId, [FromBody] ImportFromDirectoryRequest request)
    {
        var job = manager.LaunchJobImportDirectory(new ImportDirectoryJobParameters
        {
            VolumeId = volumeId,
            Directory = request.ImportDirectory,
            FileIssueMap = request.FileIssueMap
        });
        return Accepted(new { jobId = job.JobId });
    }

    public record UpdateIssueRequest(
        Guid? Id,
        int IssueNumber,
        string? Title,
        int? Year,
        string? Description,
        string? ImageUrl);

    public record UpdateVolumeManuallyRequest(
        string Title,
        int? Year,
        string? Publisher,
        string? Description,
        string? ImageUrl,
        List<VolumeAuthor> Authors,
        List<string> Genres,
        List<UpdateIssueRequest> Issues);

    // PUT /api/volumes/{volumeId}
    [HttpPut("/api/volumes/{volumeId:guid}")]
    public async Task<IActionResult> Update(Guid volumeId, [FromBody] UpdateVolumeManuallyRequest req)
    {
        var issues = req.Issues.Select(i => (i.Id, i.IssueNumber, i.Title, i.Year, i.Description, i.ImageUrl)).ToList();
        var updated = await manager.UpdateVolumeManuallyAsync(
            volumeId, req.Title, req.Year, req.Publisher,
            req.Description, req.ImageUrl, req.Authors, req.Genres, issues);
        return updated ? NoContent() : NotFound();
    }

    // POST /api/volumes/{volumeId}/regenerate-comic-info
    [HttpPost("/api/volumes/{volumeId:guid}/regenerate-comic-info")]
    public IActionResult RegenerateComicInfo(Guid volumeId)
    {
        _ = manager.LaunchJobRegenerateComicInfo(new RegenerateComicInfoJobParameters { VolumeId = volumeId });
        return Accepted(new { message = $"ComicInfo regeneration job started for volume {volumeId}." });
    }

    // POST /api/volumes/{volumeId}/recalculate-statistics
    [HttpPost("/api/volumes/{volumeId:guid}/recalculate-statistics")]
    public async Task<IActionResult> RecalculateStatistics(Guid volumeId)
    {
        var updated = await manager.RecalculateVolumeStatisticsAsync(volumeId);
        return updated is not null ? Ok(ToDto(updated)) : NotFound();
    }

    public record PatchVolumeAgeRatingRequest(string AgeRating);

    // PATCH /api/volumes/{volumeId}/age-rating
    [HttpPatch("/api/volumes/{volumeId:guid}/age-rating")]
    public async Task<IActionResult> PatchAgeRating(Guid volumeId, [FromBody] PatchVolumeAgeRatingRequest req)
    {
        if (!Enum.TryParse<AgeRating>(req.AgeRating, out var rating))
            return BadRequest(new { message = $"Invalid AgeRating value: {req.AgeRating}" });
        var updated = await manager.UpdateVolumeAgeRatingAsync(volumeId, rating);
        return updated ? NoContent() : NotFound();
    }

    public record RematchFromSourceRequest(string Source, string SourceId);

    // POST /api/volumes/{volumeId}/rematch — lance le rematch en Job et retourne son JobId
    // immédiatement ; suivi de la progression/traces via SignalR (JobPanel sur la page Volume).
    [HttpPost("/api/volumes/{volumeId:guid}/rematch")]
    public IActionResult RematchFromSource(Guid volumeId, [FromBody] RematchFromSourceRequest req)
    {
        var job = manager.LaunchJobRematchVolume(new RematchVolumeJobParameters
        { VolumeId = volumeId, Source = req.Source, SourceId = req.SourceId });
        return Accepted(new { jobId = job.JobId });
    }

    // Options de la popup "Refresh" — toutes à true par défaut (comportement historique si un
    // appelant poste un body vide/partiel), une case décochée désactive l'étape correspondante.
    // SyncNewIssuesOnly (radio sous "Sync with source", défaut false = historique "ALL issues") :
    // ne synchroniser depuis la source que les issues/albums encore inconnus en base.
    public record RefreshVolumeRequest(
        bool SyncFromSource = true,
        bool RecalculateStatistics = true,
        bool RegenerateComicInfo = true,
        bool ScanKavita = true,
        bool SyncNewIssuesOnly = false);

    // POST /api/volumes/{volumeId}/refresh — idem que /rematch, mais réutilise la source déjà
    // associée au volume (pas de recherche à refaire) ; masqué côté UI pour un volume manuel.
    [HttpPost("/api/volumes/{volumeId:guid}/refresh")]
    public async Task<IActionResult> Refresh(Guid volumeId, [FromBody] RefreshVolumeRequest? req = null)
    {
        var options = req ?? new RefreshVolumeRequest();
        try
        {
            var job = await manager.LaunchJobRefreshVolume(
                volumeId, options.SyncFromSource, options.RecalculateStatistics,
                options.RegenerateComicInfo, options.ScanKavita, options.SyncNewIssuesOnly);
            return job is not null ? Accepted(new { jobId = job.JobId }) : NotFound();
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    // DELETE /api/volumes/{volumeId}
    [HttpDelete("/api/volumes/{volumeId:guid}")]
    public async Task<IActionResult> Delete(Guid volumeId)
    {
        try
        {
            var deleted = await manager.DeleteVolumeAsync(volumeId);
            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
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

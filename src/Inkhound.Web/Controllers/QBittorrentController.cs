using Inkhound.Core;
using Inkhound.Core.Analysis;
using Inkhound.Core.Models;
using Inkhound.Core.QBittorrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

// IssueId (flux page Issue) ou VolumeId (flux page Volume) — IssueId requis quand Selective=false
// (grab direct, cible toujours une issue précise) ; l'un des deux requis quand Selective=true.
// Title/TrackerName sont persistés sur l'IssueDownload créé — l'item porte sa propre identité,
// indépendante de ce que QBittorrent peut ou non retrouver en direct par la suite.
public record QBittorrentGrabRequest(
    string DownloadUrl, string Title, string? TrackerName,
    Guid? IssueId, Guid? VolumeId, bool Selective = false);
public record ApplySelectionRequest(
    string TorrentHash, string DownloadUrl, string Title, string? TrackerName,
    Guid? IssueId, Guid? VolumeId,
    int[] SelectedFileIndices, Dictionary<int, Guid>? FileIssueOverrides);
public record UpdateDownloadHashRequest(string Hash);

[ApiController]
[Route("api/qbittorrent")]
[Authorize(Roles = "admin")]
public class QBittorrentController(InkhoundManager manager) : ControllerBase
{
    private record QBittorrentCategoryDto(string Name, string SavePath);

    // DetectedIssueNumber : numéro de tome extrait du nom de fichier (TorrentTypeAnalyzer.ExtractIssueNumber),
    // exposé pour que le frontend puisse pré-associer le fichier à une issue manquante sans dupliquer la regex.
    private record TorrentFileDto(int Index, string Name, long Size, int? DetectedIssueNumber);

    private static TorrentFileDto ToFileDto(QBittorrentTorrentFile f)
        => new(f.Index, f.Name, f.Size, TorrentTypeAnalyzer.ExtractIssueNumber(f.Name));

    // État live du torrent d'un PACK pendant la revue des fichiers (avant validation de la sélection).
    private record PackFetchStatusDto(
        bool Found, string State, double Progress, int NumComplete, int NumSeeds,
        long Dlspeed, long Eta, bool MetadataReady, IEnumerable<TorrentFileDto> Files);

    private record DownloadItemDto(
        Guid Id,
        Guid IssueId,
        string TorrentHash,
        string TorrentTitle,
        string? TrackerName,
        string Status,
        DateTime AddedAt,
        DateTime? UpdatedAt,
        int? IssueNumber,
        string? IssueTitle,
        string? VolumeTitle,
        double? Progress,
        long? Dlspeed,
        long? Eta,
        long? Size);

    // TorrentTitle vient de l'item stocké (d.Download.TorrentTitle), pas d'une lecture live QBittorrent
    // (d.Torrent?.Name) : l'identité du download reste affichable même si QBittorrent ne retrouve plus
    // le torrent (hash orphelin, torrent supprimé...). Progress/Dlspeed/Eta/Size restent des indicateurs
    // live optionnels, absents quand QBittorrent ne retrouve pas le torrent.
    private static DownloadItemDto ToDto(DownloadItemData d) => new(
        d.Download.Id,
        d.Download.IssueId,
        d.Download.TorrentHash,
        d.Download.TorrentTitle,
        d.Download.TrackerName,
        d.Download.Status.ToString(),
        d.Download.AddedAt,
        d.Download.UpdatedAt,
        d.Issue?.IssueNumber,
        d.Issue?.Title,
        d.Volume?.Title,
        d.Torrent?.Progress,
        d.Torrent?.Dlspeed,
        d.Torrent?.Eta,
        d.Torrent?.Size);

    // GET /api/qbittorrent/categories
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await manager.GetQBittorrentCategoriesAsync();
        if (categories.Count == 0)
            return StatusCode(503, new { message = "QBittorrent service unavailable or no categories found." });

        return Ok(categories.Select(c => new QBittorrentCategoryDto(c.Name, c.SavePath)));
    }

    // POST /api/qbittorrent/grab
    [HttpPost("grab")]
    public async Task<IActionResult> Grab([FromBody] QBittorrentGrabRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.DownloadUrl))
            return BadRequest(new { message = "DownloadUrl is required." });
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "Title is required." });

        if (req.Selective)
        {
            if (req.IssueId is null && req.VolumeId is null)
                return BadRequest(new { message = "IssueId or VolumeId is required." });

            var (success, hash, files) = await manager.GrabPackSelectiveAsync(req.DownloadUrl);
            if (!success)
                return StatusCode(502, new { message = "Failed to add torrent in paused mode." });

            var fileDtos = files?.Select(ToFileDto) ?? [];
            return Ok(new { torrentHash = hash, files = fileDtos });
        }
        else
        {
            if (req.IssueId is not { } issueId)
                return BadRequest(new { message = "IssueId is required for a non-selective grab." });

            var (success, torrentHash) = await manager.GrabToQBittorrentAsync(
                req.DownloadUrl, req.Title, req.TrackerName, issueId);
            if (!success)
                return StatusCode(502, new { message = "Failed to add torrent to QBittorrent." });

            return Ok(new { torrentHash, files = (IEnumerable<TorrentFileDto>?)null });
        }
    }

    // GET /api/qbittorrent/torrent/{hash}/fetch-status — état live du torrent d'un PACK pendant la
    // revue des fichiers (détection « stalled », récupération tardive de la liste de fichiers).
    [HttpGet("torrent/{hash}/fetch-status")]
    public async Task<IActionResult> GetPackFetchStatus(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return BadRequest(new { message = "Torrent hash is required." });

        var status = await manager.GetPackFetchStatusAsync(hash);
        if (status is null)
            return StatusCode(503, new { message = "QBittorrent service unavailable." });

        return Ok(new PackFetchStatusDto(
            status.Found, status.State, status.Progress, status.NumComplete, status.NumSeeds,
            status.Dlspeed, status.Eta, status.MetadataReady, status.Files.Select(ToFileDto)));
    }

    // POST /api/qbittorrent/torrent/{hash}/abort — annule la revue d'un PACK : supprime le torrent
    // et ses fichiers déjà téléchargés de QBittorrent (uniquement tant que la sélection n'est pas
    // validée, c.-à-d. qu'aucun IssueDownload ne référence ce hash).
    [HttpPost("torrent/{hash}/abort")]
    public async Task<IActionResult> AbortPackSelection(string hash)
    {
        var (success, error) = await manager.AbortPackSelectionAsync(hash);
        return success ? Ok(new { message = "Torrent removed." }) : BadRequest(new { message = error });
    }

    // POST /api/qbittorrent/apply-selection
    [HttpPost("apply-selection")]
    public async Task<IActionResult> ApplySelection([FromBody] ApplySelectionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.TorrentHash))
            return BadRequest(new { message = "TorrentHash is required." });
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "Title is required." });
        if (req.SelectedFileIndices is null || req.SelectedFileIndices.Length == 0)
            return BadRequest(new { message = "At least one file must be selected." });
        if (req.IssueId is null && req.VolumeId is null)
            return BadRequest(new { message = "IssueId or VolumeId is required." });

        var success = await manager.ApplyPackSelectionAsync(
            req.TorrentHash, req.DownloadUrl, req.Title, req.TrackerName,
            req.IssueId, req.VolumeId, req.SelectedFileIndices, req.FileIssueOverrides);
        if (!success)
            return StatusCode(502, new { message = "Failed to apply file selection." });

        return Ok(new { message = "Selection applied. Torrent resumed." });
    }

    private record DownloadsPageDto(
        IEnumerable<DownloadItemDto> Items, int PageNumber, int PageSize,
        int TotalItems, int TotalPages, bool HasNext, bool HasPrev);

    // GET /api/qbittorrent/downloads
    // GET /api/qbittorrent/downloads?status=Downloading&status=Paused&page=1&pageSize=20
    [HttpGet("downloads")]
    public async Task<IActionResult> GetDownloads(
        [FromQuery] List<string>? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        List<DownloadStatus>? statusFilter = status is { Count: > 0 }
            ? status
                .Select(s => Enum.TryParse<DownloadStatus>(s, true, out var parsed) ? parsed : (DownloadStatus?)null)
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .ToList()
            : null;

        var result = await manager.GetDownloadsPageAsync(statusFilter, page, pageSize);
        return Ok(new DownloadsPageDto(
            result.Items.Select(ToDto), result.PageNumber, result.PageSize,
            result.TotalItems, result.TotalPages, result.HasNext, result.HasPrev));
    }

    // POST /api/qbittorrent/downloads/process
    [HttpPost("downloads/process")]
    public IActionResult ProcessDownloads()
    {
        _ = manager.LaunchJobProcessDownloads(new ProcessDownloadsJobParameters());
        return Accepted(new { message = "Download processing started." });
    }

    // POST /api/qbittorrent/downloads/{id}/process
    [HttpPost("downloads/{id:guid}/process")]
    public IActionResult ProcessDownload(Guid id)
    {
        _ = manager.LaunchJobProcessDownloads(new ProcessDownloadsJobParameters { IssueDownloadId = id });
        return Accepted(new { message = "Download processing started." });
    }

    // POST /api/qbittorrent/downloads/refresh — revérifie toute la table contre QBittorrent
    [HttpPost("downloads/refresh")]
    public IActionResult RefreshDownloads()
    {
        _ = manager.LaunchJobRefreshDownloads(new RefreshDownloadsJobParameters());
        return Accepted(new { message = "Download refresh started." });
    }

    // PUT /api/qbittorrent/downloads/{id}/hash — corrige manuellement un hash orphelin (NotFound)
    [HttpPut("downloads/{id:guid}/hash")]
    public async Task<IActionResult> UpdateDownloadHash(Guid id, [FromBody] UpdateDownloadHashRequest req)
    {
        var (success, error, item) = await manager.UpdateDownloadHashAsync(id, req.Hash);
        return success ? Ok(ToDto(item!)) : BadRequest(new { message = error });
    }

    // DELETE /api/qbittorrent/downloads/{id}?removeTorrent=true
    [HttpDelete("downloads/{id:guid}")]
    public async Task<IActionResult> DeleteDownload(Guid id, [FromQuery] bool removeTorrent = false)
    {
        var (success, error, torrentRemoved) = await manager.DeleteDownloadAsync(id, removeTorrent);
        return success ? Ok(new { torrentRemoved }) : NotFound(new { message = error });
    }
}

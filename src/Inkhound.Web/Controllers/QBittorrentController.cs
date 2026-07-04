using Inkhound.Core;
using Inkhound.Core.Models;
using Inkhound.Core.QBittorrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

public record QBittorrentGrabRequest(string DownloadUrl, Guid IssueId);

[ApiController]
[Route("api/qbittorrent")]
[Authorize(Roles = "admin")]
public class QBittorrentController(InkhoundManager manager) : ControllerBase
{
    private record QBittorrentCategoryDto(string Name, string SavePath);

    private record DownloadItemDto(
        Guid Id,
        Guid IssueId,
        string TorrentHash,
        string Status,
        DateTime AddedAt,
        DateTime? UpdatedAt,
        int? IssueNumber,
        string? IssueTitle,
        string? VolumeTitle,
        string? TorrentName,
        double? Progress,
        long? Dlspeed,
        long? Eta,
        long? Size);

    private static DownloadItemDto ToDto(DownloadItemData d) => new(
        d.Download.Id,
        d.Download.IssueId,
        d.Download.TorrentHash,
        d.Download.Status.ToString(),
        d.Download.AddedAt,
        d.Download.UpdatedAt,
        d.Issue?.IssueNumber,
        d.Issue?.Title,
        d.Volume?.Title,
        d.Torrent?.Name,
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

        var (success, torrentHash) = await manager.GrabToQBittorrentAsync(req.DownloadUrl, req.IssueId);
        if (!success)
            return StatusCode(502, new { message = "Failed to add torrent to QBittorrent." });

        return Ok(new { torrentHash });
    }

    // GET /api/qbittorrent/downloads
    // GET /api/qbittorrent/downloads?status=Downloading
    [HttpGet("downloads")]
    public async Task<IActionResult> GetDownloads([FromQuery] string? status = null)
    {
        DownloadStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<DownloadStatus>(status, true, out var parsed))
            statusFilter = parsed;

        var downloads = await manager.GetDownloadsAsync(statusFilter);
        return Ok(downloads.Select(ToDto));
    }
}

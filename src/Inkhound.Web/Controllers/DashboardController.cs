using Inkhound.Core;
using Inkhound.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController(InkhoundManager manager) : ControllerBase
{
    private record LibraryStatsDto(
        Guid Id, string Name, int VolumesCount,
        int IssuesCount, int DownloadedIssuesCount, int DownloadingIssuesCount, int MissingIssuesCount);

    private record RecentVolumeDto(
        Guid Id, Guid LibraryId, string Title, VolumeImage? Image, DateTime DateAdded, int CompletionPercent);

    // Complétude = issues Standard téléchargées / issues Standard (compteurs du Volume).
    private static RecentVolumeDto ToRecentDto(Volume v) => new(
        v.Id, v.LibraryId, v.Title, v.Image, v.DateAdded,
        v.CountOfIssues > 0 ? (int)Math.Round(v.CountOfDownloadedIssues / (double)v.CountOfIssues * 100.0) : 0);

    private static MostWantedIssueDto ToMostWantedDto(InkhoundManager.DashboardMostWantedIssue m) => new(
        m.IssueId, m.VolumeId, m.LibraryId, m.VolumeTitle, m.Image,
        m.IssueNumber, m.IssueTitle, m.OwnedCount, m.TotalCount, m.MissingCount,
        m.CurrentCompletionPercent, m.ProjectedCompletionPercent);

    private const int MaxListLimit = 200;

    private record MostWantedIssueDto(
        Guid IssueId, Guid VolumeId, Guid LibraryId, string VolumeTitle, VolumeImage? Image,
        int IssueNumber, string? IssueTitle,
        int OwnedCount, int TotalCount, int MissingCount,
        int CurrentCompletionPercent, int ProjectedCompletionPercent);

    private record DashboardStatsDto(
        int LibrariesCount,
        int VolumesCount, int VolumesMonitored, int VolumesCompleted, int VolumesPaused,
        int IssuesCount, int IssuesDownloaded, int IssuesDownloading, int IssuesMissing,
        long TotalDownloadedBytes,
        IEnumerable<LibraryStatsDto> Libraries,
        IEnumerable<RecentVolumeDto> RecentVolumes,
        IEnumerable<MostWantedIssueDto> MostWanted);

    // GET /api/dashboard/stats
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var stats = await manager.GetDashboardStatsAsync(ct);

        return Ok(new DashboardStatsDto(
            stats.LibrariesCount,
            stats.VolumesCount, stats.VolumesMonitored, stats.VolumesCompleted, stats.VolumesPaused,
            stats.IssuesCount, stats.IssuesDownloaded, stats.IssuesDownloading, stats.IssuesMissing,
            stats.TotalDownloadedBytes,
            stats.Libraries.Select(l => new LibraryStatsDto(
                l.Id, l.Name, l.VolumesCount,
                l.IssuesCount, l.DownloadedIssuesCount, l.DownloadingIssuesCount, l.MissingIssuesCount)),
            stats.RecentVolumes.Select(ToRecentDto),
            stats.MostWanted.Select(ToMostWantedDto)));
    }

    // GET /api/dashboard/recent-volumes?limit=50 — sous-page « Recently added »
    [HttpGet("recent-volumes")]
    public async Task<IActionResult> GetRecentVolumes([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var volumes = await manager.GetRecentVolumesAsync(Math.Clamp(limit, 1, MaxListLimit), ct);
        return Ok(volumes.Select(ToRecentDto));
    }

    // GET /api/dashboard/most-wanted?limit=50 — sous-page « Most wanted »
    [HttpGet("most-wanted")]
    public async Task<IActionResult> GetMostWanted([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var items = await manager.GetMostWantedAsync(Math.Clamp(limit, 1, MaxListLimit), ct);
        return Ok(items.Select(ToMostWantedDto));
    }
}

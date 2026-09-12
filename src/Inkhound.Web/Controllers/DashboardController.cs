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

    private record RecentVolumeDto(Guid Id, Guid LibraryId, string Title, VolumeImage? Image, DateTime DateAdded);

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
            stats.RecentVolumes.Select(v => new RecentVolumeDto(v.Id, v.LibraryId, v.Title, v.Image, v.DateAdded)),
            stats.MostWanted.Select(m => new MostWantedIssueDto(
                m.IssueId, m.VolumeId, m.LibraryId, m.VolumeTitle, m.Image,
                m.IssueNumber, m.IssueTitle, m.OwnedCount, m.TotalCount, m.MissingCount,
                m.CurrentCompletionPercent, m.ProjectedCompletionPercent))));
    }
}

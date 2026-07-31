namespace Inkhound.Core.Models;

public enum DownloadStatus { Downloading, Paused, Finished, Syncing, Done, Error, Unknown, NotFound }

public class IssueDownload
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public string TorrentHash { get; set; } = string.Empty;
    public string TorrentTitle { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string? TrackerName { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Unknown;
    public DateTime AddedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

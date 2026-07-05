namespace Inkhound.Core.Models;

public enum DownloadStatus { Downloading, Paused, Finished, Syncing, Done, Error, Unknown }

public class IssueDownload
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public string TorrentHash { get; set; } = string.Empty;
    public DownloadStatus Status { get; set; } = DownloadStatus.Unknown;
    public DateTime AddedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

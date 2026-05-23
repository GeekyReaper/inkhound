namespace Inkhound.Core.Models;

public enum IssueStatus { DOWNLOADING, DOWNLOADED, MISSING }

public class Issue
{
    public Guid Id { get; set; }
    public string ComicVineId { get; set; } = string.Empty;
    public Guid VolumeId { get; set; }
    public int IssueNumber { get; set; } = 0;
    public string? Title { get; set; }
    public int? Year { get; set; }
    public string? Description { get; set; }
    public VolumeImage? Image { get; set; }
    public List<VolumeAuthor> Authors { get; set; } = [];


    public string? CbzFilename { get; set; }

    public int FileSizeBytes { get; set; } = 0;

    public DateTime DownloadedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public IssueStatus Status { get; set; } = IssueStatus.MISSING;
}

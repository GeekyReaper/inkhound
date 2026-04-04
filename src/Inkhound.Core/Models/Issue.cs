namespace Inkhound.Core.Models;

public enum IssueStatus { SEEKING, DOWNLOADING, DOWNLOADED }

public class Issue
{
    public Guid Id { get; set; }
    public string ComicVineId { get; set; } = string.Empty;
    public Guid VolumeId { get; set; }
    public int IssueNumber { get; set; }
    public string? Title { get; set; }
    public int? Year { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? FilePath { get; set; }
    public string? CbzFilename { get; set; }
    public DateTime? PublishedAt { get; set; }
    public IssueStatus Status { get; set; } = IssueStatus.SEEKING;
}

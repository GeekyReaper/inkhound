namespace Inkhound.Core.Models;

public enum VolumeStatus { MONITORED, COMPLETED, FREEZE }

// Represents one author/contributor entry stored as JSON in the authors field
public record VolumeAuthor(string Name, string Role);

// All image variants provided by ComicVine
public record VolumeImage(
    string? IconUrl,
    string? MediumUrl,
    string? ScreenUrl,
    string? ScreenLargeUrl,
    string? SmallUrl,
    string? SuperUrl,
    string? ThumbUrl,
    string? TinyUrl,
    string? OriginalUrl,
    string? ImageTags
);

public class Volume
{
    public Guid Id { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public Guid LibraryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string? Description { get; set; }
    public VolumeImage? Image { get; set; }
    public string? Publisher { get; set; }
    public List<VolumeAuthor> Authors { get; set; } = [];
    public List<string> Genres { get; set; } = [];
    public VolumeStatus Status { get; set; } = VolumeStatus.MONITORED;

    public List<string>? Issues { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

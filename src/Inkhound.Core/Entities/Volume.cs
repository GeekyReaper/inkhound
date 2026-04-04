namespace Inkhound.Core.Entities;

public enum VolumeStatus { MONITORED, COMPLETED, FREEZE }

// Represents one author/contributor entry stored as JSON in the authors field
public record VolumeAuthor(string Name, string Role);

public class Volume
{
    public Guid         Id          { get; set; }
    public string       ComicVineId { get; set; } = string.Empty;
    public Guid         LibraryId   { get; set; }
    public string       Title       { get; set; } = string.Empty;
    public int?         Year        { get; set; }
    public string?      Description { get; set; }
    public string?      ImageUrl    { get; set; }
    public string?      Publisher   { get; set; }
    public List<VolumeAuthor> Authors { get; set; } = [];
    public List<string> Genres      { get; set; } = [];
    public VolumeStatus Status      { get; set; } = VolumeStatus.MONITORED;
    public DateTime     CreatedAt   { get; set; }
    public DateTime     UpdatedAt   { get; set; }
}

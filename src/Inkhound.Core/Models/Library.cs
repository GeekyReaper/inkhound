namespace Inkhound.Core.Models;

public class Library
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int KavitaLibraryId { get; set; } = 0;
    public DateTime CreatedAt { get; set; }
}

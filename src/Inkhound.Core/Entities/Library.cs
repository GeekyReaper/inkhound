namespace Inkhound.Core.Entities;

public class Library
{
    public Guid   Id           { get; set; }
    public string Name         { get; set; } = string.Empty;
    public string RootPath     { get; set; } = string.Empty;
    public string KavitaFolder { get; set; } = string.Empty;
    public DateTime CreatedAt  { get; set; }
}

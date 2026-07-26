namespace Inkhound.Core.Models;

public class SelectedIndexer
{
    public Guid LibraryId { get; set; }
    public int IndexerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public string CategoriesJson { get; set; } = string.Empty;
}

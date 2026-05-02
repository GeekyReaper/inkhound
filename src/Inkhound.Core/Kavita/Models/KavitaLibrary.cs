namespace Inkhound.Core.Kavita.Models;

public class KavitaLibrary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Type { get; set; }
    public DateTime LastScanned { get; set; }
}

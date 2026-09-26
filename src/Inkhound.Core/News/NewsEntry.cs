namespace Inkhound.Core.News;

/// <summary>
/// Apparition d'un album dans un flux pour une période donnée (table <c>NewsEntries</c>) — c'est
/// l'historique des flux. Unicité (<c>Provider</c>, <c>Feed</c>, <c>Period</c>, <c>AlbumId</c>).
/// </summary>
public class NewsEntry
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public NewsFeed Feed { get; set; }
    public string AlbumId { get; set; } = string.Empty;

    /// <summary>
    /// Période de l'apparition : lundi de la semaine (<c>yyyy-MM-dd</c>) pour
    /// <see cref="NewsFeed.TopSales"/>, mois de sortie (<c>yyyy-MM</c>) pour <see cref="NewsFeed.Releases"/>.
    /// </summary>
    public string Period { get; set; } = string.Empty;

    public int? Rank { get; set; }
    public string? Evolution { get; set; }
    public int? EvolutionDelta { get; set; }
    public int? WeeksInChart { get; set; }

    public DateTime FetchedAtUtc { get; set; }
}

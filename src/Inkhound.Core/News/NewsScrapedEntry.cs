namespace Inkhound.Core.News;

/// <summary>
/// Item brut d'un flux tel que lu sur la page liste de la source, avant tout enrichissement.
/// Les champs de classement (<see cref="Rank"/>, <see cref="Evolution"/>…) ne concernent que le
/// flux <see cref="NewsFeed.TopSales"/>.
/// </summary>
public record NewsScrapedEntry
{
    /// <summary>Identifiant de l'album chez la source.</summary>
    public required string AlbumId { get; init; }
    public string SeriesTitle { get; init; } = string.Empty;
    public string? AlbumNumber { get; init; }
    public string? AlbumTitle { get; init; }
    public string? Publisher { get; init; }
    public DateOnly? ReleaseDate { get; init; }
    public NewsCategory? Category { get; init; }
    public string? ShortDescription { get; init; }
    public string? CoverUrl { get; init; }
    public string? CoverLargeUrl { get; init; }
    public string? AlbumUrl { get; init; }

    /// <summary>Rang dans le classement (1 = meilleure vente).</summary>
    public int? Rank { get; init; }

    /// <summary>Évolution par rapport à la semaine précédente : "New", "Up", "Down" ou "Stable".</summary>
    public string? Evolution { get; init; }

    /// <summary>Nombre de places gagnées/perdues (avec <see cref="Evolution"/> "Up"/"Down").</summary>
    public int? EvolutionDelta { get; init; }

    /// <summary>Nombre de semaines de présence dans le classement.</summary>
    public int? WeeksInChart { get; init; }
}

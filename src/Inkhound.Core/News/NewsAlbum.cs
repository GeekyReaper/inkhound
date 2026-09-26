namespace Inkhound.Core.News;

/// <summary>
/// Album apparu dans un flux d'actualité (table <c>NewsAlbums</c>, clé <c>Provider</c> + <c>AlbumId</c>).
/// Les champs « liste » sont renseignés dès le scraping du flux ; <see cref="SeriesId"/> et
/// <see cref="EnrichmentJson"/> par l'enrichissement (job News par lots, ou à la demande depuis
/// l'UI). Un album présent dans plusieurs flux / semaines n'existe qu'une fois ici — ses
/// apparitions sont dans <see cref="NewsEntry"/>.
/// </summary>
public class NewsAlbum
{
    public string Provider { get; set; } = string.Empty;
    public string AlbumId { get; set; } = string.Empty;

    public string SeriesTitle { get; set; } = string.Empty;
    public string? AlbumNumber { get; set; }
    public string? AlbumTitle { get; set; }
    public string? Publisher { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public NewsCategory? Category { get; set; }
    public string? ShortDescription { get; set; }
    public string? CoverUrl { get; set; }
    public string? CoverLargeUrl { get; set; }
    public string? AlbumUrl { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    /// <summary>Identifiant de la série chez la source — <c>null</c> tant que l'album n'est pas enrichi.</summary>
    public string? SeriesId { get; set; }

    /// <summary><see cref="NewsAlbumEnrichment"/> sérialisé, <c>null</c> tant que l'album n'est pas enrichi.</summary>
    public string? EnrichmentJson { get; set; }

    /// <summary>Horodatage du dernier enrichissement réussi, <c>null</c> = en attente.</summary>
    public DateTime? EnrichedAtUtc { get; set; }

    /// <summary>Nombre d'échecs d'enrichissement — au-delà de <c>NewsOptions.MaxEnrichAttempts</c> l'album n'est plus retenté par le job.</summary>
    public int EnrichAttempts { get; set; }
}

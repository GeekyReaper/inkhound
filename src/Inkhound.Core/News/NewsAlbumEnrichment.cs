namespace Inkhound.Core.News;

/// <summary>
/// Détail complet d'un album lu sur sa page (série de rattachement, auteurs, visuels…), persisté
/// en JSON dans <see cref="NewsAlbum.EnrichmentJson"/> par le job News.
/// </summary>
public record NewsAlbumEnrichment
{
    public required string AlbumId { get; init; }
    public required string SeriesId { get; init; }
    public string SeriesTitle { get; init; } = string.Empty;
    public string? AlbumTitle { get; init; }
    public string? AlbumNumber { get; init; }
    public string? Publisher { get; init; }
    public string? Collection { get; init; }
    public string? Year { get; init; }
    public string? LegalDeposit { get; init; }
    public string? Ean { get; init; }
    public string? Genre { get; init; }
    public string? Description { get; init; }
    public double? Rating { get; init; }
    public int? RatingCount { get; init; }
    public int? Pages { get; init; }
    public string? CoverUrl { get; init; }
    public string? CoverLargeUrl { get; init; }
    public string? Url { get; init; }
    public List<NewsAuthor> Authors { get; init; } = [];
    public List<NewsImage> Images { get; init; } = [];
}

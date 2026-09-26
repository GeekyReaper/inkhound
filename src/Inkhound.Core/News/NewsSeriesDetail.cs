namespace Inkhound.Core.News;

/// <summary>Fiche d'une série telle que lue en direct chez la source (page détail News).</summary>
public record NewsSeriesDetail
{
    public required string SeriesId { get; init; }
    public required string Title { get; init; }
    public string? Genre { get; init; }
    public string? Status { get; init; }
    public int? AlbumCount { get; init; }
    public string? Origin { get; init; }
    public string? Language { get; init; }
    public string? StartYear { get; init; }
    public string? EndYear { get; init; }
    public string? Description { get; init; }
    public string? Publisher { get; init; }
    public string? CoverUrl { get; init; }
    public string? Url { get; init; }
    public List<NewsSeriesAlbum> Albums { get; init; } = [];
}

namespace Inkhound.Core.News;

/// <summary>Album d'une série dans la page détail News (liste des tomes).</summary>
public record NewsSeriesAlbum(
    string AlbumId, string Title, string? Number, string? Year, string? CoverUrl, string Category);

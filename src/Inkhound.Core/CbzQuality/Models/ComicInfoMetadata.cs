namespace Inkhound.Core.CbzQuality.Models;

public sealed record ComicInfoMetadata
{
    public string? Title { get; init; }
    public string? Series { get; init; }
    public string? SeriesSort { get; init; }
    public string? LocalizedSeries { get; init; }
    public string? Number { get; init; }
    public string? Volume { get; init; }
    public string? Summary { get; init; }
    public string? Writer { get; init; }
    public string? Penciller { get; init; }
    public string? Inker { get; init; }
    public string? Colorist { get; init; }
    public string? Letterer { get; init; }
    public string? CoverArtist { get; init; }
    public string? Editor { get; init; }
    public string? Publisher { get; init; }
    public string? Imprint { get; init; }
    public int? Year { get; init; }
    public int? Month { get; init; }
    public int? Day { get; init; }
    public string? LanguageISO { get; init; }
    public string? Genre { get; init; }
    public string? Tags { get; init; }
    public int? PageCount { get; init; }
    public string? Format { get; init; }
    public string? Gtin { get; init; }
    public string? AgeRating { get; init; }
    public int? Count { get; init; }

    public required bool ParsedSuccessfully { get; init; }
    public string? ParseError { get; init; }
}

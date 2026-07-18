namespace Inkhound.Core.CbzQuality.Models;

/// <summary>
/// Aggregates resolution / compression-quality / weight signals used by the Kavita "fluidity"
/// scoring axes (see KavitaCompatibilityScorer). These are performance heuristics, not
/// documented Kavita hard requirements.
/// </summary>
public sealed record QualityAnalysis
{
    public double? MedianHeightPx { get; init; }
    public required int ImagesTooLowResolutionCount { get; init; }
    public required int ImagesBelowIdealResolutionCount { get; init; }
    public required int ImagesIdealResolutionCount { get; init; }
    public required int ImagesAboveIdealResolutionCount { get; init; }
    public required int ImagesTooHighResolutionCount { get; init; }

    public double? AverageJpegQuality { get; init; }
    public required int JpegTooLowQualityCount { get; init; }
    public required int JpegLowQualityCount { get; init; }
    public required int JpegHighQualityCount { get; init; }
    public required int JpegTooHighQualityCount { get; init; }

    public double? AverageWebpBitsPerPixel { get; init; }
    public required int LosslessWebpCount { get; init; }
    public required int WebpBppTooLowCount { get; init; }
    public required int WebpBppLowCount { get; init; }
    public required int WebpBppHighCount { get; init; }
    public required int WebpBppTooHighCount { get; init; }
}

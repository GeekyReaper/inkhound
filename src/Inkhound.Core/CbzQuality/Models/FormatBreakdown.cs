namespace Inkhound.Core.CbzQuality.Models;

public sealed record FormatBreakdown
{
    public required string Format { get; init; }
    public required int Count { get; init; }
    public required long TotalBytes { get; init; }
    public required double AverageBytes { get; init; }

    /// <summary>Average compression-derived bits-per-pixel across successfully decoded images of this
    /// format. Null if none decoded. Used to make weight/quality scoring resolution-aware.</summary>
    public double? AverageBitsPerPixel { get; init; }

    public required bool IsSupportedByKavita { get; init; }
}

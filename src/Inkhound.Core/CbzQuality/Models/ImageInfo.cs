namespace Inkhound.Core.CbzQuality.Models;

public sealed record ImageInfo
{
    public required bool DecodeSucceeded { get; init; }
    public int? WidthPx { get; init; }
    public int? HeightPx { get; init; }
    public string? DecodedFormatName { get; init; }
    public string? DecodeErrorMessage { get; init; }

    /// <summary>Estimated JPEG encode quality (0-100), read from quantization tables by ImageSharp. Null for non-JPEG.</summary>
    public int? EstimatedJpegQuality { get; init; }

    /// <summary>"Lossy" or "Lossless" for WebP images. Null for other formats.</summary>
    public string? WebpFileFormat { get; init; }

    /// <summary>
    /// Compression-derived bits-per-pixel (entry byte size * 8 / width*height), used as a quality proxy
    /// for formats (WebP) where ImageSharp does not expose an estimated encode quality.
    /// </summary>
    public double? BitsPerPixel { get; init; }
}

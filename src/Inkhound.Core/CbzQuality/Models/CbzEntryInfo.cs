namespace Inkhound.Core.CbzQuality.Models;

public sealed record CbzEntryInfo
{
    public required string FullName { get; init; }
    public required string FileName { get; init; }
    public required string DirectoryPath { get; init; }
    public required long CompressedLength { get; init; }
    public required long UncompressedLength { get; init; }
    public required bool IsImage { get; init; }
    public required bool IsJunk { get; init; }
    public required bool IsComicInfoXml { get; init; }
    public string? DetectedImageFormat { get; init; }
    public string? ExtensionImageFormat { get; init; }
    public bool ExtensionMismatch { get; init; }
    public ImageInfo? Image { get; init; }
}

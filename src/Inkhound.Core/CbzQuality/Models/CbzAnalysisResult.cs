namespace Inkhound.Core.CbzQuality.Models;

public sealed record CbzAnalysisResult
{
    public required string FilePath { get; init; }
    public required long FileSizeBytes { get; init; }
    public required bool IsValidZip { get; init; }
    public required bool LooksLikeRarRenamedToCbz { get; init; }
    public string? LoadError { get; init; }

    public required IReadOnlyList<CbzEntryInfo> Entries { get; init; }
    public required int TotalEntryCount { get; init; }
    public required int ImageEntryCount { get; init; }
    public required int JunkEntryCount { get; init; }
    public required int NonImageNonJunkEntryCount { get; init; }

    public required long TotalImageBytes { get; init; }
    public required double AverageImageBytes { get; init; }
    public required long MinImageBytes { get; init; }
    public required long MaxImageBytes { get; init; }

    /// <summary>Sum of <see cref="CbzEntryInfo.CompressedLength"/> across all entries — the archive's actual on-disk zip footprint.</summary>
    public required long TotalCompressedBytes { get; init; }
    /// <summary>Sum of <see cref="CbzEntryInfo.UncompressedLength"/> across all entries.</summary>
    public required long TotalUncompressedBytes { get; init; }
    /// <summary>
    /// TotalCompressedBytes / TotalUncompressedBytes: 1.0 means the zip container adds nothing (stored/no
    /// compression or already-incompressible content, e.g. WebP/JPEG pages); lower means the deflate step
    /// is actually shrinking entries. 1.0 when the archive has no entries.
    /// </summary>
    public required double ZipCompressionRatio { get; init; }

    public required IReadOnlyList<FormatBreakdown> FormatBreakdown { get; init; }
    public required int ExtensionMismatchCount { get; init; }
    public required int CorruptedImageCount { get; init; }
    public required int UndecodedImageCount { get; init; }

    public required bool HasComicInfoXml { get; init; }
    public required bool ComicInfoXmlAtRoot { get; init; }
    public ComicInfoMetadata? ComicInfo { get; init; }

    public required StructureAnalysis Structure { get; init; }
    public required NamingAnalysis Naming { get; init; }
    public required QualityAnalysis Quality { get; init; }
    public required TimeSpan AnalysisDuration { get; init; }

    public static CbzAnalysisResult ForLoadFailure(string filePath, long fileSizeBytes, bool looksLikeRar, string loadError, TimeSpan duration) => new()
    {
        FilePath = filePath,
        FileSizeBytes = fileSizeBytes,
        IsValidZip = false,
        LooksLikeRarRenamedToCbz = looksLikeRar,
        LoadError = loadError,
        Entries = [],
        TotalEntryCount = 0,
        ImageEntryCount = 0,
        JunkEntryCount = 0,
        NonImageNonJunkEntryCount = 0,
        TotalImageBytes = 0,
        AverageImageBytes = 0,
        MinImageBytes = 0,
        MaxImageBytes = 0,
        TotalCompressedBytes = 0,
        TotalUncompressedBytes = 0,
        ZipCompressionRatio = 1.0,
        FormatBreakdown = [],
        ExtensionMismatchCount = 0,
        CorruptedImageCount = 0,
        UndecodedImageCount = 0,
        HasComicInfoXml = false,
        ComicInfoXmlAtRoot = false,
        ComicInfo = null,
        Structure = new StructureAnalysis { IsFlat = true, MaxFolderDepth = 0, DistinctDirectories = [], TreeText = string.Empty },
        Naming = new NamingAnalysis
        {
            IsConsistentZeroPadding = true,
            LexicographicOrderMatchesNaturalOrder = true,
            MaxDigitCount = 0,
            MinDigitCount = 0,
            DetectedGaps = [],
            DetectedDuplicates = []
        },
        Quality = new QualityAnalysis
        {
            ImagesTooLowResolutionCount = 0,
            ImagesBelowIdealResolutionCount = 0,
            ImagesIdealResolutionCount = 0,
            ImagesAboveIdealResolutionCount = 0,
            ImagesTooHighResolutionCount = 0,
            JpegTooLowQualityCount = 0,
            JpegLowQualityCount = 0,
            JpegHighQualityCount = 0,
            JpegTooHighQualityCount = 0,
            LosslessWebpCount = 0,
            WebpBppTooLowCount = 0,
            WebpBppLowCount = 0,
            WebpBppHighCount = 0,
            WebpBppTooHighCount = 0
        },
        AnalysisDuration = duration
    };
}

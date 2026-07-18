namespace Inkhound.Core.CbzQuality.Models;

/// <summary>
/// Every tunable parameter of the Kavita compatibility scoring algorithm, with defaults matching the
/// tool's own recommended settings. Consumed by both <c>CbzAnalyzer.AnalyzeAsync</c> (the resolution/
/// JPEG-quality/WebP-bpp band edges below decide how pages are bucketed during analysis) and
/// <c>KavitaCompatibilityScorer.Score</c> (the penalty/bonus weights below decide how those buckets —
/// and every other check — affect the score). Callers that customize this must pass the SAME instance
/// to both calls: bucketing happens once during analysis, so a mismatched pair of instances would
/// produce a score whose issue text disagrees with the counts it's describing.
///
/// Compatibility rules (unsupported formats, ComicInfo.xml placement, page ordering, ...) are grounded
/// in documented Kavita scanning behavior. Fluidity rules (format/resolution/quality/weight) are our
/// own performance heuristics, not officially documented by Kavita — kept deliberately lighter-weight
/// than compatibility failures.
/// </summary>
public sealed record ScoringSettings
{
    // --- Compatibility ---

    /// <summary>Points deducted per unsupported image format detected (e.g. BMP, TIFF) — formats Kavita cannot render.</summary>
    public int UnsupportedFormatPenaltyPerFormat { get; init; } = 8;
    /// <summary>Maximum total deduction from unsupported formats, regardless of how many distinct formats appear.</summary>
    public int UnsupportedFormatPenaltyCap { get; init; } = 30;

    /// <summary>Points deducted per image that failed to decode (corrupted or truncated data).</summary>
    public int CorruptedImagePenaltyPerImage { get; init; } = 5;
    /// <summary>Maximum total deduction from corrupted/undecodable images.</summary>
    public int CorruptedImagePenaltyCap { get; init; } = 30;

    /// <summary>Full-score deduction when the archive contains no images at all — an unreadable comic.</summary>
    public int NoImagesFoundPenalty { get; init; } = 100;

    /// <summary>Points deducted per entry whose file extension doesn't match its actual magic-byte-detected format.</summary>
    public int ExtensionMismatchPenaltyPerEntry { get; init; } = 2;
    /// <summary>Maximum total deduction from extension/format mismatches.</summary>
    public int ExtensionMismatchPenaltyCap { get; init; } = 10;

    /// <summary>Points deducted when the archive has no ComicInfo.xml at all.</summary>
    public int NoComicInfoPenalty { get; init; } = 3;
    /// <summary>Points deducted when ComicInfo.xml exists but isn't at the archive root — Kavita ignores it there.</summary>
    public int ComicInfoNotAtRootPenalty { get; init; } = 10;
    /// <summary>Points deducted when ComicInfo.xml is present at the root but fails to parse (malformed XML).</summary>
    public int ComicInfoMalformedPenalty { get; init; } = 15;
    /// <summary>Points deducted when &lt;LocalizedSeries&gt; equals the .cbz filename — a specific collision that makes Kavita silently skip the file during scanning.</summary>
    public int LocalizedSeriesMatchesFilenamePenalty { get; init; } = 15;

    /// <summary>Points deducted when lexicographic and natural sort order of pages diverge (e.g. "10.jpg" before "2.jpg") — reading order would be wrong in Kavita.</summary>
    public int PageSortOrderMismatchPenalty { get; init; } = 12;
    /// <summary>Points deducted when page numbers use an inconsistent digit count — order is still correct, just untidy.</summary>
    public int InconsistentZeroPaddingPenalty { get; init; } = 4;
    /// <summary>Points deducted when gaps are detected in the page numbering sequence.</summary>
    public int PageNumberGapsPenalty { get; init; } = 5;
    /// <summary>Points deducted when duplicate page numbers are detected.</summary>
    public int PageNumberDuplicatesPenalty { get; init; } = 5;

    /// <summary>Points deducted per junk file found (Thumbs.db, .DS_Store, __MACOSX, ...).</summary>
    public int JunkFilePenaltyPerEntry { get; init; } = 1;
    /// <summary>Maximum total deduction from junk files.</summary>
    public int JunkFilePenaltyCap { get; init; } = 5;

    /// <summary>Points deducted when pages sit inside a single level of subfolder instead of the archive root.</summary>
    public int NestedFolderDepth1Penalty { get; init; } = 3;
    /// <summary>Points deducted when pages are nested two or more folder levels deep.</summary>
    public int NestedFolderDepth2PlusPenalty { get; init; } = 8;

    /// <summary>Points deducted per non-image, non-junk file present in the archive (e.g. a stray .txt or .nfo).</summary>
    public int ExtraneousFilePenaltyPerEntry { get; init; } = 2;
    /// <summary>Maximum total deduction from extraneous non-image files.</summary>
    public int ExtraneousFilePenaltyCap { get; init; } = 6;

    /// <summary>Points deducted when the .cbz filename contains parentheses — their contents get stripped by Kavita's series/volume parsing.</summary>
    public int ParenthesesInFilenamePenalty { get; init; } = 2;

    // --- Fluidity: Axis 1 - format ---
    // Judged once against the archive's DOMINANT format (FormatBreakdown[0], already sorted by page
    // count descending — same source of truth Axis 4 uses), not per-image with a cap: the vast
    // majority of real .cbz archives are single-format, so a per-page count/cap added complexity
    // without ever changing the verdict for those files. A format absent from the dictionary (or one
    // Kavita doesn't support at all — see UnsupportedFormatPenaltyPerFormat) scores 0 here.

    /// <summary>
    /// Bonus (positive) or penalty (negative) applied once based on the archive's dominant image
    /// format. Keyed by lowercase format name.
    /// </summary>
    public IReadOnlyDictionary<string, int> FormatScoreByFormat { get; init; } = new Dictionary<string, int>
    {
        ["webp"] = 10,
        ["jpeg"] = 2,
        ["avif"] = 10,
        ["png"] = -10,
        ["gif"] = -10
    };

    // --- Fluidity: Axis 2 - resolution ---
    // Band edges live here (not in CbzAnalyzer) so both analysis-time bucketing and the scorer's issue
    // text read from a single source of truth.

    /// <summary>Lower bound (px) of the ideal page height band. Below this, a page is "below ideal" (or "too low" under <see cref="ResolutionTooLow"/>).</summary>
    public int ResolutionIdealMin { get; init; } = 2048;
    /// <summary>Upper bound (px) of the ideal page height band.</summary>
    public int ResolutionIdealMax { get; init; } = 4000;
    /// <summary>Below this height (px), a page is flagged as too-low resolution — readability is degraded.</summary>
    public int ResolutionTooLow { get; init; } = 1200;
    /// <summary>Above this height (px), a page is flagged as too-high resolution — wasted CPU/storage for no visible gain.</summary>
    public int ResolutionTooHigh { get; init; } = 6000;

    /// <summary>Points deducted per page that's too low or too high resolution (severe end of the resolution axis).</summary>
    public int ResolutionSeverePenaltyPerImage { get; init; } = 4;
    /// <summary>Maximum total deduction from severely mis-sized pages.</summary>
    public int ResolutionSeverePenaltyCap { get; init; } = 30;
    /// <summary>Points deducted per page that's merely below or above the ideal band (not to the severe too-low/too-high extreme).</summary>
    public int ResolutionMinorPenaltyPerImage { get; init; } = 2;
    /// <summary>Maximum total deduction from mildly out-of-band pages.</summary>
    public int ResolutionMinorPenaltyCap { get; init; } = 15;

    // --- Fluidity: Axis 3 - compression quality ---

    /// <summary>Lower bound of the ideal JPEG quality band (0-100 scale).</summary>
    public int JpegQualityIdealMin { get; init; } = 80;
    /// <summary>Upper bound of the ideal JPEG quality band (0-100 scale).</summary>
    public int JpegQualityIdealMax { get; init; } = 90;
    /// <summary>Below this JPEG quality, a page is flagged as too-low quality — visible compression artifacts likely.</summary>
    public int JpegQualityLow { get; init; } = 70;
    /// <summary>Above this JPEG quality, a page is flagged as too-high quality — wasted bytes for imperceptible visual gain.</summary>
    public int JpegQualityHigh { get; init; } = 95;

    /// <summary>Lower bound of the ideal WebP lossy bits-per-pixel band.</summary>
    public double WebpBppIdealMin { get; init; } = 0.30;
    /// <summary>Upper bound of the ideal WebP lossy bits-per-pixel band.</summary>
    public double WebpBppIdealMax { get; init; } = 2.00;
    /// <summary>Below this bits-per-pixel value, a WebP page is flagged as too-low quality.</summary>
    public double WebpBppTooLow { get; init; } = 0.15;
    /// <summary>Above this bits-per-pixel value, a WebP page is flagged as too-high quality.</summary>
    public double WebpBppTooHigh { get; init; } = 3.00;

    /// <summary>Points deducted per page with severely too-low or too-high compression quality (JPEG or WebP).</summary>
    public int QualitySeverePenaltyPerImage { get; init; } = 2;
    /// <summary>Maximum total deduction from severe compression-quality outliers.</summary>
    public int QualitySeverePenaltyCap { get; init; } = 15;
    /// <summary>Points deducted per page slightly outside the ideal compression-quality band.</summary>
    public int QualityMinorPenaltyPerImage { get; init; } = 1;
    /// <summary>Maximum total deduction from minor compression-quality deviations.</summary>
    public int QualityMinorPenaltyCap { get; init; } = 6;
    /// <summary>Points deducted per WebP page that's lossless — heavier than an equivalent lossy WebP at target quality for a scanned page.</summary>
    public int WebpLosslessPenaltyPerImage { get; init; } = 3;
    /// <summary>Maximum total deduction from lossless WebP pages.</summary>
    public int WebpLosslessPenaltyCap { get; init; } = 15;

    // --- Fluidity: Axis 4 - average weight per page (format-aware, in MB per page) ---

    /// <summary>
    /// Flat list of page-weight tiers, each optionally scoped to one image format via
    /// <see cref="PageWeightTier.Format"/>. A tier whose <c>Format</c> is <c>null</c> serves as the
    /// fallback for any dominant format that has no tiers of its own.
    /// </summary>
    public IReadOnlyList<PageWeightTier> PageWeightTiers { get; init; } =
    [
        .. BuildDefaultTiers("webp", 0.18, 0.36, 2.40, 3.60, 6.00),
        .. BuildDefaultTiers("jpeg", 0.30, 0.54, 3.60, 5.40, 9.00),
        .. BuildDefaultTiers("avif", 0.14, 0.30, 1.90, 2.90, 4.80),
        .. BuildDefaultTiers("png", 1.20, 2.40, 8.30, 14.30, 29.80),
        .. BuildDefaultTiers("gif", 1.20, 2.40, 8.30, 14.30, 29.80),
        // Fallback for a dominant format with no tiers of its own — same numbers as "jpeg", matching
        // this axis's pre-tier-list behavior (unknown formats used to fall back to jpeg's band).
        .. BuildDefaultTiers(null, 0.30, 0.54, 3.60, 5.40, 9.00)
    ];

    private static List<PageWeightTier> BuildDefaultTiers(string? format, double tooLow, double idealMin, double idealMax, double heavy, double critical) =>
    [
        new() { Format = format, Label = "Very Low", MinMb = 0, MaxMb = tooLow, Score = -8 },
        new() { Format = format, Label = "Below Ideal", MinMb = tooLow, MaxMb = idealMin, Score = -3 },
        new() { Format = format, Label = "Ideal", MinMb = idealMin, MaxMb = idealMax, Score = 0 },
        new() { Format = format, Label = "Above Ideal", MinMb = idealMax, MaxMb = heavy, Score = -4 },
        new() { Format = format, Label = "Excessive", MinMb = heavy, MaxMb = critical, Score = -12 },
        new() { Format = format, Label = "Very Excessive", MinMb = critical, MaxMb = double.PositiveInfinity, Score = -20 }
    ];

    // --- Fluidity: Axis 5 - zip container compression ---

    /// <summary>
    /// Ordered list of zip-compression tiers, each <c>[MinPct, MaxPct]</c> (both inclusive, in percent
    /// compression saved) mapped to a score (positive = bonus, negative = penalty, 0 = neutral).
    /// Evaluated in list order, first match wins (same convention as <see cref="ScoreBands"/>).
    /// </summary>
    public IReadOnlyList<ZipCompressionTier> ZipCompressionTiers { get; init; } =
    [
        new() { Label = "Uncompressed", MinPct = 0, MaxPct = 0, Score = 2 },
        new() { Label = "Light", MinPct = 0, MaxPct = 5, Score = -2 },
        new() { Label = "Moderate", MinPct = 5, MaxPct = 15, Score = -4 },
        new() { Label = "Heavy", MinPct = 15, MaxPct = double.PositiveInfinity, Score = -5 }
    ];

    // --- Score bands ---

    /// <summary>
    /// Maps a minimum score (inclusive) to a human-readable band label. Evaluated in order,
    /// first match wins, so entries must stay sorted by descending <c>Min</c>.
    /// </summary>
    public IReadOnlyList<ScoreBandDefinition> ScoreBands { get; init; } =
    [
        new() { Min = 95, Band = "Excellent" },
        new() { Min = 80, Band = "Bon" },
        new() { Min = 60, Band = "Correct" },
        new() { Min = 30, Band = "Faible" },
        new() { Min = 0, Band = "Illisible" }
    ];
}

/// <summary>
/// One page-weight tier: an archive whose dominant format matches <see cref="Format"/> (or any format,
/// when <see cref="Format"/> is <c>null</c>) and whose average page size falls in
/// <c>[MinMb, MaxMb)</c> megabytes scores <see cref="Score"/> points (positive = bonus, negative =
/// penalty, 0 = neutral/ideal).
/// </summary>
public sealed record PageWeightTier
{
    /// <summary>Lowercase image format this tier applies to (e.g. "webp", "jpeg"), or <c>null</c> to serve as the fallback for any format without tiers of its own.</summary>
    public string? Format { get; init; }
    /// <summary>Human-readable label shown in issue messages (e.g. "Ideal", "Excessive").</summary>
    public required string Label { get; init; }
    /// <summary>Inclusive lower bound (in MB) of the average-page-size range this tier covers.</summary>
    public required double MinMb { get; init; }
    /// <summary>Exclusive upper bound (in MB) of the average-page-size range this tier covers. Use <see cref="double.PositiveInfinity"/> for the top-most tier.</summary>
    public required double MaxMb { get; init; }
    /// <summary>Points awarded (positive) or deducted (negative) when a page's average size falls in <c>[MinMb, MaxMb)</c> MB. 0 = neutral/ideal.</summary>
    public required int Score { get; init; }
}

/// <summary>
/// One zip-compression tier: an archive whose compression percentage (bytes saved by deflate, as a
/// percent of uncompressed size) falls in <c>[MinPct, MaxPct]</c> (both inclusive) scores
/// <see cref="Score"/> points (positive = bonus, negative = penalty, 0 = neutral).
/// </summary>
public sealed record ZipCompressionTier
{
    /// <summary>Human-readable label shown in issue messages (e.g. "Uncompressed", "Heavy").</summary>
    public required string Label { get; init; }
    /// <summary>Inclusive lower bound (percent compression saved) of the range this tier covers.</summary>
    public required double MinPct { get; init; }
    /// <summary>Inclusive upper bound (percent compression saved) of the range this tier covers. Use <see cref="double.PositiveInfinity"/> for the top-most tier.</summary>
    public required double MaxPct { get; init; }
    /// <summary>Points awarded (positive) or deducted (negative) when the compression percentage falls in <c>[MinPct, MaxPct]</c>. 0 = neutral.</summary>
    public required int Score { get; init; }
}

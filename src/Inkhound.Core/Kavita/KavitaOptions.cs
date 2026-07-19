using System.Text.Json;
using Foundation.Core.Interface;
using Foundation.Core.Model;
using Inkhound.Core.CbzQuality.Models;

namespace Inkhound.Core.Kavita;

public class KavitaOptions : IOptionList
{
    private static readonly ScoringSettings Defaults = new();

    // JSON (standard, and .NET's JsonSerializer by default) has no representation for double.PositiveInfinity —
    // the top tier of PageWeightTiers/ZipCompressionTiers uses it as "no upper bound". Substitute a very large
    // finite sentinel when serializing: no real average page size / compression percentage will ever reach it,
    // so the tier comparisons (`avgMb < t.MaxMb`) behave identically, and the value round-trips through
    // System.Text.Json and the browser's JSON.parse (which rejects the literal `Infinity` token) without issue.
    private const double UnboundedSentinel = 999_999d;
    private static double SanitizeInfinity(double value) => double.IsPositiveInfinity(value) ? UnboundedSentinel : value;

    private static readonly string DefaultFormatScoreByFormatJson = JsonSerializer.Serialize(
        Defaults.FormatScoreByFormat.Select(kv => new { format = kv.Key, score = kv.Value }));

    private static readonly string DefaultPageWeightTiersJson = JsonSerializer.Serialize(
        Defaults.PageWeightTiers.Select(t => new { format = t.Format, label = t.Label, minMb = t.MinMb, maxMb = SanitizeInfinity(t.MaxMb), score = t.Score }));

    private static readonly string DefaultZipCompressionTiersJson = JsonSerializer.Serialize(
        Defaults.ZipCompressionTiers.Select(t => new { label = t.Label, minPct = t.MinPct, maxPct = SanitizeInfinity(t.MaxPct), score = t.Score }));

    private static readonly string DefaultScoreBandsJson = JsonSerializer.Serialize(
        Defaults.ScoreBands.Select(b => new { min = b.Min, band = b.Band }));

    // --- Connection ---
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string ApiKey { get; set; } = string.Empty;
    public string PluginName { get; set; } = "Inkhound";
    public int TimeoutSeconds { get; set; } = 30;
    public bool UseProxy { get; set; } = false;

    // --- Scoring: Compatibility ---
    public int UnsupportedFormatPenaltyPerFormat { get; set; } = Defaults.UnsupportedFormatPenaltyPerFormat;
    public int UnsupportedFormatPenaltyCap { get; set; } = Defaults.UnsupportedFormatPenaltyCap;
    public int CorruptedImagePenaltyPerImage { get; set; } = Defaults.CorruptedImagePenaltyPerImage;
    public int CorruptedImagePenaltyCap { get; set; } = Defaults.CorruptedImagePenaltyCap;
    public int NoImagesFoundPenalty { get; set; } = Defaults.NoImagesFoundPenalty;
    public int ExtensionMismatchPenaltyPerEntry { get; set; } = Defaults.ExtensionMismatchPenaltyPerEntry;
    public int ExtensionMismatchPenaltyCap { get; set; } = Defaults.ExtensionMismatchPenaltyCap;
    public int NoComicInfoPenalty { get; set; } = Defaults.NoComicInfoPenalty;
    public int ComicInfoNotAtRootPenalty { get; set; } = Defaults.ComicInfoNotAtRootPenalty;
    public int ComicInfoMalformedPenalty { get; set; } = Defaults.ComicInfoMalformedPenalty;
    public int LocalizedSeriesMatchesFilenamePenalty { get; set; } = Defaults.LocalizedSeriesMatchesFilenamePenalty;
    public int PageSortOrderMismatchPenalty { get; set; } = Defaults.PageSortOrderMismatchPenalty;
    public int InconsistentZeroPaddingPenalty { get; set; } = Defaults.InconsistentZeroPaddingPenalty;
    public int PageNumberGapsPenalty { get; set; } = Defaults.PageNumberGapsPenalty;
    public int PageNumberDuplicatesPenalty { get; set; } = Defaults.PageNumberDuplicatesPenalty;
    public int JunkFilePenaltyPerEntry { get; set; } = Defaults.JunkFilePenaltyPerEntry;
    public int JunkFilePenaltyCap { get; set; } = Defaults.JunkFilePenaltyCap;
    public int NestedFolderDepth1Penalty { get; set; } = Defaults.NestedFolderDepth1Penalty;
    public int NestedFolderDepth2PlusPenalty { get; set; } = Defaults.NestedFolderDepth2PlusPenalty;
    public int ExtraneousFilePenaltyPerEntry { get; set; } = Defaults.ExtraneousFilePenaltyPerEntry;
    public int ExtraneousFilePenaltyCap { get; set; } = Defaults.ExtraneousFilePenaltyCap;
    public int ParenthesesInFilenamePenalty { get; set; } = Defaults.ParenthesesInFilenamePenalty;

    // --- Scoring: Resolution ---
    public int ResolutionIdealMin { get; set; } = Defaults.ResolutionIdealMin;
    public int ResolutionIdealMax { get; set; } = Defaults.ResolutionIdealMax;
    public int ResolutionTooLow { get; set; } = Defaults.ResolutionTooLow;
    public int ResolutionTooHigh { get; set; } = Defaults.ResolutionTooHigh;
    public int ResolutionSeverePenaltyPerImage { get; set; } = Defaults.ResolutionSeverePenaltyPerImage;
    public int ResolutionSeverePenaltyCap { get; set; } = Defaults.ResolutionSeverePenaltyCap;
    public int ResolutionMinorPenaltyPerImage { get; set; } = Defaults.ResolutionMinorPenaltyPerImage;
    public int ResolutionMinorPenaltyCap { get; set; } = Defaults.ResolutionMinorPenaltyCap;

    // --- Scoring: Compression quality ---
    public int JpegQualityIdealMin { get; set; } = Defaults.JpegQualityIdealMin;
    public int JpegQualityIdealMax { get; set; } = Defaults.JpegQualityIdealMax;
    public int JpegQualityLow { get; set; } = Defaults.JpegQualityLow;
    public int JpegQualityHigh { get; set; } = Defaults.JpegQualityHigh;
    public double WebpBppIdealMin { get; set; } = Defaults.WebpBppIdealMin;
    public double WebpBppIdealMax { get; set; } = Defaults.WebpBppIdealMax;
    public double WebpBppTooLow { get; set; } = Defaults.WebpBppTooLow;
    public double WebpBppTooHigh { get; set; } = Defaults.WebpBppTooHigh;
    public int QualitySeverePenaltyPerImage { get; set; } = Defaults.QualitySeverePenaltyPerImage;
    public int QualitySeverePenaltyCap { get; set; } = Defaults.QualitySeverePenaltyCap;
    public int QualityMinorPenaltyPerImage { get; set; } = Defaults.QualityMinorPenaltyPerImage;
    public int QualityMinorPenaltyCap { get; set; } = Defaults.QualityMinorPenaltyCap;
    public int WebpLosslessPenaltyPerImage { get; set; } = Defaults.WebpLosslessPenaltyPerImage;
    public int WebpLosslessPenaltyCap { get; set; } = Defaults.WebpLosslessPenaltyCap;

    // --- Scoring: lists (raw JSON, parsed only when materializing a ScoringSettings instance — see
    // KavitaService.BuildScoringSettings). Edited via the "app-tier-list-editor" component on the
    // frontend, never expected to be hand-typed as JSON. ---
    public string FormatScoreByFormatJson { get; set; } = DefaultFormatScoreByFormatJson;
    public string PageWeightTiersJson { get; set; } = DefaultPageWeightTiersJson;
    public string ZipCompressionTiersJson { get; set; } = DefaultZipCompressionTiersJson;
    public string ScoreBandsJson { get; set; } = DefaultScoreBandsJson;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(BaseUrl))
            errors.Add("BaseUrl is required.");

        if (string.IsNullOrWhiteSpace(ApiKey))
            errors.Add("ApiKey is required.");

        if (TimeoutSeconds <= 0)
            errors.Add("TimeoutSeconds must be greater than 0.");

        return errors.Count == 0;
    }

    public List<OptionDefinition> GetOptions()
    {
        const string compat = "Scoring — Compatibility";
        const string resolution = "Scoring — Resolution";
        const string quality = "Scoring — Compression Quality";
        const string lists = "Scoring — Lists";

        return new List<OptionDefinition>
        {
            new OptionDefinition { Name = nameof(BaseUrl), Section = "Connection", SortOrder = 0, Value = BaseUrl, ValueType = EValueType.STRING, DefaultValue = "http://localhost:5000", Description = "Base URL of the local Kavita instance.", Mandatory = true },
            new OptionDefinition { Name = nameof(ApiKey), Section = "Connection", SortOrder = 10, Value = ApiKey, ValueType = EValueType.PASSWORD, DefaultValue = string.Empty, Description = "API key from Kavita → User Settings → API Key.", Mandatory = true },
            new OptionDefinition { Name = nameof(TimeoutSeconds), Section = "Connection", SortOrder = 20, Value = TimeoutSeconds.ToString(), ValueType = EValueType.INT, DefaultValue = "30", Description = "HTTP request timeout in seconds.", Mandatory = false },
            new OptionDefinition { Name = nameof(PluginName), Section = "Integration", SortOrder = 30, Value = PluginName, ValueType = EValueType.STRING, DefaultValue = "Inkhound", Description = "Plugin name sent to Kavita during authentication.", Mandatory = false },
            new OptionDefinition { Name = nameof(UseProxy), Section = "Connection", SortOrder = 40, Value = UseProxy.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "false", Description = "Route HTTP requests through the active Webshare proxy, if available.", Mandatory = false },

            IntOption(nameof(UnsupportedFormatPenaltyPerFormat), compat, 100, UnsupportedFormatPenaltyPerFormat, Defaults.UnsupportedFormatPenaltyPerFormat, "Points deducted per unsupported image format detected (e.g. BMP, TIFF)."),
            IntOption(nameof(UnsupportedFormatPenaltyCap), compat, 110, UnsupportedFormatPenaltyCap, Defaults.UnsupportedFormatPenaltyCap, "Maximum total deduction from unsupported formats."),
            IntOption(nameof(CorruptedImagePenaltyPerImage), compat, 120, CorruptedImagePenaltyPerImage, Defaults.CorruptedImagePenaltyPerImage, "Points deducted per image that failed to decode."),
            IntOption(nameof(CorruptedImagePenaltyCap), compat, 130, CorruptedImagePenaltyCap, Defaults.CorruptedImagePenaltyCap, "Maximum total deduction from corrupted/undecodable images."),
            IntOption(nameof(NoImagesFoundPenalty), compat, 140, NoImagesFoundPenalty, Defaults.NoImagesFoundPenalty, "Full-score deduction when the archive contains no images at all."),
            IntOption(nameof(ExtensionMismatchPenaltyPerEntry), compat, 150, ExtensionMismatchPenaltyPerEntry, Defaults.ExtensionMismatchPenaltyPerEntry, "Points deducted per entry whose extension doesn't match its real format."),
            IntOption(nameof(ExtensionMismatchPenaltyCap), compat, 160, ExtensionMismatchPenaltyCap, Defaults.ExtensionMismatchPenaltyCap, "Maximum total deduction from extension/format mismatches."),
            IntOption(nameof(NoComicInfoPenalty), compat, 170, NoComicInfoPenalty, Defaults.NoComicInfoPenalty, "Points deducted when the archive has no ComicInfo.xml at all."),
            IntOption(nameof(ComicInfoNotAtRootPenalty), compat, 180, ComicInfoNotAtRootPenalty, Defaults.ComicInfoNotAtRootPenalty, "Points deducted when ComicInfo.xml exists but isn't at the archive root."),
            IntOption(nameof(ComicInfoMalformedPenalty), compat, 190, ComicInfoMalformedPenalty, Defaults.ComicInfoMalformedPenalty, "Points deducted when ComicInfo.xml is present at the root but fails to parse."),
            IntOption(nameof(LocalizedSeriesMatchesFilenamePenalty), compat, 200, LocalizedSeriesMatchesFilenamePenalty, Defaults.LocalizedSeriesMatchesFilenamePenalty, "Points deducted when <LocalizedSeries> equals the .cbz filename (Kavita silently skips the file)."),
            IntOption(nameof(PageSortOrderMismatchPenalty), compat, 210, PageSortOrderMismatchPenalty, Defaults.PageSortOrderMismatchPenalty, "Points deducted when lexicographic and natural sort order of pages diverge."),
            IntOption(nameof(InconsistentZeroPaddingPenalty), compat, 220, InconsistentZeroPaddingPenalty, Defaults.InconsistentZeroPaddingPenalty, "Points deducted when page numbers use an inconsistent digit count."),
            IntOption(nameof(PageNumberGapsPenalty), compat, 230, PageNumberGapsPenalty, Defaults.PageNumberGapsPenalty, "Points deducted when gaps are detected in the page numbering sequence."),
            IntOption(nameof(PageNumberDuplicatesPenalty), compat, 240, PageNumberDuplicatesPenalty, Defaults.PageNumberDuplicatesPenalty, "Points deducted when duplicate page numbers are detected."),
            IntOption(nameof(JunkFilePenaltyPerEntry), compat, 250, JunkFilePenaltyPerEntry, Defaults.JunkFilePenaltyPerEntry, "Points deducted per junk file found (Thumbs.db, .DS_Store, __MACOSX, ...)."),
            IntOption(nameof(JunkFilePenaltyCap), compat, 260, JunkFilePenaltyCap, Defaults.JunkFilePenaltyCap, "Maximum total deduction from junk files."),
            IntOption(nameof(NestedFolderDepth1Penalty), compat, 270, NestedFolderDepth1Penalty, Defaults.NestedFolderDepth1Penalty, "Points deducted when pages sit inside a single level of subfolder."),
            IntOption(nameof(NestedFolderDepth2PlusPenalty), compat, 280, NestedFolderDepth2PlusPenalty, Defaults.NestedFolderDepth2PlusPenalty, "Points deducted when pages are nested two or more folder levels deep."),
            IntOption(nameof(ExtraneousFilePenaltyPerEntry), compat, 290, ExtraneousFilePenaltyPerEntry, Defaults.ExtraneousFilePenaltyPerEntry, "Points deducted per non-image, non-junk file present in the archive."),
            IntOption(nameof(ExtraneousFilePenaltyCap), compat, 300, ExtraneousFilePenaltyCap, Defaults.ExtraneousFilePenaltyCap, "Maximum total deduction from extraneous non-image files."),
            IntOption(nameof(ParenthesesInFilenamePenalty), compat, 310, ParenthesesInFilenamePenalty, Defaults.ParenthesesInFilenamePenalty, "Points deducted when the .cbz filename contains parentheses."),

            IntOption(nameof(ResolutionIdealMin), resolution, 400, ResolutionIdealMin, Defaults.ResolutionIdealMin, "Lower bound (px) of the ideal page height band."),
            IntOption(nameof(ResolutionIdealMax), resolution, 410, ResolutionIdealMax, Defaults.ResolutionIdealMax, "Upper bound (px) of the ideal page height band."),
            IntOption(nameof(ResolutionTooLow), resolution, 420, ResolutionTooLow, Defaults.ResolutionTooLow, "Below this height (px), a page is flagged as too-low resolution."),
            IntOption(nameof(ResolutionTooHigh), resolution, 430, ResolutionTooHigh, Defaults.ResolutionTooHigh, "Above this height (px), a page is flagged as too-high resolution."),
            IntOption(nameof(ResolutionSeverePenaltyPerImage), resolution, 440, ResolutionSeverePenaltyPerImage, Defaults.ResolutionSeverePenaltyPerImage, "Points deducted per severely mis-sized page (too low or too high)."),
            IntOption(nameof(ResolutionSeverePenaltyCap), resolution, 450, ResolutionSeverePenaltyCap, Defaults.ResolutionSeverePenaltyCap, "Maximum total deduction from severely mis-sized pages."),
            IntOption(nameof(ResolutionMinorPenaltyPerImage), resolution, 460, ResolutionMinorPenaltyPerImage, Defaults.ResolutionMinorPenaltyPerImage, "Points deducted per page merely below/above the ideal band."),
            IntOption(nameof(ResolutionMinorPenaltyCap), resolution, 470, ResolutionMinorPenaltyCap, Defaults.ResolutionMinorPenaltyCap, "Maximum total deduction from mildly out-of-band pages."),

            IntOption(nameof(JpegQualityIdealMin), quality, 500, JpegQualityIdealMin, Defaults.JpegQualityIdealMin, "Lower bound of the ideal JPEG quality band (0-100)."),
            IntOption(nameof(JpegQualityIdealMax), quality, 510, JpegQualityIdealMax, Defaults.JpegQualityIdealMax, "Upper bound of the ideal JPEG quality band (0-100)."),
            IntOption(nameof(JpegQualityLow), quality, 520, JpegQualityLow, Defaults.JpegQualityLow, "Below this JPEG quality, a page is flagged as too-low quality."),
            IntOption(nameof(JpegQualityHigh), quality, 530, JpegQualityHigh, Defaults.JpegQualityHigh, "Above this JPEG quality, a page is flagged as too-high quality."),
            DoubleOption(nameof(WebpBppIdealMin), quality, 540, WebpBppIdealMin, Defaults.WebpBppIdealMin, "Lower bound of the ideal WebP lossy bits-per-pixel band."),
            DoubleOption(nameof(WebpBppIdealMax), quality, 550, WebpBppIdealMax, Defaults.WebpBppIdealMax, "Upper bound of the ideal WebP lossy bits-per-pixel band."),
            DoubleOption(nameof(WebpBppTooLow), quality, 560, WebpBppTooLow, Defaults.WebpBppTooLow, "Below this bits-per-pixel value, a WebP page is flagged as too-low quality."),
            DoubleOption(nameof(WebpBppTooHigh), quality, 570, WebpBppTooHigh, Defaults.WebpBppTooHigh, "Above this bits-per-pixel value, a WebP page is flagged as too-high quality."),
            IntOption(nameof(QualitySeverePenaltyPerImage), quality, 580, QualitySeverePenaltyPerImage, Defaults.QualitySeverePenaltyPerImage, "Points deducted per page with severely too-low/too-high compression quality."),
            IntOption(nameof(QualitySeverePenaltyCap), quality, 590, QualitySeverePenaltyCap, Defaults.QualitySeverePenaltyCap, "Maximum total deduction from severe compression-quality outliers."),
            IntOption(nameof(QualityMinorPenaltyPerImage), quality, 600, QualityMinorPenaltyPerImage, Defaults.QualityMinorPenaltyPerImage, "Points deducted per page slightly outside the ideal compression-quality band."),
            IntOption(nameof(QualityMinorPenaltyCap), quality, 610, QualityMinorPenaltyCap, Defaults.QualityMinorPenaltyCap, "Maximum total deduction from minor compression-quality deviations."),
            IntOption(nameof(WebpLosslessPenaltyPerImage), quality, 620, WebpLosslessPenaltyPerImage, Defaults.WebpLosslessPenaltyPerImage, "Points deducted per lossless WebP page."),
            IntOption(nameof(WebpLosslessPenaltyCap), quality, 630, WebpLosslessPenaltyCap, Defaults.WebpLosslessPenaltyCap, "Maximum total deduction from lossless WebP pages."),

            new OptionDefinition { Name = nameof(FormatScoreByFormatJson), Section = lists, SortOrder = 900, Value = FormatScoreByFormatJson, ValueType = EValueType.TEXT, DefaultValue = DefaultFormatScoreByFormatJson, Description = "Bonus/penalty by dominant image format (JSON). Edit via the tier editor on the Kavita settings page.", Mandatory = true },
            new OptionDefinition { Name = nameof(PageWeightTiersJson), Section = lists, SortOrder = 910, Value = PageWeightTiersJson, ValueType = EValueType.TEXT, DefaultValue = DefaultPageWeightTiersJson, Description = "Page-weight tiers per image format (JSON). Edit via the tier editor on the Kavita settings page.", Mandatory = true },
            new OptionDefinition { Name = nameof(ZipCompressionTiersJson), Section = lists, SortOrder = 920, Value = ZipCompressionTiersJson, ValueType = EValueType.TEXT, DefaultValue = DefaultZipCompressionTiersJson, Description = "Zip container compression tiers (JSON). Edit via the tier editor on the Kavita settings page.", Mandatory = true },
            new OptionDefinition { Name = nameof(ScoreBandsJson), Section = lists, SortOrder = 930, Value = ScoreBandsJson, ValueType = EValueType.TEXT, DefaultValue = DefaultScoreBandsJson, Description = "Score band labels (JSON). Edit via the tier editor on the Kavita settings page.", Mandatory = true }
        };
    }

    private static OptionDefinition IntOption(string name, string section, int sortOrder, int value, int defaultValue, string description) =>
        new() { Name = name, Section = section, SortOrder = sortOrder, Value = value.ToString(), ValueType = EValueType.INT, DefaultValue = defaultValue.ToString(), Description = description, Mandatory = false };

    private static OptionDefinition DoubleOption(string name, string section, int sortOrder, double value, double defaultValue, string description) =>
        new() { Name = name, Section = section, SortOrder = sortOrder, Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture), ValueType = EValueType.DOUBLE, DefaultValue = defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture), Description = description, Mandatory = false };

    public bool LoadOptions(List<OptionDefinition> options, out List<string> errors)
    {
        errors = new List<string>();

        foreach (var option in options)
        {
            if (option.IsValid(out var optionErrors))
            {
                switch (option.Name)
                {
                    case nameof(BaseUrl): BaseUrl = option.Value; break;
                    case nameof(ApiKey): ApiKey = option.Value; break;
                    case nameof(PluginName): PluginName = option.Value; break;
                    case nameof(TimeoutSeconds): TimeoutSeconds = option.GetInt(); break;
                    case nameof(UseProxy): UseProxy = option.GetBool(); break;

                    case nameof(UnsupportedFormatPenaltyPerFormat): UnsupportedFormatPenaltyPerFormat = option.GetInt(); break;
                    case nameof(UnsupportedFormatPenaltyCap): UnsupportedFormatPenaltyCap = option.GetInt(); break;
                    case nameof(CorruptedImagePenaltyPerImage): CorruptedImagePenaltyPerImage = option.GetInt(); break;
                    case nameof(CorruptedImagePenaltyCap): CorruptedImagePenaltyCap = option.GetInt(); break;
                    case nameof(NoImagesFoundPenalty): NoImagesFoundPenalty = option.GetInt(); break;
                    case nameof(ExtensionMismatchPenaltyPerEntry): ExtensionMismatchPenaltyPerEntry = option.GetInt(); break;
                    case nameof(ExtensionMismatchPenaltyCap): ExtensionMismatchPenaltyCap = option.GetInt(); break;
                    case nameof(NoComicInfoPenalty): NoComicInfoPenalty = option.GetInt(); break;
                    case nameof(ComicInfoNotAtRootPenalty): ComicInfoNotAtRootPenalty = option.GetInt(); break;
                    case nameof(ComicInfoMalformedPenalty): ComicInfoMalformedPenalty = option.GetInt(); break;
                    case nameof(LocalizedSeriesMatchesFilenamePenalty): LocalizedSeriesMatchesFilenamePenalty = option.GetInt(); break;
                    case nameof(PageSortOrderMismatchPenalty): PageSortOrderMismatchPenalty = option.GetInt(); break;
                    case nameof(InconsistentZeroPaddingPenalty): InconsistentZeroPaddingPenalty = option.GetInt(); break;
                    case nameof(PageNumberGapsPenalty): PageNumberGapsPenalty = option.GetInt(); break;
                    case nameof(PageNumberDuplicatesPenalty): PageNumberDuplicatesPenalty = option.GetInt(); break;
                    case nameof(JunkFilePenaltyPerEntry): JunkFilePenaltyPerEntry = option.GetInt(); break;
                    case nameof(JunkFilePenaltyCap): JunkFilePenaltyCap = option.GetInt(); break;
                    case nameof(NestedFolderDepth1Penalty): NestedFolderDepth1Penalty = option.GetInt(); break;
                    case nameof(NestedFolderDepth2PlusPenalty): NestedFolderDepth2PlusPenalty = option.GetInt(); break;
                    case nameof(ExtraneousFilePenaltyPerEntry): ExtraneousFilePenaltyPerEntry = option.GetInt(); break;
                    case nameof(ExtraneousFilePenaltyCap): ExtraneousFilePenaltyCap = option.GetInt(); break;
                    case nameof(ParenthesesInFilenamePenalty): ParenthesesInFilenamePenalty = option.GetInt(); break;

                    case nameof(ResolutionIdealMin): ResolutionIdealMin = option.GetInt(); break;
                    case nameof(ResolutionIdealMax): ResolutionIdealMax = option.GetInt(); break;
                    case nameof(ResolutionTooLow): ResolutionTooLow = option.GetInt(); break;
                    case nameof(ResolutionTooHigh): ResolutionTooHigh = option.GetInt(); break;
                    case nameof(ResolutionSeverePenaltyPerImage): ResolutionSeverePenaltyPerImage = option.GetInt(); break;
                    case nameof(ResolutionSeverePenaltyCap): ResolutionSeverePenaltyCap = option.GetInt(); break;
                    case nameof(ResolutionMinorPenaltyPerImage): ResolutionMinorPenaltyPerImage = option.GetInt(); break;
                    case nameof(ResolutionMinorPenaltyCap): ResolutionMinorPenaltyCap = option.GetInt(); break;

                    case nameof(JpegQualityIdealMin): JpegQualityIdealMin = option.GetInt(); break;
                    case nameof(JpegQualityIdealMax): JpegQualityIdealMax = option.GetInt(); break;
                    case nameof(JpegQualityLow): JpegQualityLow = option.GetInt(); break;
                    case nameof(JpegQualityHigh): JpegQualityHigh = option.GetInt(); break;
                    case nameof(WebpBppIdealMin): WebpBppIdealMin = option.GetDouble(); break;
                    case nameof(WebpBppIdealMax): WebpBppIdealMax = option.GetDouble(); break;
                    case nameof(WebpBppTooLow): WebpBppTooLow = option.GetDouble(); break;
                    case nameof(WebpBppTooHigh): WebpBppTooHigh = option.GetDouble(); break;
                    case nameof(QualitySeverePenaltyPerImage): QualitySeverePenaltyPerImage = option.GetInt(); break;
                    case nameof(QualitySeverePenaltyCap): QualitySeverePenaltyCap = option.GetInt(); break;
                    case nameof(QualityMinorPenaltyPerImage): QualityMinorPenaltyPerImage = option.GetInt(); break;
                    case nameof(QualityMinorPenaltyCap): QualityMinorPenaltyCap = option.GetInt(); break;
                    case nameof(WebpLosslessPenaltyPerImage): WebpLosslessPenaltyPerImage = option.GetInt(); break;
                    case nameof(WebpLosslessPenaltyCap): WebpLosslessPenaltyCap = option.GetInt(); break;

                    case nameof(FormatScoreByFormatJson): FormatScoreByFormatJson = option.Value; break;
                    case nameof(PageWeightTiersJson): PageWeightTiersJson = option.Value; break;
                    case nameof(ZipCompressionTiersJson): ZipCompressionTiersJson = option.Value; break;
                    case nameof(ScoreBandsJson): ScoreBandsJson = option.Value; break;
                }
            }
            else
            {
                errors.AddRange(optionErrors);
            }
        }

        return errors.Count == 0;
    }
}

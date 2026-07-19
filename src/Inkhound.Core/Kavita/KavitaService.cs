using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Foundation.Core;
using Foundation.Core.Model;
using Inkhound.Core.CbzQuality.Models;
using Inkhound.Core.Kavita.Models;

namespace Inkhound.Core.Kavita;

public class KavitaService : BaseService<KavitaOptions>
{
    private HttpClient _http;
    private string? _token;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public KavitaService()
    {
        _http = BuildHttpClient();
    }

    #region Override BaseService

    public override string GetServiceName() => "Kavita";

    public override async Task<bool> LoadOptions(List<OptionDefinition> optionList)
    {
        // Rebuild _http and invalidate cached token when options change
        Options.LoadOptions(optionList, out _);
        _token = null;
        _tokenExpiry = DateTime.MinValue;
        _http = BuildHttpClient();
        return await base.LoadOptions(optionList);
    }

    protected override async Task<EState> CheckInternalState()
    {
        // Force re-authentication to verify the API key is still valid
        _token = null;
        return await AuthenticateAsync() ? EState.OK : EState.ERROR;
    }

    #endregion

    public async Task<List<KavitaLibrary>> GetLibrariesAsync()
    {
        try
        {
            if (!await EnsureTokenAsync()) return [];

            var response = await _http.GetAsync("api/Library/libraries");
            if (!response.IsSuccessStatusCode)
            {
                SendTrace($"GetLibrariesAsync failed with HTTP {(int)response.StatusCode}", ETraceLevel.WARNING);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<List<KavitaLibrary>>(json, JsonOpts);
            return result ?? [];
        }
        catch (Exception ex)
        {
            SendTrace("GetLibrariesAsync error", ex);
            return [];
        }
    }

    public async Task<bool> ScanLibraryAsync(int libraryId, bool force = false)
    {
        try
        {
            if (!await EnsureTokenAsync()) return false;

            var url = $"api/library/scan?libraryId={libraryId}&force={force.ToString().ToLowerInvariant()}";
            var response = await _http.PostAsync(url, content: null);
            if (!response.IsSuccessStatusCode)
            {
                SendTrace($"ScanLibraryAsync failed with HTTP {(int)response.StatusCode} for library {libraryId}", ETraceLevel.WARNING);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            SendTrace($"ScanLibraryAsync error for library {libraryId}", ex);
            return false;
        }
    }

    public async Task<bool> ScanFolderAsync(string folderPath)
    {
        try
        {
            if (!await EnsureTokenAsync()) return false;

            var url = "api/Library/scan-folder";
            var content = new StringContent(
                JsonSerializer.Serialize(new { apiKey = Options.ApiKey, folderPath }),
                Encoding.UTF8,
                "application/json");
            var response = await _http.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                SendTrace($"ScanFolderAsync failed with HTTP {(int)response.StatusCode} for folder {folderPath}: {body}", ETraceLevel.WARNING);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            SendTrace($"ScanFolderAsync error for folder {folderPath}", ex);
            return false;
        }
    }

    /// <summary>
    /// Materializes a full <see cref="ScoringSettings"/> instance from the flat/JSON fields stored on
    /// <see cref="KavitaOptions"/> — used by the CBZ "Analyze" job. Falls back to a field's own default
    /// (fresh <see cref="ScoringSettings"/>) if a stored list-shaped JSON value fails to parse.
    /// </summary>
    public ScoringSettings BuildScoringSettings()
    {
        var d = new ScoringSettings();
        return new ScoringSettings
        {
            UnsupportedFormatPenaltyPerFormat = Options.UnsupportedFormatPenaltyPerFormat,
            UnsupportedFormatPenaltyCap = Options.UnsupportedFormatPenaltyCap,
            CorruptedImagePenaltyPerImage = Options.CorruptedImagePenaltyPerImage,
            CorruptedImagePenaltyCap = Options.CorruptedImagePenaltyCap,
            NoImagesFoundPenalty = Options.NoImagesFoundPenalty,
            ExtensionMismatchPenaltyPerEntry = Options.ExtensionMismatchPenaltyPerEntry,
            ExtensionMismatchPenaltyCap = Options.ExtensionMismatchPenaltyCap,
            NoComicInfoPenalty = Options.NoComicInfoPenalty,
            ComicInfoNotAtRootPenalty = Options.ComicInfoNotAtRootPenalty,
            ComicInfoMalformedPenalty = Options.ComicInfoMalformedPenalty,
            LocalizedSeriesMatchesFilenamePenalty = Options.LocalizedSeriesMatchesFilenamePenalty,
            PageSortOrderMismatchPenalty = Options.PageSortOrderMismatchPenalty,
            InconsistentZeroPaddingPenalty = Options.InconsistentZeroPaddingPenalty,
            PageNumberGapsPenalty = Options.PageNumberGapsPenalty,
            PageNumberDuplicatesPenalty = Options.PageNumberDuplicatesPenalty,
            JunkFilePenaltyPerEntry = Options.JunkFilePenaltyPerEntry,
            JunkFilePenaltyCap = Options.JunkFilePenaltyCap,
            NestedFolderDepth1Penalty = Options.NestedFolderDepth1Penalty,
            NestedFolderDepth2PlusPenalty = Options.NestedFolderDepth2PlusPenalty,
            ExtraneousFilePenaltyPerEntry = Options.ExtraneousFilePenaltyPerEntry,
            ExtraneousFilePenaltyCap = Options.ExtraneousFilePenaltyCap,
            ParenthesesInFilenamePenalty = Options.ParenthesesInFilenamePenalty,

            ResolutionIdealMin = Options.ResolutionIdealMin,
            ResolutionIdealMax = Options.ResolutionIdealMax,
            ResolutionTooLow = Options.ResolutionTooLow,
            ResolutionTooHigh = Options.ResolutionTooHigh,
            ResolutionSeverePenaltyPerImage = Options.ResolutionSeverePenaltyPerImage,
            ResolutionSeverePenaltyCap = Options.ResolutionSeverePenaltyCap,
            ResolutionMinorPenaltyPerImage = Options.ResolutionMinorPenaltyPerImage,
            ResolutionMinorPenaltyCap = Options.ResolutionMinorPenaltyCap,

            JpegQualityIdealMin = Options.JpegQualityIdealMin,
            JpegQualityIdealMax = Options.JpegQualityIdealMax,
            JpegQualityLow = Options.JpegQualityLow,
            JpegQualityHigh = Options.JpegQualityHigh,
            WebpBppIdealMin = Options.WebpBppIdealMin,
            WebpBppIdealMax = Options.WebpBppIdealMax,
            WebpBppTooLow = Options.WebpBppTooLow,
            WebpBppTooHigh = Options.WebpBppTooHigh,
            QualitySeverePenaltyPerImage = Options.QualitySeverePenaltyPerImage,
            QualitySeverePenaltyCap = Options.QualitySeverePenaltyCap,
            QualityMinorPenaltyPerImage = Options.QualityMinorPenaltyPerImage,
            QualityMinorPenaltyCap = Options.QualityMinorPenaltyCap,
            WebpLosslessPenaltyPerImage = Options.WebpLosslessPenaltyPerImage,
            WebpLosslessPenaltyCap = Options.WebpLosslessPenaltyCap,

            FormatScoreByFormat = TryParseScoringList<List<FormatScoreRow>>(Options.FormatScoreByFormatJson)
                ?.ToDictionary(r => r.Format, r => r.Score, StringComparer.OrdinalIgnoreCase) ?? d.FormatScoreByFormat,
            PageWeightTiers = TryParseScoringList<List<PageWeightTier>>(Options.PageWeightTiersJson) ?? d.PageWeightTiers,
            ZipCompressionTiers = TryParseScoringList<List<ZipCompressionTier>>(Options.ZipCompressionTiersJson) ?? d.ZipCompressionTiers,
            ScoreBands = TryParseScoringList<List<ScoreBandDefinition>>(Options.ScoreBandsJson) ?? d.ScoreBands
        };
    }

    private T? TryParseScoringList<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            SendTrace("Failed to parse a stored scoring JSON field — falling back to its default.", ex);
            return null;
        }
    }

    private sealed record FormatScoreRow(string Format, int Score);

    // Returns true if token is valid, false if authentication failed
    private async Task<bool> EnsureTokenAsync()
    {
        if (_token is not null && DateTime.UtcNow < _tokenExpiry)
            return true;

        return await AuthenticateAsync();
    }

    private async Task<bool> AuthenticateAsync()
    {
        try
        {
            var url = $"api/Plugin/authenticate?apiKey={Uri.EscapeDataString(Options.ApiKey)}&pluginName={Uri.EscapeDataString(Options.PluginName)}";
            var response = await _http.PostAsync(url, content: null);

            if (!response.IsSuccessStatusCode)
            {
                SendTrace($"Kavita authentication failed with HTTP {(int)response.StatusCode}", ETraceLevel.WARNING);
                _token = null;
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("token", out var tokenElement))
            {
                SendTrace($"Kavita authentication response missing 'token'. Raw: {json}", ETraceLevel.WARNING);
                _token = null;
                return false;
            }

            _token = tokenElement.GetString();
            if (string.IsNullOrEmpty(_token))
            {
                SendTrace("Kavita returned an empty token", ETraceLevel.WARNING);
                return false;
            }

            _tokenExpiry = ParseJwtExpiry(_token) - TimeSpan.FromMinutes(5);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            SendTrace($"Kavita authenticated, token valid until {_tokenExpiry:u}", ETraceLevel.DEBUG);
            return true;
        }
        catch (Exception ex)
        {
            SendTrace("Kavita authentication error", ex);
            _token = null;
            return false;
        }
    }

    // Decodes the JWT payload and extracts the 'exp' Unix timestamp
    private static DateTime ParseJwtExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return DateTime.UtcNow.AddHours(23);

            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            payload = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("exp", out var exp))
                return DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()).UtcDateTime;
        }
        catch { }

        return DateTime.UtcNow.AddHours(23);
    }

    private HttpClient BuildHttpClient()
    {
        var baseUrl = Options.BaseUrl.TrimEnd('/') + '/';
        return new HttpClient(CreateHttpHandler(Options.UseProxy))
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(Options.TimeoutSeconds > 0 ? Options.TimeoutSeconds : 30)
        };
    }
}

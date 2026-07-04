using System.Text.Json;
using System.Text.RegularExpressions;
using Foundation.Core;
using Foundation.Core.Model;

namespace Inkhound.Core.QBittorrent;

public class QBittorrentService : BaseService<QBittorrentOptions>
{
    private HttpClient _http;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex MagnetHashRegex =
        new(@"urn:btih:([0-9a-fA-F]{40})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public QBittorrentService()
    {
        _http = BuildHttpClient();
    }

    #region Override BaseService

    public override string GetServiceName() => "QBittorrent";

    public override async Task<bool> LoadOptions(List<OptionDefinition> optionList)
    {
        Options.LoadOptions(optionList, out _);
        _http = BuildHttpClient();
        return await base.LoadOptions(optionList);
    }

    protected override async Task<EState> CheckInternalState()
    {
        try
        {
            var response = await _http.GetAsync("api/v2/app/version");
            return response.IsSuccessStatusCode ? EState.OK : EState.ERROR;
        }
        catch (Exception ex)
        {
            SendTrace("QBittorrent health check failed", ex);
            return EState.ERROR;
        }
    }

    #endregion

    public QBittorrentGrabParameters GetGrabParameters() => new(
        string.IsNullOrEmpty(Options.DefaultCategory) ? null : Options.DefaultCategory,
        string.IsNullOrEmpty(Options.DefaultSavePath) ? null : Options.DefaultSavePath,
        Options.AddPaused);

    public async Task<List<QBittorrentCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("api/v2/torrents/categories", ct);
            if (!response.IsSuccessStatusCode)
            {
                SendTrace($"GetCategoriesAsync failed with HTTP {(int)response.StatusCode}", ETraceLevel.WARNING);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var dict = JsonSerializer.Deserialize<Dictionary<string, QBittorrentCategory>>(json, JsonOpts);
            return dict?.Values.Where(c => !string.IsNullOrEmpty(c.Name)).ToList() ?? [];
        }
        catch (Exception ex)
        {
            SendTrace("GetCategoriesAsync error", ex);
            return [];
        }
    }

    public async Task<string?> AddTorrentAsync(
        string url,
        string? category = null,
        string? savePath = null,
        bool paused = false,
        CancellationToken ct = default)
    {
        try
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(url), "urls");
            form.Add(new StringContent(paused ? "true" : "false"), "paused");
            if (!string.IsNullOrEmpty(category))
                form.Add(new StringContent(category), "category");
            if (!string.IsNullOrEmpty(savePath))
                form.Add(new StringContent(savePath), "savepath");

            var response = await _http.PostAsync("api/v2/torrents/add", form, ct);
            if (!response.IsSuccessStatusCode)
            {
                SendTrace($"AddTorrentAsync failed with HTTP {(int)response.StatusCode}", ETraceLevel.WARNING);
                return null;
            }

            var text = (await response.Content.ReadAsStringAsync(ct)).Trim();
            if (!IsAddSuccess(text))
            {
                SendTrace($"AddTorrentAsync unexpected response: {text}", ETraceLevel.WARNING);
                return null;
            }

            if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                return ExtractMagnetHash(url);

            // Pour les URLs HTTP .torrent : on identifie le torrent ajouté par son timestamp.
            // On ne peut pas faire FirstOrDefault() car d'autres torrents récents
            // (ajoutés lors d'une session précédente) seraient retournés à la place.
            var addedAfter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 5; // -5 s pour décalage d'horloge
            await Task.Delay(1500, ct);
            var recent = await GetTorrentsAsync(null, ct);
            var added = recent.FirstOrDefault(t => t.AddedOn >= addedAfter);
            return added?.Hash ?? recent.FirstOrDefault()?.Hash; // fallback défensif
        }
        catch (Exception ex)
        {
            SendTrace("AddTorrentAsync error", ex);
            return null;
        }
    }

    public async Task<List<QBittorrentTorrent>> GetTorrentsAsync(
        IEnumerable<string>? hashes = null,
        CancellationToken ct = default)
    {
        try
        {
            var hashList = hashes?.Where(h => !string.IsNullOrEmpty(h)).ToList() ?? [];
            var url = hashList.Count > 0
                ? $"api/v2/torrents/info?hashes={Uri.EscapeDataString(string.Join("|", hashList))}"
                : "api/v2/torrents/info?sort=added_on&reverse=true&limit=10";

            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                SendTrace($"GetTorrentsAsync failed with HTTP {(int)response.StatusCode}", ETraceLevel.WARNING);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<List<QBittorrentTorrent>>(json, JsonOpts) ?? [];
        }
        catch (Exception ex)
        {
            SendTrace("GetTorrentsAsync error", ex);
            return [];
        }
    }

    public async Task<List<QBittorrentTorrentFile>> GetTorrentFilesAsync(
        string hash,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"api/v2/torrents/files?hash={Uri.EscapeDataString(hash)}";
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                SendTrace($"GetTorrentFilesAsync failed with HTTP {(int)response.StatusCode}", ETraceLevel.WARNING);
                return [];
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<List<QBittorrentTorrentFile>>(json, JsonOpts) ?? [];
        }
        catch (Exception ex)
        {
            SendTrace("GetTorrentFilesAsync error", ex);
            return [];
        }
    }

    public async Task<bool> PauseTorrentAsync(string hash, CancellationToken ct = default)
    {
        try
        {
            var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("hashes", hash)]);
            var response = await _http.PostAsync("api/v2/torrents/pause", form, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            SendTrace("PauseTorrentAsync error", ex);
            return false;
        }
    }

    public async Task<bool> ResumeTorrentAsync(string hash, CancellationToken ct = default)
    {
        try
        {
            var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("hashes", hash)]);
            var response = await _http.PostAsync("api/v2/torrents/resume", form, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            SendTrace("ResumeTorrentAsync error", ex);
            return false;
        }
    }

    public async Task<bool> SetFilePrioritiesAsync(
        string hash,
        IEnumerable<int> fileIds,
        int priority,
        CancellationToken ct = default)
    {
        try
        {
            var form = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("hash", hash),
                new KeyValuePair<string, string>("id", string.Join("|", fileIds)),
                new KeyValuePair<string, string>("priority", priority.ToString())
            ]);
            var response = await _http.PostAsync("api/v2/torrents/filePrio", form, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            SendTrace("SetFilePrioritiesAsync error", ex);
            return false;
        }
    }

    private static bool IsAddSuccess(string responseText)
    {
        // API v1/ancienne version : texte "Ok."
        if (responseText.Equals("Ok.", StringComparison.OrdinalIgnoreCase)) return true;
        // API v2/nouvelle version : JSON { "failure_count": 0, "pending_count": 1, ... }
        if (responseText.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(responseText);
                if (doc.RootElement.TryGetProperty("failure_count", out var failCount))
                    return failCount.GetInt32() == 0;
            }
            catch { /* JSON malformé — on rejette */ }
        }
        return false;
    }

    public static string? ExtractMagnetHash(string magnetUrl)
    {
        var match = MagnetHashRegex.Match(magnetUrl);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    private HttpClient BuildHttpClient()
    {
        var baseUrl = Options.BaseUrl.TrimEnd('/') + '/';
        var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(Options.TimeoutSeconds > 0 ? Options.TimeoutSeconds : 30)
        };

        if (!string.IsNullOrEmpty(Options.ApiKey))
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {Options.ApiKey}");

        return client;
    }
}

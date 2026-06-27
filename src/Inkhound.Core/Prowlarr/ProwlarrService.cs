using System.Text;
using System.Text.Json;
using Foundation.Core;
using Foundation.Core.Model;

namespace Inkhound.Core.Prowlarr;

public class ProwlarrService : BaseService<ProwlarrOptions>
{
    private HttpClient _http;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // Wrapper pour désérialiser la réponse paginée de l'historique Prowlarr
    private record ProwlarrHistoryPage(List<ProwlarrHistoryItem> Records);

    public ProwlarrService()
    {
        _http = BuildHttpClient();
    }

    #region Override BaseService

    public override string GetServiceName() => "Prowlarr";

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
            var response = await _http.GetAsync("api/v1/indexer");
            return response.IsSuccessStatusCode ? EState.OK : EState.ERROR;
        }
        catch (Exception ex)
        {
            SendTrace("Prowlarr health check failed", ex);
            return EState.ERROR;
        }
    }

    #endregion

    public async Task<List<ProwlarrIndexer>> GetIndexersAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("api/v1/indexer", ct);
            if (!response.IsSuccessStatusCode)
            {
                SendTrace($"GetIndexersAsync failed with HTTP {(int)response.StatusCode}", ETraceLevel.WARNING);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<List<ProwlarrIndexer>>(json, JsonOpts) ?? [];
        }
        catch (Exception ex)
        {
            SendTrace("GetIndexersAsync error", ex);
            return [];
        }
    }

    public async Task<List<ProwlarrSearchResult>> SearchAsync(
        string query,
        int[]? indexerIds = null,
        int[]? categories = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = new StringBuilder($"api/v1/search?query={Uri.EscapeDataString(query)}");
            if (indexerIds is { Length: > 0 })
            {
                foreach (var id in indexerIds)
                    url.Append($"&indexerIds={id}");
            }
            if (categories is { Length: > 0 })
            {
                foreach (var cat in categories)
                    url.Append($"&categories={cat}");
            }

            var response = await _http.GetAsync(url.ToString(), ct);
            if (!response.IsSuccessStatusCode)
            {
                SendTrace($"SearchAsync failed with HTTP {(int)response.StatusCode} for query '{query}'", ETraceLevel.WARNING);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<List<ProwlarrSearchResult>>(json, JsonOpts) ?? [];
        }
        catch (Exception ex)
        {
            SendTrace($"SearchAsync error for query '{query}'", ex);
            return [];
        }
    }

    public async Task<bool> GrabReleaseAsync(
        string guid,
        int indexerId,
        int? downloadClientId = null,
        CancellationToken ct = default)
    {
        try
        {
            var grab = new ProwlarrGrab(guid, indexerId, downloadClientId);
            var content = new StringContent(
                JsonSerializer.Serialize(grab, JsonOpts),
                Encoding.UTF8,
                "application/json");

            var response = await _http.PostAsync("api/v1/search", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                SendTrace($"GrabReleaseAsync failed with HTTP {(int)response.StatusCode} for guid={guid}", ETraceLevel.WARNING);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            SendTrace($"GrabReleaseAsync error for guid={guid}", ex);
            return false;
        }
    }

    public async Task<List<ProwlarrHistoryItem>> GetHistoryAsync(
        int limit = 20,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"api/v1/history?pageSize={limit}&eventType=grabbed", ct);
            if (!response.IsSuccessStatusCode)
            {
                SendTrace($"GetHistoryAsync failed with HTTP {(int)response.StatusCode}", ETraceLevel.WARNING);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var page = JsonSerializer.Deserialize<ProwlarrHistoryPage>(json, JsonOpts);
            return page?.Records ?? [];
        }
        catch (Exception ex)
        {
            SendTrace("GetHistoryAsync error", ex);
            return [];
        }
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
            client.DefaultRequestHeaders.Add("X-Api-Key", Options.ApiKey);

        return client;
    }
}

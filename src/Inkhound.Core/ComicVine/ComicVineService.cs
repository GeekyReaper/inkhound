using System.Net.Http.Json;
using System.Text.Json;
using Inkhound.Core.Models;
using Inkhound.Core.Interface;

namespace Inkhound.Core.ComicVine;

public sealed class ComicVineService : IinkhoundService
{
    private const string VolumePrefix = "4050";
    private const string IssuePrefix = "4000";
    private const int MaxPageSize = 100;

    private static readonly string VolumeFieldList =
        "id,name,start_year,count_of_issues,publisher,image,deck,description,api_detail_url,site_detail_url";

    private static readonly string IssueListFieldList =
        "id,name,issue_number,volume,cover_date,store_date,image,api_detail_url,site_detail_url";

    private static readonly string IssueDetailFieldList =
        "id,name,issue_number,volume,cover_date,store_date,description,image,api_detail_url,site_detail_url,person_credits";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public static string ServiceName = "ComicVine";

    private readonly HttpClient _http;
    private readonly ComicVineOptions _options;

    public ComicVineService(ComicVineOptions options)
    {

        _options = options;
        _http = new HttpClient
        {
            BaseAddress = new Uri(_options.BaseUrl),
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds)
        };
    }

    public bool Initialize(out List<string> errors)
    {

        if (_options != null && _options.IsValid(out errors))
        {
            return true;
        }
        errors = ["Invalid options provided for ComicVineService."];
        return false;
    }

    public static async Task<(bool IsValid, List<string> Errors)> CheckOptionsAsync(
        List<OptionDefinition> optionDefinitions,
        CancellationToken ct = default)
    {
        var options = ComicVineOptions.SetOptions(optionDefinitions);
        if (options == null)
            return (false, ["Failed to set options."]);

        if (!options.IsValid(out var localErrors))
            return (false, localErrors);

        using var http = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };

        var url = $"volumes/?api_key={options.ApiKey}&format=json&limit=1&field_list=id";
        var response = await http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            return (false, [$"ComicVine API unreachable: HTTP {(int)response.StatusCode}"]);

        var body = await response.Content.ReadFromJsonAsync<CvStatusResponse>(JsonOpts, ct);
        if (body is null || body.StatusCode != 1)
            return (false, [$"ComicVine API error: {body?.Error ?? "empty response"}"]);

        return (true, []);
    }

    private record CvStatusResponse(int StatusCode, string Error);

    private const string PublisherPrefix = "4010";

    private static readonly string PublisherFieldList =
        "id,name,image,deck,description,location_city,location_state,api_detail_url,site_detail_url";

    // Mapping from enum to ComicVine API field names
    private static readonly Dictionary<VolumeSortField, string> VolumeSortFieldNames = new()
    {
        [VolumeSortField.Name] = "name",
        [VolumeSortField.StartYear] = "start_year",
        [VolumeSortField.CountOfIssues] = "count_of_issues",
        [VolumeSortField.DateAdded] = "date_added",
        [VolumeSortField.DateLastUpdated] = "date_last_updated"
    };

    private static readonly Dictionary<PublisherSortField, string> PublisherSortFieldNames = new()
    {
        [PublisherSortField.Name] = "name",
        [PublisherSortField.DateAdded] = "date_added",
        [PublisherSortField.DateLastUpdated] = "date_last_updated"
    };

    // Search volumes by name (paged), with optional sort
    public Task<CvPagedResponse<CvVolume>> SearchVolumesAsync(
        string query,
        int page = 1,
        int? limit = null,
        VolumeSortField? sortField = null,
        SortDirection sortDir = SortDirection.Asc,
        CancellationToken ct = default)
    {
        string? sort = sortField.HasValue
            ? $"{VolumeSortFieldNames[sortField.Value]}:{(sortDir == SortDirection.Asc ? "asc" : "desc")}"
            : null;


        var offset = (page - 1) * (limit ?? _options.PageSize);

        var url = ListUrl("volumes", VolumeFieldList, limit ?? _options.PageSize, offset,
            $"name:{Uri.EscapeDataString(query)}", sort);
        return GetPagedAsync<CvVolume>(url, ct);
    }

    // Search publishers by name (paged), with optional sort
    public Task<CvPagedResponse<CvPublisherDetail>> SearchPublishersAsync(
        string query,
        int page = 1,
        int? limit = null,
        PublisherSortField? sortField = null,
        SortDirection sortDir = SortDirection.Asc,
        CancellationToken ct = default)
    {
        string? sort = sortField.HasValue
            ? $"{PublisherSortFieldNames[sortField.Value]}:{(sortDir == SortDirection.Asc ? "asc" : "desc")}"
            : null;

        var effectiveLimit = limit ?? _options.PageSize;
        var offset = (page - 1) * effectiveLimit;

        var url = ListUrl("publishers", PublisherFieldList, effectiveLimit, offset,
            $"name:{Uri.EscapeDataString(query)}", sort);
        return GetPagedAsync<CvPublisherDetail>(url, ct);
    }

    // Get a single page of publishers without name filter — lists all
    public Task<CvPagedResponse<CvPublisherDetail>> GetPublishersPageAsync(
        int page = 1,
        int? limit = null,
        PublisherSortField? sortField = null,
        SortDirection sortDir = SortDirection.Asc,
        CancellationToken ct = default)
    {
        string? sort = sortField.HasValue
            ? $"{PublisherSortFieldNames[sortField.Value]}:{(sortDir == SortDirection.Asc ? "asc" : "desc")}"
            : null;

        var effectiveLimit = limit ?? _options.PageSize;
        var offset = (page - 1) * effectiveLimit;

        var url = ListUrl("publishers", PublisherFieldList, effectiveLimit, offset, sort: sort);
        return GetPagedAsync<CvPublisherDetail>(url, ct);
    }

    // Get ALL publishers — auto-paginates and merges all pages
    public async Task<IReadOnlyList<CvPublisherDetail>> GetAllPublishersAsync(
        PublisherSortField? sortField = null,
        SortDirection sortDir = SortDirection.Asc,
        CancellationToken ct = default)
    {
        var all = new List<CvPublisherDetail>();
        var currentPage = 1;
        while (true)
        {
            var response = await GetPublishersPageAsync(currentPage, MaxPageSize, sortField, sortDir, ct);
            all.AddRange(response.Results);
            if (all.Count >= response.NumberOfTotalResults) break;
            currentPage++;
            await Task.Delay(250, ct); // stay within ComicVine rate limit (200 req/hour)
        }
        return all.AsReadOnly();
    }

    // Get single publisher detail by ComicVine numeric ID
    public async Task<CvPublisherDetail?> GetPublisherAsync(int comicVineId, CancellationToken ct = default)
    {
        var url = DetailUrl("publisher", PublisherPrefix, comicVineId, PublisherFieldList);
        var r = await GetAsync<CvDetailResponse<CvPublisherDetail>>(url, ct);
        return r?.Results;
    }

    // Get single volume detail by ComicVine numeric ID
    public async Task<CvVolume?> GetVolumeAsync(int comicVineId, CancellationToken ct = default)
    {
        var url = DetailUrl("volume", VolumePrefix, comicVineId, VolumeFieldList);
        var r = await GetAsync<CvDetailResponse<CvVolume>>(url, ct);
        return r?.Results;
    }

    // Get ALL issues for a volume — auto-paginates and merges all pages
    public async Task<IReadOnlyList<CvIssue>> GetAllIssuesForVolumeAsync(
        int comicVineVolumeId, CancellationToken ct = default)
    {
        var all = new List<CvIssue>();
        var currentPage = 1;
        while (true)
        {
            var response = await GetIssuesPageAsync(comicVineVolumeId, currentPage, MaxPageSize, ct);
            all.AddRange(response.Results);
            if (all.Count >= response.NumberOfTotalResults) break;
            currentPage++;
            await Task.Delay(250, ct); // stay within ComicVine rate limit (200 req/hour)
        }
        return all.AsReadOnly();
    }

    // Get a single page of issues (for explicit pagination control)
    public Task<CvPagedResponse<CvIssue>> GetIssuesPageAsync(
        int comicVineVolumeId, int page = 1, int? limit = null, CancellationToken ct = default)
    {
        var effectiveLimit = limit ?? _options.PageSize;
        var offset = (page - 1) * effectiveLimit;

        var url = ListUrl("issues", IssueListFieldList, effectiveLimit, offset,
            $"volume:{comicVineVolumeId}");
        return GetPagedAsync<CvIssue>(url, ct);
    }

    // Get single issue detail — includes person_credits, excluded from list calls
    public async Task<CvIssue?> GetIssueAsync(int comicVineIssueId, CancellationToken ct = default)
    {
        var url = DetailUrl("issue", IssuePrefix, comicVineIssueId, IssueDetailFieldList);
        var r = await GetAsync<CvDetailResponse<CvIssue>>(url, ct);
        return r?.Results;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private string ListUrl(string resource, string fields, int limit, int offset,
        string? filter = null, string? sort = null)
    {
        var url = $"{resource}/?api_key={_options.ApiKey}&format=json" +
                  $"&field_list={fields}&limit={Math.Clamp(limit, 1, MaxPageSize)}&offset={offset}";
        if (filter is not null) url += $"&filter={filter}";
        if (sort is not null) url += $"&sort={sort}";
        return url;
    }

    private string DetailUrl(string resource, string prefix, int id, string fields) =>
        $"{resource}/{prefix}-{id}/?api_key={_options.ApiKey}&format=json&field_list={fields}";

    private async Task<CvPagedResponse<T>> GetPagedAsync<T>(string url, CancellationToken ct)
    {
        var result = await GetAsync<CvPagedResponse<T>>(url, ct)
            ?? throw new InvalidOperationException($"ComicVine returned null body for: {url}");

        if (result.StatusCode != 1)
            throw new InvalidOperationException(
                $"ComicVine API error {result.StatusCode}: {result.Error}");

        return result;
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOpts, ct);
    }
}

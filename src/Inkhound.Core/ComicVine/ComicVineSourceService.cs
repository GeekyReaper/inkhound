using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inkhound.Core.Models;
using Inkhound.Core.Sources;
using Foundation.Core;
using Foundation.Core.Interface;
using Foundation.Core.Model;
using System.Runtime.CompilerServices;
using System.Resources;
using System.Net;
using System.Data;
using System.Diagnostics;
using System.Threading.Channels;

namespace Inkhound.Core.ComicVine;


public enum ELevelDetail { ID, SUMMARY, FULL }

public class ComicVineSourceService : BaseService<ComicVineOptions>, ISourceService
{
    public string SourceKey => "comicvine";


    private const string VolumePrefix = "4050";
    private const string IssuePrefix = "4000";
    private const int MaxPageSize = 100;

    private static readonly string VolumeSearchFieldList = "id,name,start_year,count_of_issues,description,publisher,image,first_issue,last_issue,site_detail_url";

    private static readonly string VolumeDetailFieldList =
        "id,name,count_of_issues,date_added,date_last_updated,deck,description,image,issues,people,publisher,site_detail_url,start_year";



    private static readonly Dictionary<ELevelDetail, string> IssueFieldListByLevelDetail = new()
    {
        [ELevelDetail.ID] = "id, name,issue_number",
        [ELevelDetail.SUMMARY] = "id,name,issue_number,cover_date,description,image,site_detail_url",
        [ELevelDetail.FULL] = "id,name,issue_number,volume,cover_date,store_date,description,image,api_detail_url,site_detail_url,person_credits"
    };


    private static readonly string PublisherFieldList =
        "id,name,image,deck,description,location_city,location_state,api_detail_url,site_detail_url";
    private HttpClient _http;
    private RateLimiter _rateLimiter = null!;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Converters = { new ComicVineDateTimeConverter() }
    };

    // ComicVine returns dates as "yyyy-MM-dd HH:mm:ss" (space, not T)
    private sealed class ComicVineDateTimeConverter : JsonConverter<DateTime>
    {
        private const string Format = "yyyy-MM-dd HH:mm:ss";
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => DateTime.ParseExact(reader.GetString()!, Format, CultureInfo.InvariantCulture);
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString(Format));
    }



    public ComicVineSourceService()
    {
        _http = BuildHttpClient();
        _rateLimiter = new RateLimiter(Options.RateLimitMs);
    }

    #region Override BaseService

    public override string GetServiceName() => "ComicVine";
    public override async Task<bool> LoadOptions(List<OptionDefinition> optionList)
    {
        Options.LoadOptions(optionList, out _);
        _http = BuildHttpClient();
        var old = _rateLimiter;
        _rateLimiter = new RateLimiter(Options.RateLimitMs);
        old.Dispose();
        return await base.LoadOptions(optionList);
    }

    protected override async Task<EState> CheckInternalState()
    {
        var url = $"volumes/?api_key={Options.ApiKey}&format=json&limit=1&field_list=id";
        try
        {
            var body = await GetAsync<CvStatusResponse>(url, CancellationToken.None);
            if (body is null || body.StatusCode != 1)
                return EState.ERROR;
        }
        catch (Exception ex)
        {
            SendTrace("Request to ComicVine failed", ex);
            return EState.ERROR;
        }

        return EState.OK;
    }

    #endregion

    private HttpClient BuildHttpClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(Options.BaseUrl),
            Timeout = TimeSpan.FromSeconds(Options.TimeoutSeconds)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Options.UserAgent);
        return client;
    }
    private record CvStatusResponse(int StatusCode, string Error);

    private const string PublisherPrefix = "4010";



    #region API Mapping

    private static readonly Dictionary<PublisherSortField, string> PublisherSortFieldNames = new()
    {
        [PublisherSortField.Name] = "name",
        [PublisherSortField.DateAdded] = "date_added",
        [PublisherSortField.DateLastUpdated] = "date_last_updated"
    };

    // Search volumes by name (paged) via /search endpoint
    public Task<CvPagedResponse<CvVolumeStub>> SearchVolumesByNameAsync(
        string query,
        int page = 1,
        int? limit = null,
        CancellationToken ct = default)
    {
        var url = SearchUrl(query, "volume", VolumeSearchFieldList, limit ?? Options.PageSize, page);
        return GetPagedAsync<CvVolumeStub>(url, ct);
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

        var effectiveLimit = limit ?? Options.PageSize;
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

        var effectiveLimit = limit ?? Options.PageSize;
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
        var url = DetailUrl("volume", VolumePrefix, comicVineId, VolumeDetailFieldList);
        var r = await GetAsync<CvDetailResponse<CvVolume>>(url, ct);
        return r?.Results;
    }



    // Get ALL issues for a volume — paginates with minimal fields then fetches full detail per issue
    public async Task<IReadOnlyList<CvIssue>> GetAllIssuesForVolumeAsync(
        int comicVineVolumeId, ELevelDetail detail = ELevelDetail.ID, CancellationToken ct = default)
    {
        // Phase 1: collect all issue IDs with minimal fields to stay under rate limit

        var ids = new List<int>();
        var all = new List<CvIssue>();
        var offset = 0;
        while (true)
        {
            var url = ListUrl("issues", IssueFieldListByLevelDetail[detail == ELevelDetail.FULL ? ELevelDetail.ID : detail], MaxPageSize, offset, $"volume:{comicVineVolumeId}");
            var response = await GetPagedAsync<CvIssue>(url, ct);
            ids.AddRange(response.Results.Select(i => i.Id));
            all.AddRange(response.Results);
            if (ids.Count >= response.NumberOfTotalResults) break;
            SendTrace($"Fetched {ids.Count}/{response.NumberOfTotalResults} issues for volume {comicVineVolumeId}", ETraceLevel.INFO);
            offset += response.Results.Count;
        }
        if (detail != ELevelDetail.FULL)
            return all.AsReadOnly();

        // Phase 2: fetch full detail (including person_credits) for each issue
        all.Clear(); // Phase 1 stubs only had id — discard them
        foreach (var id in ids)
        {
            Debug.WriteLine($"[ComicVine] Fetching issue id={id}");
            var issue = await GetIssueAsync(id, ct);
            SendTrace($"Fetched issue id={id}", ETraceLevel.INFO);
            Debug.WriteLine($"[ComicVine] Fetching issue id={id} DONE");
            if (issue is not null)
                all.Add(issue);
            await Task.Delay(500, ct);
        }
        return all.AsReadOnly();
    }

    // Get a single page of issues (for explicit pagination control)
    public Task<CvPagedResponse<CvIssue>> GetIssuesPageAsync(
        int comicVineVolumeId, int page = 1, int? limit = null, ELevelDetail detail = ELevelDetail.ID, CancellationToken ct = default)
    {
        var effectiveLimit = limit ?? Options.PageSize;
        var offset = (page - 1) * effectiveLimit;

        var url = ListUrl("issues", IssueFieldListByLevelDetail[detail], effectiveLimit, offset,
            $"volume:{comicVineVolumeId}");
        return GetPagedAsync<CvIssue>(url, ct);
    }

    // Get single issue detail — includes person_credits, excluded from list calls
    public async Task<CvIssue?> GetIssueAsync(int comicVineIssueId, CancellationToken ct = default)
    {
        var url = DetailUrl("issue", IssuePrefix, comicVineIssueId, IssueFieldListByLevelDetail[ELevelDetail.FULL]);
        var r = await GetAsync<CvDetailResponse<CvIssue>>(url, ct);
        return r?.Results;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    #endregion

    #region ISourceService

    async Task<Page<SourceVolume>> ISourceService.SearchVolumesByNameAsync(
        string query, int page, int? limit, CancellationToken ct)
    {
        var response = await SearchVolumesByNameAsync(query, page, limit, ct);
        return new Page<SourceVolume>
        {
            Items = response.Results.Select(ToSourceVolume).ToList(),
            PageNumber = page,
            PageSize = response.Limit,
            TotalItems = response.NumberOfTotalResults,
        };
    }

    async Task<SourceVolume?> ISourceService.GetVolumeAsync(string sourceVolumeId, CancellationToken ct)
    {
        if (!int.TryParse(sourceVolumeId, out var id)) return null;
        var v = await GetVolumeAsync(id, ct);
        return v is null ? null : ToSourceVolume(v);
    }

    async Task<Page<SourceIssue>> ISourceService.GetIssuesPageAsync(
        string sourceVolumeId, int page, int? limit, CancellationToken ct)
    {
        if (!int.TryParse(sourceVolumeId, out var id)) return new Page<SourceIssue>();
        var response = await GetIssuesPageAsync(id, page, limit, ELevelDetail.SUMMARY, ct);
        return new Page<SourceIssue>
        {
            Items = response.Results.Select(ToSourceIssue).ToList(),
            PageNumber = page,
            PageSize = response.Limit,
            TotalItems = response.NumberOfTotalResults,
        };
    }

    async Task<IReadOnlyList<SourceIssue>> ISourceService.GetAllIssuesForVolumeAsync(
        string sourceVolumeId, CancellationToken ct)
    {
        if (!int.TryParse(sourceVolumeId, out var id)) return [];
        var issues = await GetAllIssuesForVolumeAsync(id, ELevelDetail.FULL, ct);
        return issues.Select(ToSourceIssue).ToList();
    }

    async Task<SourceIssue?> ISourceService.GetIssueAsync(string sourceIssueId, CancellationToken ct)
    {
        if (!int.TryParse(sourceIssueId, out var id)) return null;
        var issue = await GetIssueAsync(id, ct);
        return issue is null ? null : ToSourceIssue(issue);
    }

    private static SourceVolume ToSourceVolume(CvVolumeStub v) =>
        new(v.Id.ToString(), v.Name, ParseYear(v.StartYear), v.CountOfIssues, v.Publisher?.Name, v.Description, v.Image?.SmallUrl);

    private static SourceVolume ToSourceVolume(CvVolume v) =>
        new(v.Id.ToString(), v.Name, ParseYear(v.StartYear), v.CountOfIssues, v.Publisher?.Name, v.Description, v.Image?.SmallUrl);

    private static SourceIssue ToSourceIssue(CvIssue i) =>
        new(i.Id.ToString(), i.Name, i.IssueNumber, ParseCoverDate(i.CoverDate), i.Description, i.Image?.SmallUrl);

    private static int? ParseYear(string? s) => int.TryParse(s, out var y) ? y : null;

    private static DateTime? ParseCoverDate(string? s) => DateTime.TryParse(s, CultureInfo.InvariantCulture, out var d) ? d : null;

    #endregion

    private string SearchUrl(string query, string resources, string fields, int limit, int page) =>
        $"search/?api_key={Options.ApiKey}&format=json&query={Uri.EscapeDataString(query)}" +
        $"&resources={resources}&field_list={fields}&limit={Math.Clamp(limit, 1, MaxPageSize)}&page={page}";

    private string ListUrl(string resource, string fields, int limit, int offset,
        string? filter = null, string? sort = null)
    {
        var url = $"{resource}/?api_key={Options.ApiKey}&format=json" +
                  $"&field_list={fields}&limit={Math.Clamp(limit, 1, MaxPageSize)}&offset={offset}";
        if (filter is not null) url += $"&filter={filter}";
        if (sort is not null) url += $"&sort={sort}";
        return url;
    }

    private string DetailUrl(string resource, string prefix, int id, string fields) =>
        $"{resource}/{prefix}-{id}/?api_key={Options.ApiKey}&format=json&field_list={fields}";

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
        var http = _http;
        var task = _rateLimiter.EnqueueAsync(async consumerCt =>
        {
            var response = await http.GetAsync(url, consumerCt);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(consumerCt);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        });
        return await task.WaitAsync(ct);
    }
}

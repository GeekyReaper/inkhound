using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Inkhound.Core.Models;
using Foundation.Core;
using Foundation.Core.Interface;
using Foundation.Core.Model;
using System.Runtime.CompilerServices;
using System.Resources;

namespace Inkhound.Core.ComicVine;

public partial class ComicVineService : BaseService<ComicVineOptions>
{
    private const string VolumePrefix = "4050";
    private const string IssuePrefix = "4000";
    private const int MaxPageSize = 100;

    private static readonly string VolumeFieldList =
        "id,name,start_year,count_of_issues,publisher,image,deck,description,api_detail_url,site_detail_url,person_credits,team_credits,character_credits,concept_credits,location_credits,object_credits";

    private static readonly string IssueListFieldList =
        "id,name,issue_number,volume,cover_date,store_date,image,api_detail_url,site_detail_url";

    private static readonly string IssueDetailFieldList =
        "id,name,issue_number,volume,cover_date,store_date,description,image,api_detail_url,site_detail_url,person_credits";

    private HttpClient _http;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };



    public ComicVineService()
    {
        _http = BuildHttpClient();
    }

    #region Override BaseService

    public override string GetServiceName() => "ComicVine";
    public override async Task<bool> LoadOptions(List<OptionDefinition> optionList)
    {
        // Rebuild _http before base.LoadOptions so CheckInternalState uses the updated options
        Options.LoadOptions(optionList, out _);
        _http = BuildHttpClient();
        return await base.LoadOptions(optionList);
    }

    protected override async Task<EState> CheckInternalState()
    {
        var url = $"volumes/?api_key={Options.ApiKey}&format=json&limit=1&field_list=id";
        try
        {
            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return (EState.ERROR);

            var body = await response.Content.ReadFromJsonAsync<CvStatusResponse>(JsonOpts);
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


        var offset = (page - 1) * (limit ?? Options.PageSize);

        var url = ListUrl("volumes", VolumeFieldList, limit ?? Options.PageSize, offset,
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
        var effectiveLimit = limit ?? Options.PageSize;
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
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    public async Task<CvFindResult> FindVolume(string issueFilename, string favoriteCountryCode,
        CancellationToken ct = default)
    {
        var parts = issueFilename.Replace('\\', '/').Split('/', 2);
        var folderName = parts.Length == 2 ? parts[0] : Path.GetFileNameWithoutExtension(parts[0]);
        var fileName = parts.Length == 2 ? Path.GetFileNameWithoutExtension(parts[1]) : folderName;

        var (title, year) = ParseFolderName(folderName);
        var issueNum = ParseIssueNumber(fileName);
        var normalizedTitle = NormalizeForSearch(title);

        var searchResult = await SearchVolumesAsync(title, limit: 20, ct: ct);
        if (searchResult.Results.Count == 0)
            return new CvFindResult(null, null);

        var bestVolume = searchResult.Results
            .Select(v => (Volume: v, Score: ScoreVolume(v, normalizedTitle, year, issueNum, favoriteCountryCode)))
            .MaxBy(x => x.Score)
            .Volume;

        if (issueNum is null)
            return new CvFindResult(bestVolume, null);

        // Find the matching issue — paginate if needed
        var page = 1;
        while (true)
        {
            var issuePage = await GetIssuesPageAsync(bestVolume.Id, page, MaxPageSize, ct);
            var match = issuePage.Results.FirstOrDefault(
                i => int.TryParse(i.IssueNumber, out var n) && n == issueNum);

            if (match is not null)
                return new CvFindResult(bestVolume, match);

            if (issuePage.Results.Count + issuePage.Offset >= issuePage.NumberOfTotalResults)
                break;

            page++;
            await Task.Delay(250, ct);
        }

        return new CvFindResult(bestVolume, null);
    }

    // ── FindVolumeByName ──────────────────────────────────────────────────────
    public async Task<CvVolume?> FindVolumeByName(string volumeName, string favoriteCountryCode,
        CancellationToken ct = default)
    {
        var candidates = ExtractVolumeCandidates(volumeName)
            .Where(c => !string.IsNullOrWhiteSpace(c.Title))
            .ToList();

        if (candidates.Count == 0)
            return null;

        // 1. Search in parallel — one query per candidate
        var searchTasks = candidates.Select(c => SearchVolumesAsync(c.Title.Trim(), limit: 20, ct: ct));
        var searchResults = await Task.WhenAll(searchTasks);

        // 2. Deduplicate by volume ID — for volumes found by multiple candidates,
        //    keep the candidate whose title best matches the volume name
        var bestCandidatePerVolume = candidates
            .Zip(searchResults, (candidate, result) => (candidate, result.Results))
            .SelectMany(x => x.Results.Select(v => (Volume: v, Candidate: x.candidate)))
            .GroupBy(x => x.Volume.Id)
            .Select(g => g.MaxBy(x =>
                NormalizeForSearch(x.Volume.Name).Contains(NormalizeForSearch(x.Candidate.Title)) ? 1 : 0))
            .ToList();

        // 3. Fetch full details sequentially to respect ComicVine rate limit (250ms between calls)
        var detailedVolumes = new List<(CvVolume Volume, ParsedVolumeName Candidate)>();
        for (var i = 0; i < bestCandidatePerVolume.Count; i++)
        {
            var item = bestCandidatePerVolume[i];
            var detail = await GetVolumeAsync(item.Volume.Id, ct);
            if (detail is not null)
                detailedVolumes.Add((detail, item.Candidate));
            if (i < bestCandidatePerVolume.Count - 1)
                await Task.Delay(250, ct);
        }

        if (detailedVolumes.Count == 0)
            return null;

        // 4. Score each detailed volume against its best candidate
        return detailedVolumes
            .Select(x =>
            {
                var normalizedTitle = NormalizeForSearch(x.Candidate.Title);
                var minCount = Math.Max(x.Candidate.MinTomes ?? 0, x.Candidate.IssueNumber ?? 0);
                var score = ScoreVolume(x.Volume, normalizedTitle, x.Candidate.Year, minCount,
                    favoriteCountryCode, x.Candidate.metadata);
                return (x.Volume, score);
            })
            .MaxBy(x => x.score)
            .Volume;
    }

    // ── FindVolume helpers ─────────────────────────────────────────────────────
    private static readonly Dictionary<string, string[]> PublisherCountryHints =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["FR"] = ["dargaud", "dupuis", "casterman", "glenat", "soleil", "lombard",
                      "delcourt", "ankama", "fluide", "bamboo", "vents d'ouest"],
            ["US"] = ["marvel", "dc comics", "image", "dark horse", "idw", "dynamite", "boom", "archie"],
            ["JP"] = ["shueisha", "kodansha", "shogakukan", "viz"],
        };

    #region REGEX
    [GeneratedRegex(@"^(.+?)\s*\((\d{4})\)\s*$")]
    private static partial Regex FolderYearRegex();
    [GeneratedRegex(@"\s*(\d{4})\s*")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\b[T,t,V,v][omevlu]*[\s\.\-_]*0*(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TomeNumberRegex();

    [GeneratedRegex(@"^0*(\d+)")]
    private static partial Regex LeadingNumberRegex();

    [GeneratedRegex(@"[-–]\s*0*(\d{1,4})\s*[-–]")]
    private static partial Regex DashEnclosedNumberRegex();

    [GeneratedRegex(@"\b0*(\d{1,4})\b")]
    private static partial Regex IsolatedNumberRegex();

    [GeneratedRegex(@"[\[{][^\[\]{}]*[\]}]")]
    private static partial Regex BracketTagRegex();

    [GeneratedRegex(@"\(\s*(\d+)\s*tomes?\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex TomesCountRegex();

    [GeneratedRegex(@"\(\s*(\d{4})\s*\)")]
    private static partial Regex YearParenRegex();
    #endregion

    public record ParsedVolumeName(string Title, int? Year, int? MinTomes, int? IssueNumber, List<string>? metadata);

    private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CBR", "CBZ", "PDF", "Ebook", "epub", "NoTag", "NoTAG", "NOTAG",
        "FR", "FRENCH", "EN", "ENGLISH", "VF", "VO",
        "BD", "INTEGRALE", "COLLECTION", "HS", "MANGA",
    };
    private static readonly HashSet<string> SplitString = new(StringComparer.OrdinalIgnoreCase)
    {
        "-", ".", "+", ","
    };

    private static string StripNoiseWords(IEnumerable<string> noiseWords, string input)
    {
        var pattern = $@"\b({string.Join("|", noiseWords.Select(Regex.Escape))})\b";
        var result = Regex.Replace(input, pattern, " ", RegexOptions.IgnoreCase);
        return string.Join(" ", result.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static List<ParsedVolumeName> ExtractVolumeCandidates(string input)
    {
        var result = new List<ParsedVolumeName>();
        var s = input.Trim();
        // 1. Strip bracket tags: [BD], [cbr], [aATAa], {CBR & CBZ], etc.
        s = BracketTagRegex().Replace(s, " ");

        s = StripNoiseWords(NoiseWords, s);



        //s = StripNoiseWords2(s);

        // 2. Extract (N Tomes) → minTomes
        int? minTomes = null;
        var tomesMatch = TomesCountRegex().Match(s);
        if (tomesMatch.Success && int.TryParse(tomesMatch.Groups[1].Value, out var tc))
        {
            minTomes = tc;
            s = s.Remove(tomesMatch.Index, tomesMatch.Length);
        }

        // 3. Extract standalone (YYYY) → year
        int? year = null;
        var yearMatch = YearParenRegex().Match(s);
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var y))
        {
            year = y;
            s = s.Remove(yearMatch.Index, yearMatch.Length);
        }


        // Clean split carateres

        var charClass = "[" + string.Concat(SplitString.Select(Regex.Escape)) + "]";

        s = Regex.Replace(s, $@"({charClass}\s*){{2,}}", "");  // consécutifs
        s = Regex.Replace(s, $@"{charClass}\s*$", "");           // en fin
        s = Regex.Replace(s, $@"^\s*{charClass}", "");           // en début

        // s = Regex.Replace(s, @"(\.\s*){2}", "");
        // s = Regex.Replace(s, @"(\.\s*)$", "");
        // s = Regex.Replace(s, @"(^\s*\.)", "");

        // s = Regex.Replace(s, @"(\-\s*){2}", "");
        // s = Regex.Replace(s, @"(\-\s*)$", "");
        // s = Regex.Replace(s, @"(^\s*\- )", "");

        // s = Regex.Replace(s, @"(\+\s*){2}", "");
        // s = Regex.Replace(s, @"(\+\s*)$", "");
        // s = Regex.Replace(s, @"(^\+\s*)", "");

        result.Add(new ParsedVolumeName(s.Trim(), year, minTomes, 0, null));

        // Find split character
        foreach (var t in SplitString)
        {


            var segment = s.Split(t);
            if (segment != null && segment.Count() > 1)
            {
                var titles = new List<string>();
                foreach (var seg in segment)
                {
                    if (!string.IsNullOrEmpty(seg.Trim()))
                    {
                        var tomematch = TomeNumberRegex().Match(seg);
                        var yearmatch = YearRegex().Match(seg);
                        if (tomematch.Success && int.TryParse(tomematch.Groups[1].Value, out var tomeNumber))
                        {
                            minTomes = minTomes == null ? tomeNumber : minTomes > tomeNumber ? minTomes : tomeNumber;
                        }
                        else if (yearmatch.Success && int.TryParse(yearmatch.Groups[1].Value, out var tomyear))
                        {
                            year = year == null ? tomyear : (year < tomyear) ? year : tomyear;
                        }
                        else
                        {
                            titles.Add(seg);
                        }
                    }

                }
                foreach (var title in titles)
                {
                    result.Add(new ParsedVolumeName(title, year, minTomes, 0, titles));
                }
            }
        }
        return result;
    }
    private static (string Title, int? Year) ParseFolderName(string folder)
    {
        var match = FolderYearRegex().Match(folder.Trim());
        if (match.Success && int.TryParse(match.Groups[2].Value, out var year))
            return (match.Groups[1].Value.Trim(), year);
        return (folder.Trim(), null);
    }

    private static int? ParseIssueNumber(string filename)
    {
        // 1. French "Tome" format: T01, T 02
        var m = TomeNumberRegex().Match(filename);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) return n;

        // 2. Starts with a number: "01 - ", "002 "
        m = LeadingNumberRegex().Match(filename);
        if (m.Success && int.TryParse(m.Groups[1].Value, out n)) return n;

        // 3. Number enclosed in dashes: " - 012 - "
        m = DashEnclosedNumberRegex().Match(filename);
        if (m.Success && int.TryParse(m.Groups[1].Value, out n)) return n;

        // 4. First isolated integer
        m = IsolatedNumberRegex().Match(filename);
        if (m.Success && int.TryParse(m.Groups[1].Value, out n)) return n;

        return null;
    }

    private static double ScoreVolume(CvVolume candidate, string normalizedQuery,
        int? year, int? issueNum, string countryCode, List<string>? metadata = null)
    {
        double score = 0;

        // Title similarity (0–60)
        var candidateNorm = NormalizeForSearch(candidate.Name);
        if (candidateNorm == normalizedQuery)
            score += 60;
        else if (candidateNorm.Contains(normalizedQuery) || normalizedQuery.Contains(candidateNorm))
            score += 40;
        else
            score += Math.Max(0, 30 - LevenshteinDistance(candidateNorm, normalizedQuery));

        // Year match (0–15)
        if (year.HasValue && candidate.StartYear == year.Value.ToString())
            score += 15;

        // Issue count coverage (−20 to +15)
        if (issueNum.HasValue)
        {
            if (candidate.CountOfIssues >= issueNum.Value)
                score += 15;
            else
                score -= 20;
        }

        // Publisher country (0–10)
        if (!string.IsNullOrEmpty(candidate.Publisher?.Name)
            && PublisherCountryHints.TryGetValue(countryCode, out var hints))
        {
            var pub = candidate.Publisher.Name.ToLowerInvariant();
            if (hints.Any(pub.Contains))
                score += 10;
        }

        return score;
    }

    private static string NormalizeForSearch(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }

        return d[a.Length, b.Length];
    }
}

using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Inkhound.Core.Models;
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

public partial class ComicVineService : BaseService<ComicVineOptions>
{
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



    public ComicVineService()
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
    private record CvLinkCandidateVolume(CvVolume volume, List<ParsedVolumeName> candidates);


    public async Task<CvFindResult> FindVolume(string issueFilename, string favoriteCountryCode, CvVolume? cvVolume = null,
        CancellationToken ct = default)
    {
        var parts = issueFilename.Replace('\\', '/').Split('/', 2);
        var folderName = parts.Length == 2 ? parts[0] : Path.GetFileNameWithoutExtension(parts[0]);
        var fileName = parts.Length == 2 ? Path.GetFileNameWithoutExtension(parts[1]) : folderName;

        var issueNum = ParseIssueNumber(fileName);
        var normalizedTitle = NormalizeForSearch(folderName);

        if (cvVolume == null)
        {
            cvVolume = await AutomaticSearchVolume(folderName, favoriteCountryCode, ct: ct);
            if (cvVolume == null)
                return new CvFindResult(null, null);
        }

        if (issueNum is null)
            return new CvFindResult(cvVolume, null);

        // Find the matching issue — paginate if needed
        var page = 1;
        while (true)
        {
            var issuePage = await GetIssuesPageAsync(cvVolume.Id, page, MaxPageSize, ELevelDetail.SUMMARY, ct);
            var match = issuePage.Results.FirstOrDefault(
                i => int.TryParse(i.IssueNumber, out var n) && n == issueNum);

            if (match is not null)
            {
                // Get dIssues DEtails
                var issue = await GetIssueAsync(match.Id, ct);
                return new CvFindResult(cvVolume, issue);
            }

            if (issuePage.Results.Count + issuePage.Offset >= issuePage.NumberOfTotalResults)
                break;

            page++;
        }

        return new CvFindResult(cvVolume, null);
    }

    // ── FindVolumeByName ──────────────────────────────────────────────────────
    public async Task<CvVolume?> AutomaticSearchVolume(string volumeName, string favoriteCountryCode, ProgressionCallback? progression = null,
        CancellationToken ct = default)
    {


        // Calculate all candidates
        var candidates = ExtractVolumeNameCandidates(volumeName);
        SendTrace($"Volume name canditates {candidates.Count}", ETraceLevel.DEBUG);
        if (candidates.Count == 0)
            return null;

        // 2. Search and get Volume details
        var result = new Dictionary<int, CvLinkCandidateVolume>();
        foreach (var candidate in candidates)
        {
            var resultpage = await SearchVolumesByNameAsync(candidate.Title, limit: 20, ct: ct); // Take only first page

            SendTrace($"Search for {candidate.Title}  and found {resultpage.Results.Count} results", ETraceLevel.DEBUG);
            foreach (var item in resultpage.Results)
            {
                if (result.ContainsKey(item.Id))
                {
                    result[item.Id].candidates.Add(candidate);
                }
                else
                {
                    var v = await GetVolumeAsync(item.Id, ct);
                    if (v != null)
                    {
                        result.Add(item.Id, new CvLinkCandidateVolume(v, candidates = [candidate]));
                    }
                }
            }
        }

        SendTrace($"{result.Count} volumes could be candidate", ETraceLevel.DEBUG);

        if (result.Count == 0)
            return null;

        if (result.Count == 1)
            return result.First().Value.volume;


        double bestScore = 0;
        double maxScore = 100;
        CvVolume? BestVolume = null;
        // 3. Calculate scoring
        foreach (var item in result)
        {
            foreach (var candidate in item.Value.candidates)
            {
                var score = ScoreVolume(item.Value.volume, candidate, favoriteCountryCode);
                if (score > bestScore)
                {
                    bestScore = score;
                    BestVolume = item.Value.volume;
                    SendTrace($"Update best score {bestScore} for volume {item.Value.volume.Name}", ETraceLevel.DEBUG);
                }
            }
            if (bestScore > maxScore)
            {
                return BestVolume;
            }
        }
        return BestVolume;

    }

    // ── FindVolume helpers ─────────────────────────────────────────────────────
    private static readonly Dictionary<string, string[]> PublisherCountryHints =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["FR"] = ["dargaud", "dupuis", "casterman", "glenat", "soleil", "lombard",
                      "delcourt", "ankama", "fluide", "bamboo", "vents d'ouest", "hachette"],
            ["US"] = ["marvel", "dc comics", "image", "dark horse", "idw", "dynamite", "boom", "archie"],
            ["JP"] = ["shueisha", "kodansha", "shogakukan", "viz"],
        };

    #region REGEX
    [GeneratedRegex(@"\s*(\d{4})\s*")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"#\s*0*(\d+)")]
    private static partial Regex HashNumberRegex();

    [GeneratedRegex(@"\b[T,t,V,v][omevlu]*[\s\.\-_]*0*(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TomeNumberRegex();

    [GeneratedRegex(@"^0*(\d+)")]
    private static partial Regex LeadingNumberRegex();

    [GeneratedRegex(@"[-–_]\s*0*(\d{1,4})\s*[-–_]")]
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

    public static List<ParsedVolumeName> ExtractVolumeNameCandidates(string input)
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

        s = s.Trim();

        var alltitles = new List<string>();

        // Find split character
        foreach (var t in SplitString)
        {
            var segment = s.Split(t, StringSplitOptions.RemoveEmptyEntries & StringSplitOptions.TrimEntries);
            if (segment != null && segment.Count() > 1)
            {
                var titles = new List<string>();
                foreach (var seg in segment)
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
                foreach (var title in titles)
                {
                    result.Add(new ParsedVolumeName(title, year, minTomes, 0, titles));
                }

                result.Add(new ParsedVolumeName(string.Join(" ", titles), year, minTomes, 0, null));
            }
        }
        if (result.Count == 0)
        {
            result.Add(new ParsedVolumeName(s, year, minTomes, 0, null));
        }
        return result;
    }


    public static int? ParseIssueNumber(string filename)
    {
        var name = Path.GetFileNameWithoutExtension(filename);

        // 1. Hash format: "Batman #012"
        var m = HashNumberRegex().Match(name);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) return n;

        // 2. French "Tome" format: T01, T 02
        m = TomeNumberRegex().Match(name);
        if (m.Success && int.TryParse(m.Groups[1].Value, out n)) return n;

        // 3. Starts with a number: "01 - ", "002 "
        m = LeadingNumberRegex().Match(name);
        if (m.Success && int.TryParse(m.Groups[1].Value, out n)) return n;

        // 4. Number enclosed in dashes: " - 012 - "
        m = DashEnclosedNumberRegex().Match(name);
        if (m.Success && int.TryParse(m.Groups[1].Value, out n)) return n;

        // 5. First isolated integer, excluding years (1800–2099)
        return IsolatedNumberRegex().Matches(name)
            .Select(x => int.TryParse(x.Groups[1].Value, out var v) ? v : (int?)null)
            .FirstOrDefault(v => v.HasValue && !(v >= 1800 && v <= 2099));
    }

    private static double ScoreVolume(CvVolume volume, ParsedVolumeName candidate, string countryCode)
    {
        double score = 0;

        // Title similarity (0–60)
        var volumeNameNorm = NormalizeForSearch(volume.Name);
        var candidateNameNorm = NormalizeForSearch(candidate.Title);

        if (volumeNameNorm == candidateNameNorm)
            score += 60;
        else if (volumeNameNorm.Contains(candidateNameNorm) || candidateNameNorm.Contains(volumeNameNorm))
            score += 40;
        else
            score += Math.Max(0, 30 - LevenshteinDistance(volumeNameNorm, candidateNameNorm));

        // Year match (0–15)
        if (candidate.Year.HasValue && volume.StartYear == candidate.Year.Value.ToString())
            score += 15;

        // Issue count coverage (−20 to +15)
        if (candidate.IssueNumber.HasValue)
        {
            if (volume.CountOfIssues >= candidate.IssueNumber.Value)
                score += 15;
            else
                score -= 20;
        }

        // Publisher country (0–10)
        if (!string.IsNullOrEmpty(volume.Publisher?.Name)
            && PublisherCountryHints.TryGetValue(countryCode, out var hints))
        {
            var pub = volume.Publisher.Name.ToLowerInvariant();
            if (hints.Any(pub.Contains))
                score += 40;
        }



        if (candidate.metadata != null && candidate.metadata.Count > 0)
        {
            foreach (var meta in candidate.metadata)
            {
                var m = NormalizeForSearch(meta);
                var publisher = volume.Publisher == null ? "" : NormalizeForSearch(volume.Publisher.Name);
                if (!string.IsNullOrEmpty(publisher) && (m.Contains(publisher) || publisher.Contains(m)))
                    score += 15;
                if (volume.People != null)
                {
                    foreach (var p in volume.People?.Select(p => p.Name))
                    {
                        if (!string.IsNullOrEmpty(p))
                        {
                            var pnorm = NormalizeForSearch(p);
                            if (pnorm.Contains(m) || m.Contains(pnorm))
                                score += 5;
                        }
                    }
                }
            }
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

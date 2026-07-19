using System.Net.Http.Headers;
using System.Text.Json;
using Foundation.Core;
using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.WebshareProxy;

public class WebshareProxyService : BaseService<WebshareProxyOptions>, IProxyProviderService
{
    private HttpClient _http;
    private readonly List<ProxyInfo> _proxies = [];
    private int _currentIndex = -1;
    private readonly object _lock = new();

    public WebshareProxyService()
    {
        _http = BuildHttpClient();
        // Recharger toute la liste paginée à chaque tick de monitoring (30s par défaut) serait excessif
        // pour un compte payant — on se contente d'un rafraîchissement toutes les 5 minutes.
        StateRefreshDelay = TimeSpan.FromMinutes(5);
    }

    #region Override BaseService

    public override string GetServiceName() => "WebshareProxy";

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
            var count = await LoadProxiesAsync(CancellationToken.None);
            return count > 0 ? EState.OK : EState.ERROR;
        }
        catch (Exception ex)
        {
            SendTrace("Failed to load Webshare proxy list", ex);
            return EState.ERROR;
        }
    }

    #endregion

    private HttpClient BuildHttpClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(Options.BaseUrl.TrimEnd('/') + '/'),
            Timeout = TimeSpan.FromSeconds(Options.TimeoutSeconds)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", Options.ApiKey);
        return client;
    }

    // ── Properties ──────────────────────────────────────────────────────────

    public IReadOnlyList<ProxyInfo> Proxies => _proxies.AsReadOnly();

    public int TotalCount => _proxies.Count;
    public int AvailableCount => _proxies.Count(p => p.Valid && !p.Failed);

    public ProxyInfo? CurrentProxy
    {
        get { lock (_lock) return _currentIndex >= 0 && _currentIndex < _proxies.Count ? _proxies[_currentIndex] : null; }
    }

    // ── Loading from the Webshare API ──────────────────────────────────────

    // Recharge la liste complète des proxys (suit la pagination). Préserve le proxy actif s'il est toujours
    // présent et valide dans la liste rafraîchie ; sinon en sélectionne un nouveau au hasard.
    private async Task<int> LoadProxiesAsync(CancellationToken ct)
    {
        var loaded = new List<ProxyInfo>();

        string? nextUrl = "proxy/list/?mode=direct&page_size=100";
        while (nextUrl is not null)
        {
            using var response = await _http.GetAsync(nextUrl, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            foreach (var item in root.GetProperty("results").EnumerateArray())
            {
                var address = item.GetProperty("proxy_address").GetString() ?? "";
                if (string.IsNullOrEmpty(address)) continue;

                loaded.Add(new ProxyInfo
                {
                    Address = address,
                    Port = item.GetProperty("port").GetInt32(),
                    Username = item.GetProperty("username").GetString() ?? "",
                    Password = item.GetProperty("password").GetString() ?? "",
                    CountryCode = item.TryGetProperty("country_code", out var cc) && cc.ValueKind == JsonValueKind.String
                        ? cc.GetString() : null,
                    Valid = item.TryGetProperty("valid", out var v) && v.GetBoolean(),
                });
            }

            nextUrl = root.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString() : null;
        }

        lock (_lock)
        {
            var previousActive = _currentIndex >= 0 && _currentIndex < _proxies.Count ? _proxies[_currentIndex] : null;

            _proxies.Clear();
            _proxies.AddRange(loaded);

            _currentIndex = previousActive is not null
                ? _proxies.FindIndex(p => p.Address == previousActive.Address && p.Port == previousActive.Port && p.Valid && !p.Failed)
                : -1;

            if (_currentIndex < 0)
                SelectRandomInternal();
        }

        return _proxies.Count;
    }

    // À appeler sous _lock uniquement.
    private void SelectRandomInternal()
    {
        var available = _proxies.Where(p => p.Valid && !p.Failed).ToList();
        if (available.Count == 0)
        {
            _currentIndex = -1;
            return;
        }
        var chosen = available[Random.Shared.Next(available.Count)];
        _currentIndex = _proxies.IndexOf(chosen);
    }

    // ── Rotation ────────────────────────────────────────────────────────────

    // Rotation manuelle, sans marquer le proxy courant comme défaillant.
    public ProxyInfo? NextProxy()
    {
        lock (_lock)
        {
            var available = _proxies.Where(p => p.Valid && !p.Failed).ToList();
            if (available.Count == 0) return null;

            var current = _currentIndex >= 0 && _currentIndex < _proxies.Count ? _proxies[_currentIndex] : null;
            var currentPos = current is not null ? available.IndexOf(current) : -1;
            var next = available[(currentPos + 1) % available.Count];
            _currentIndex = _proxies.IndexOf(next);
            return next;
        }
    }

    // Remet à zéro le flag Failed sur tous les proxys (pour réessayer après un délai).
    public void ResetFailures()
    {
        lock (_lock)
        {
            foreach (var p in _proxies)
                p.Failed = false;
        }
    }

    // Diagnostic : IP externe vue par les serveurs distants à travers le proxy actif.
    public async Task<string?> GetExternalIpAsync(CancellationToken ct = default)
    {
        var proxy = CurrentProxy;
        if (proxy is null) return null;

        var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = proxy.ToProxyEndpoint().ToWebProxy(),
        };
        using var tempClient = new HttpClient(handler);
        try
        {
            var ip = await tempClient.GetStringAsync("https://api.ipify.org?format=text", ct);
            return ip.Trim();
        }
        catch
        {
            return null;
        }
    }

    // ── Usage statistics ────────────────────────────────────────────────────

    public async Task<WebshareStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        var tProfile = _http.GetStringAsync("profile/", ct);
        var tSubscription = _http.GetStringAsync("subscription/", ct);
        var tPlan = _http.GetStringAsync("subscription/plan/", ct);

        await Task.WhenAll(tProfile, tSubscription, tPlan);

        using var docProfile = JsonDocument.Parse(await tProfile);
        using var docSub = JsonDocument.Parse(await tSubscription);
        using var docPlan = JsonDocument.Parse(await tPlan);

        var profile = docProfile.RootElement;
        var sub = docSub.RootElement;

        JsonElement? plan = null;
        if (docPlan.RootElement.TryGetProperty("results", out var planResults))
        {
            foreach (var p in planResults.EnumerateArray()) { plan = p; break; }
        }

        var email = profile.TryGetProperty("email", out var em) ? em.GetString() : null;
        var firstName = profile.TryGetProperty("first_name", out var fn) ? fn.GetString() : null;
        var lastName = profile.TryGetProperty("last_name", out var ln) ? ln.GetString() : null;

        DateTime? periodStart = null, periodEnd = null;
        string? term = null;
        if (sub.TryGetProperty("start_date", out var sd) && sd.TryGetDateTime(out var dv)) periodStart = dv;
        if (sub.TryGetProperty("end_date", out var ed) && ed.TryGetDateTime(out var fv)) periodEnd = fv;
        if (sub.TryGetProperty("term", out var tm)) term = tm.GetString();

        string? planType = null;
        string? planSubtype = null;
        int? proxyCount = null;
        double? bandwidthLimitGb = null;

        if (plan.HasValue)
        {
            if (plan.Value.TryGetProperty("proxy_type", out var pt)) planType = pt.GetString();
            if (plan.Value.TryGetProperty("proxy_subtype", out var ps)) planSubtype = ps.GetString();
            if (plan.Value.TryGetProperty("proxy_count", out var pc)) proxyCount = pc.GetInt32();
            if (plan.Value.TryGetProperty("bandwidth_limit", out var bl) && bl.ValueKind == JsonValueKind.Number)
                bandwidthLimitGb = bl.GetDouble();
        }

        var requestCount = 0;
        long totalBytes = 0;
        var errorCount = 0;
        double totalDuration = 0;
        var byProxy = new Dictionary<string, (int req, long bytes, int err)>();
        var byDomain = new Dictionary<string, long>();

        string? nextUrl = "proxy/activity/?page_size=100";
        while (nextUrl is not null)
        {
            var json = await _http.GetStringAsync(nextUrl, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            foreach (var item in root.GetProperty("results").EnumerateArray())
            {
                requestCount++;

                var bytes = item.TryGetProperty("bytes", out var b) && b.ValueKind == JsonValueKind.Number
                    ? b.GetInt64() : 0L;
                totalBytes += bytes;

                var hasError = item.TryGetProperty("error_reason", out var er)
                    && er.ValueKind != JsonValueKind.Null
                    && !string.IsNullOrEmpty(er.GetString());
                if (hasError) errorCount++;

                if (item.TryGetProperty("request_duration", out var rd) && rd.ValueKind == JsonValueKind.Number)
                    totalDuration += rd.GetDouble();

                var proxyAddress = item.TryGetProperty("proxy_address", out var pa) ? pa.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(proxyAddress))
                {
                    byProxy.TryGetValue(proxyAddress, out var existing);
                    byProxy[proxyAddress] = (existing.req + 1, existing.bytes + bytes, existing.err + (hasError ? 1 : 0));
                }

                var domain = item.TryGetProperty("domain", out var dom) ? dom.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(domain))
                {
                    byDomain.TryGetValue(domain, out var domainBytes);
                    byDomain[domain] = domainBytes + bytes;
                }
            }

            nextUrl = root.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString() : null;
        }

        return new WebshareStatistics
        {
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            PlanType = planType,
            PlanSubtype = planSubtype,
            ProxyCount = proxyCount,
            BandwidthLimitGb = bandwidthLimitGb,
            Term = term,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            RequestCount = requestCount,
            TotalBytes = totalBytes,
            ErrorCount = errorCount,
            AverageDurationSeconds = requestCount > 0 ? totalDuration / requestCount : 0,
            ByProxy = byProxy.ToDictionary(kvp => kvp.Key, kvp => new WebshareProxyActivity { RequestCount = kvp.Value.req, Bytes = kvp.Value.bytes, ErrorCount = kvp.Value.err }),
            ByDomain = byDomain,
        };
    }

    // ── IProxyProviderService ───────────────────────────────────────────────

    ProxyEndpoint? IProxyProviderService.CurrentProxy => CurrentProxy?.ToProxyEndpoint();

    // Marque le proxy courant comme défaillant (ex : IP bannie) puis bascule automatiquement sur le suivant —
    // c'est le chemin emprunté quand un service consommateur signale un bannissement via NotifyProxyBanned().
    ProxyEndpoint? IProxyProviderService.RotateToNext()
    {
        lock (_lock)
        {
            if (_currentIndex >= 0 && _currentIndex < _proxies.Count)
                _proxies[_currentIndex].Failed = true;
        }
        return NextProxy()?.ToProxyEndpoint();
    }
}

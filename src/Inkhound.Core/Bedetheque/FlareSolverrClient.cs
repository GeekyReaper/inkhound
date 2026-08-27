using System.Text;
using System.Text.Json;

namespace Inkhound.Core.Bedetheque;

// Client minimal pour l'API HTTP /v1 de FlareSolverr (https://github.com/FlareSolverr/FlareSolverr).
// FlareSolverr fait tourner un navigateur headless qui résout les challenges Cloudflare et renvoie
// le HTML/JSON final (driver.page_source) — utilisé par BedethequeSourceService comme unique chemin
// HTTP quand Cloudflare bloque les requêtes directes (voir GetHtmlViaFlareSolverrAsync).
// Port depuis D:\Dev\workspace\bdguest-scrapper\Scrapper\FlareSolverrClient.cs (mécanisme vérifié
// fonctionnel en production sur ce même site).
internal sealed class FlareSolverrClient : IDisposable
{
    private readonly HttpClient _http;

    public FlareSolverrClient(string baseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(90),
        };
    }

    // Crée une session FlareSolverr (navigateur persistant). Le proxy, s'il est fourni, n'est
    // appliqué qu'à la création de session : c'est le seul endroit où FlareSolverr accepte des
    // identifiants (utilisateur/mot de passe) pour le proxy — une requête individuelle ne le
    // supporte pas.
    public async Task<string> CreateSessionAsync(string? proxyUrl, CancellationToken ct)
    {
        var sessionId = Guid.NewGuid().ToString();

        var payload = new Dictionary<string, object?>
        {
            ["cmd"] = "sessions.create",
            ["session"] = sessionId,
        };

        if (!string.IsNullOrWhiteSpace(proxyUrl))
            payload["proxy"] = BuildProxyPayload(proxyUrl);

        using var doc = await PostAsync(payload, ct);
        EnsureOk(doc, "Impossible de créer la session FlareSolverr.");

        return sessionId;
    }

    // Exécute un GET via la session FlareSolverr donnée. Retourne le code HTTP, le corps
    // (HTML/JSON), le user-agent du navigateur headless et les cookies obtenus.
    public async Task<(int Status, string Html, string? UserAgent,
        List<(string Name, string Value, string Domain, string Path)> Cookies)> RequestGetAsync(
        string sessionId, string url, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["cmd"] = "request.get",
            ["session"] = sessionId,
            ["url"] = url,
            ["maxTimeout"] = 60000,
        };

        using var doc = await PostAsync(payload, ct);
        EnsureOk(doc, $"FlareSolverr n'a pas pu résoudre {url}.");

        var solution = doc.RootElement.GetProperty("solution");
        var status = solution.TryGetProperty("status", out var s) ? s.GetInt32() : 200;
        var html = solution.TryGetProperty("response", out var r) ? r.GetString() ?? "" : "";
        var userAgent = solution.TryGetProperty("userAgent", out var ua) ? ua.GetString() : null;

        var cookies = new List<(string Name, string Value, string Domain, string Path)>();
        if (solution.TryGetProperty("cookies", out var cookiesEl) && cookiesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cookiesEl.EnumerateArray())
            {
                var name = c.TryGetProperty("name", out var n) ? n.GetString() : null;
                var value = c.TryGetProperty("value", out var v) ? v.GetString() : null;
                if (string.IsNullOrEmpty(name) || value is null) continue;
                var domain = c.TryGetProperty("domain", out var d) ? d.GetString() ?? "" : "";
                var path = c.TryGetProperty("path", out var p) && p.GetString() is { Length: > 0 } pv ? pv : "/";
                cookies.Add((name, value, domain, path));
            }
        }

        return (status, html, userAgent, cookies);
    }

    // Détruit une session FlareSolverr. Best effort — les erreurs sont avalées.
    public async Task DestroySessionAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["cmd"] = "sessions.destroy",
                ["session"] = sessionId,
            };
            using var doc = await PostAsync(payload, ct);
        }
        catch { /* best effort */ }
    }

    private async Task<JsonDocument> PostAsync(Dictionary<string, object?> payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("v1", content, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new BedethequeBlockedException($"FlareSolverr a répondu {(int)response.StatusCode} : {body}");

        return JsonDocument.Parse(body);
    }

    private static void EnsureOk(JsonDocument doc, string errorPrefix)
    {
        var status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : null;
        if (status != "ok")
        {
            var message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "erreur inconnue";
            throw new BedethequeBlockedException($"{errorPrefix} ({message})");
        }
    }

    private static Dictionary<string, object?> BuildProxyPayload(string proxyUrl)
    {
        var uri = new Uri(proxyUrl);
        var result = new Dictionary<string, object?>
        {
            ["url"] = $"{uri.Scheme}://{uri.Host}:{uri.Port}",
        };

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            result["username"] = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1)
                result["password"] = Uri.UnescapeDataString(parts[1]);
        }

        return result;
    }

    public void Dispose() => _http.Dispose();
}

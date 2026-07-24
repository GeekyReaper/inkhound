using System.Net;
using System.Text.RegularExpressions;
using Foundation.Core;
using Foundation.Core.Interface;
using Foundation.Core.Model;
using HtmlAgilityPack;
using Inkhound.Core.Models;
using Inkhound.Core.Sources;

namespace Inkhound.Core.Bedetheque;

// Intégration bedetheque.com (site scrapé, pas d'API publique) — Serie = Volume, Album = Issue.
// Logique de scraping réécrite ici à partir de zéro (aucune dépendance vers un projet tiers) ;
// seul le parseur HTML (HtmlAgilityPack) est un package NuGet standard.
public class BedethequeSourceService : BaseService<BedethequeOptions>, ISourceService
{
    private const string SourceKeyConst = "bedetheque";
    public string SourceKey => SourceKeyConst;

    private static readonly string[] LeadingArticles = ["les ", "le ", "la ", "l'", "des ", "un ", "une "];

    private readonly CookieContainer _cookies = new();
    private HttpClient _http;
    private RateLimiter _rateLimiter = null!;

    public BedethequeSourceService()
    {
        _http = BuildHttpClient();
        _rateLimiter = new RateLimiter(Options.RateLimitMs);

        // Sans ça, GetState() ré-exécute CheckInternalState() (donc une vraie requête HTTP vers
        // la page d'accueil) à chaque appel — y compris ceux de la boucle de monitoring globale
        // toutes les 30s en continu, et un de plus avant chaque recherche. Pour un site scrapé et
        // sensible au trafic automatisé, ce rythme régulier est justement le genre de signature
        // qui attire un blocage IP. Bedetheque n'est pas critique au fonctionnement global de
        // l'app (contrairement à DbStorage/ComicVine) : on peut se permettre un cache large.
        StateRefreshDelay = TimeSpan.FromMinutes(180);
    }

    #region Override BaseService

    public override string GetServiceName() => "Bedetheque";

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
        try
        {
            var html = await GetHtmlAsync("/", ct: CancellationToken.None);
            return string.IsNullOrWhiteSpace(html) ? EState.ERROR : EState.OK;
        }
        catch (Exception ex)
        {
            SendTrace("Request to Bedetheque failed", ex);
            return EState.ERROR;
        }
    }

    #endregion

    private HttpClient BuildHttpClient()
    {
        var handler = CreateHttpHandler(Options.UseProxy);
        handler.CookieContainer = _cookies;
        handler.UseCookies = true;
        handler.AllowAutoRedirect = true;
        handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli;

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(Options.BaseUrl),
            Timeout = TimeSpan.FromSeconds(Options.TimeoutSeconds)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Options.UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "fr-FR,fr;q=0.9,en-US;q=0.8,en;q=0.7");
        client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua", "\"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\", \"Not-A.Brand\";v=\"99\"");
        client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        return client;
    }

    #region API Mapping — recherche de séries

    // Recherche par formulaire (/search/albums) : la seule méthode fiable pour une requête
    // multi-mots (l'autocomplete AJAX du site ne matche que sur le premier mot). Le site
    // n'expose aucune pagination côté serveur pour cette recherche : on récupère la liste
    // complète, dédupliquée par nom de série, puis on résout l'ID réel de chaque série via
    // la page du premier album trouvé (seul moyen fiable de l'obtenir depuis ce flux).
    public async Task<IReadOnlyList<BdSerieSearchResult>> SearchAllSeriesByNameAsync(string query, CancellationToken ct = default)
    {
        var strippedQuery = StripLeadingArticle(query);

        var formHtml = await GetHtmlAsync("/search/albums", ct: ct);
        var formDoc = new HtmlDocument();
        formDoc.LoadHtml(formHtml);
        var csrf = formDoc.DocumentNode
            .SelectSingleNode("//input[@name='csrf_token_bel']")
            ?.GetAttributeValue("value", string.Empty);
        if (string.IsNullOrEmpty(csrf))
            throw new BedethequeBlockedException("CSRF token not found — Bedetheque may have blocked access or changed its page structure.");

        var searchUrl = $"/search/albums?csrf_token_bel={Uri.EscapeDataString(csrf)}" +
                         $"&RechSerie={Uri.EscapeDataString(strippedQuery)}" +
                         "&RechLangue=&RechEO=0";
        var html = await GetHtmlAsync(searchUrl, referer: $"{Options.BaseUrl}/search/albums", ct: ct);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var items = doc.DocumentNode.SelectNodes("//ul[contains(@class,'search-list')]/li");
        if (items is null) return [];

        var bySerieName = new Dictionary<string, (string AlbumUrl, string? CoverUrl, string? Langue, string? Origine, int Count, int? MinAnnee, int? MaxAnnee)>(StringComparer.OrdinalIgnoreCase);

        foreach (var li in items)
        {
            var link = li.SelectSingleNode(".//a");
            if (link is null) continue;

            var albumUrl = link.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrEmpty(albumUrl)) continue;
            var coverUrl = link.GetAttributeValue("rel", string.Empty) is { Length: > 0 } r ? r : null;

            var serieName = li.SelectSingleNode(".//span[@class='serie']")?.InnerText.Trim();
            if (string.IsNullOrEmpty(serieName)) continue;
            serieName = WebUtility.HtmlDecode(serieName);

            var dlText = li.SelectSingleNode(".//span[@class='dl']")?.InnerText.Trim() ?? string.Empty;
            var annee = ParseAnneeFromDl(dlText);

            var flagSrc = li.SelectSingleNode(".//span[@class='ico']/img")?.GetAttributeValue("src", string.Empty) ?? string.Empty;
            var langue = ExtractLangueFromFlag(flagSrc);
            var origine = ExtractOrigineFromFlag(flagSrc);

            if (bySerieName.TryGetValue(serieName, out var existing))
            {
                var minA = CombineYear(existing.MinAnnee, annee, Math.Min);
                var maxA = CombineYear(existing.MaxAnnee, annee, Math.Max);
                bySerieName[serieName] = (existing.AlbumUrl, existing.CoverUrl ?? coverUrl, existing.Langue ?? langue, existing.Origine ?? origine, existing.Count + 1, minA, maxA);
            }
            else
            {
                bySerieName[serieName] = (albumUrl, coverUrl, langue, origine, 1, annee, annee);
            }
        }

        using var semaphore = new SemaphoreSlim(Math.Max(1, Options.MaxParallelRequests));
        var resolveTasks = bySerieName.Select(async kvp =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var serieId = await ResolveSerieIdFromAlbumUrlAsync(kvp.Value.AlbumUrl, ct);
                if (serieId is null) return null;
                return new BdSerieSearchResult(
                    serieId.Value, kvp.Key, null, kvp.Value.Origine, kvp.Value.Langue,
                    kvp.Value.MinAnnee?.ToString(), kvp.Value.MaxAnnee?.ToString(), kvp.Value.Count,
                    kvp.Value.CoverUrl, $"{Options.BaseUrl}/serie-{serieId}-BD-x.html");
            }
            finally { semaphore.Release(); }
        });

        var resolved = await Task.WhenAll(resolveTasks);
        return resolved.Where(r => r is not null).Select(r => r!).ToList().AsReadOnly();
    }

    private async Task<int?> ResolveSerieIdFromAlbumUrlAsync(string albumUrl, CancellationToken ct)
    {
        var html = await GetHtmlAsync(albumUrl, ct: ct);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var href = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'bandeau-info')]//h1/a")?.GetAttributeValue("href", string.Empty);
        if (string.IsNullOrEmpty(href)) return null;
        var match = Regex.Match(href, @"serie-(\d+)-");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    private static string StripLeadingArticle(string query)
    {
        var trimmed = query.TrimStart();
        foreach (var article in LeadingArticles)
        {
            if (trimmed.StartsWith(article, StringComparison.OrdinalIgnoreCase))
                return trimmed[article.Length..].TrimStart();
        }
        return trimmed;
    }

    private static int? ParseAnneeFromDl(string dlText)
    {
        var match = Regex.Match(dlText, @"(\d{4})");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    private static int? CombineYear(int? existing, int? incoming, Func<int, int, int> combine)
    {
        if (incoming is null) return existing;
        if (existing is null) return incoming;
        return combine(existing.Value, incoming.Value);
    }

    #endregion

    #region API Mapping — détail série + albums

    public async Task<BdSerie?> GetSerieAsync(int id, CancellationToken ct = default)
    {
        var html = await GetHtmlAsync($"/serie-{id}-BD-x.html", ct: ct);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return ParseSerie(doc, id, $"{Options.BaseUrl}/serie-{id}-BD-x.html");
    }

    public async Task<IReadOnlyList<BdAlbumSummary>> GetAllAlbumSummariesForSerieAsync(int serieId, CancellationToken ct = default)
    {
        var serie = await GetSerieAsync(serieId, ct);
        return serie?.Albums ?? [];
    }

    // Récupère le détail complet (dont les auteurs) de chaque album de la série — un appel HTTP
    // par album, rythmé par le RateLimiter, miroir du pattern Phase 1/Phase 2 de
    // ComicVineSourceService.GetAllIssuesForVolumeAsync.
    public async Task<IReadOnlyList<BdAlbum>> GetAllAlbumsForSerieAsync(int serieId, CancellationToken ct = default)
    {
        var summaries = await GetAllAlbumSummariesForSerieAsync(serieId, ct);
        var all = new List<BdAlbum>();
        foreach (var summary in summaries)
        {
            var album = await GetAlbumAsync(summary.Id, ct);
            if (album is not null) all.Add(album);
        }
        return all.AsReadOnly();
    }

    public async Task<BdAlbum?> GetAlbumAsync(int id, CancellationToken ct = default)
    {
        var url = $"/BD-x-Tome-1-x-{id}.html";
        var html = await GetHtmlAsync(url, ct: ct);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return ParseAlbum(doc, id, $"{Options.BaseUrl}{url}");
    }

    private BdSerie? ParseSerie(HtmlDocument doc, int id, string serieUrl)
    {
        var titre = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'bandeau-info')]//h1/a")?.InnerText.Trim();
        if (string.IsNullOrEmpty(titre)) return null;
        titre = WebUtility.HtmlDecode(titre);

        var h3 = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'bandeau-info')]//h3");

        string? genre = h3?.SelectSingleNode(".//span[contains(@class,'style')]")?.InnerText.Trim();
        string? parution = h3?.SelectSingleNode(".//span/i[contains(@class,'icon-info-sign')]/..")?.InnerText.Trim();
        string? origine = h3?.SelectSingleNode(".//span/i[contains(@class,'icon-globe')]/..")?.InnerText.Trim();

        int? nombreAlbums = null;
        var albumsText = h3?.SelectSingleNode(".//span/i[contains(@class,'icon-book')]/..")?.InnerText;
        if (albumsText is not null)
        {
            var m = Regex.Match(albumsText, @"\d+");
            if (m.Success) nombreAlbums = int.Parse(m.Value);
        }

        string? anneeDebut = null, anneeFin = null;
        var anneeText = h3?.SelectSingleNode(".//span/i[contains(@class,'icon-calendar')]/..")?.InnerText;
        if (anneeText is not null)
        {
            var m = Regex.Match(anneeText, @"(\d{4})(?:-(\d{4}))?");
            if (m.Success)
            {
                anneeDebut = m.Groups[1].Value;
                anneeFin = m.Groups[2].Success ? m.Groups[2].Value : anneeDebut;
            }
        }

        var flagSrc = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'bandeau-info')]//img[contains(@class,'flag')]")?.GetAttributeValue("src", string.Empty) ?? string.Empty;
        var langue = ExtractLangueFromFlag(flagSrc);

        // Certaines pages utilisent une mise en page alternative où ces informations sont
        // exposées comme une simple liste plutôt que dans le bloc <h3> — fallback nécessaire.
        if (genre is null || nombreAlbums is null || origine is null)
        {
            var fallbackItems = doc.DocumentNode.SelectNodes("//ul[contains(@class,'serie-info')]/li");
            if (fallbackItems is not null)
            {
                foreach (var li in fallbackItems)
                {
                    var label = li.SelectSingleNode(".//label")?.InnerText.Trim() ?? string.Empty;
                    var value = li.InnerText.Replace(label, string.Empty).Trim();
                    if (genre is null && label.Contains("Genre", StringComparison.OrdinalIgnoreCase)) genre = value;
                    else if (nombreAlbums is null && label.Contains("Tome", StringComparison.OrdinalIgnoreCase))
                    {
                        var m = Regex.Match(value, @"\d+");
                        if (m.Success) nombreAlbums = int.Parse(m.Value);
                    }
                    else if (origine is null && label.Contains("Origine", StringComparison.OrdinalIgnoreCase)) origine = value;
                }
            }
        }

        var description = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'single-content')]//p")?.InnerText.Trim()
            ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'serie')]//p")?.InnerText.Trim();
        if (description is not null) description = WebUtility.HtmlDecode(description);

        var coverUrl = $"{Options.BaseUrl}/cache/thb_series/PlancheS_{id}.jpg";
        var albums = ParseAlbumList(doc);

        return new BdSerie(id, titre, genre, parution, nombreAlbums, origine, langue, anneeDebut, anneeFin, description, coverUrl, serieUrl, albums);
    }

    private static List<BdAlbumSummary> ParseAlbumList(HtmlDocument doc)
    {
        var result = new List<BdAlbumSummary>();
        var items = doc.DocumentNode.SelectNodes("//ul[contains(@class,'liste-albums')]/li[@itemscope]");
        if (items is null) return result;

        foreach (var li in items)
        {
            var anchor = li.SelectSingleNode(".//a[@name]");
            if (anchor is null || !int.TryParse(anchor.GetAttributeValue("name", ""), out var albumId) || albumId == 0)
                continue;

            var href = li.SelectSingleNode(".//a[@itemprop='url']")?.GetAttributeValue("href", string.Empty) ?? string.Empty;

            var img = li.SelectSingleNode(".//div[contains(@class,'couv')]//img[@itemprop='image']");
            var coverUrl = img?.GetAttributeValue("src", string.Empty) is { Length: > 0 } cs ? cs : null;

            var nameSpan = li.SelectSingleNode(".//span[@itemprop='name']");
            string titre;
            string? numero = null;
            if (nameSpan is not null)
            {
                var raw = Regex.Replace(nameSpan.InnerText, @"\s+", " ").Trim();
                var m = Regex.Match(raw, @"^(\S+)\s*\.\s*(.+)$");
                if (m.Success) { numero = m.Groups[1].Value.Trim(); titre = m.Groups[2].Value.Trim(); }
                else titre = raw;
            }
            else
            {
                titre = img?.GetAttributeValue("alt", string.Empty) ?? string.Empty;
            }
            titre = WebUtility.HtmlDecode(titre);

            var editeur = li.SelectSingleNode(".//span[@itemprop='publisher']")?.InnerText.Trim() is { Length: > 0 } ep ? ep : null;

            var dateContent = li.SelectSingleNode(".//meta[@itemprop='datePublished']")?.GetAttributeValue("content", string.Empty);
            var annee = dateContent is { Length: >= 4 } dc ? dc[..4] : null;

            result.Add(new BdAlbumSummary(albumId, titre, numero, annee, editeur, coverUrl, href));
        }
        return result;
    }

    private BdAlbum? ParseAlbum(HtmlDocument doc, int id, string url)
    {
        var serieLink = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'bandeau-info')]//h1/a");
        if (serieLink is null) return null;

        var serieHref = serieLink.GetAttributeValue("href", string.Empty);
        var serieMatch = Regex.Match(serieHref, @"serie-(\d+)-");
        var serieId = serieMatch.Success ? int.Parse(serieMatch.Groups[1].Value) : 0;
        var serieTitre = WebUtility.HtmlDecode(serieLink.InnerText.Trim());

        var altHeadline = doc.DocumentNode.SelectSingleNode("//meta[@itemprop='alternativeheadline']")?.GetAttributeValue("content", string.Empty);
        string? numAlbum = null;
        if (!string.IsNullOrEmpty(altHeadline))
        {
            var m = Regex.Match(altHeadline, @"Tome\s+(.+)");
            if (m.Success) numAlbum = m.Groups[1].Value.Trim();
        }

        var titre = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'bandeau-info')]//h2")?.InnerText.Trim() ?? string.Empty;
        titre = Regex.Replace(titre, @"^\d+[a-zA-Z']*\s*[.-]\s*", string.Empty).Trim();
        titre = WebUtility.HtmlDecode(titre);

        var h3 = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'bandeau-info')]//h3");

        var auteurs = new List<BdAuteur>();
        var listeAuteurs = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'liste-auteurs')]");
        var auteurLinks = listeAuteurs?.SelectNodes(".//a[@href]");
        var metierSpans = listeAuteurs?.SelectNodes(".//span[contains(@class,'metier')]");
        if (auteurLinks is not null)
        {
            for (var i = 0; i < auteurLinks.Count; i++)
            {
                var nom = WebUtility.HtmlDecode(auteurLinks[i].InnerText.Trim());
                if (string.IsNullOrEmpty(nom)) continue;
                var role = metierSpans is not null && i < metierSpans.Count ? metierSpans[i].InnerText.Trim() : null;
                var auteurUrl = auteurLinks[i].GetAttributeValue("href", string.Empty);
                auteurs.Add(new BdAuteur(nom, role, string.IsNullOrEmpty(auteurUrl) ? null : auteurUrl));
            }
        }
        if (auteurs.Count == 0 && h3 is not null)
        {
            var author = h3.SelectSingleNode(".//span[@itemprop='author']")?.InnerText.Trim();
            var illustrator = h3.SelectSingleNode(".//span[@itemprop='illustrator']")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(author)) auteurs.Add(new BdAuteur(author, "Scénario", null));
            if (!string.IsNullOrEmpty(illustrator)) auteurs.Add(new BdAuteur(illustrator, "Dessin", null));
        }

        var editeur = h3?.SelectSingleNode(".//span[contains(@class,'editeur')]")?.InnerText.Trim();
        var collection = h3?.SelectSingleNode(".//span[contains(@class,'collection')]")?.InnerText.Trim().Trim('(', ')');
        var annee = h3?.SelectSingleNode(".//span[contains(@class,'annee')]")?.InnerText.Trim();

        var ean = doc.DocumentNode.SelectSingleNode("//input[@id='EAN']")?.GetAttributeValue("value", string.Empty) is { Length: > 0 } ev ? ev : null;

        var coverUrl = doc.DocumentNode.SelectSingleNode("//input[@id='Couverture']")?.GetAttributeValue("value", string.Empty);
        if (string.IsNullOrEmpty(coverUrl))
            coverUrl = doc.DocumentNode.SelectSingleNode("//img[@itemprop='image']")?.GetAttributeValue("src", string.Empty);

        var descriptionRaw = doc.DocumentNode.SelectSingleNode("//span[@itemprop='description']")?.InnerText;
        string? description = null;
        if (!string.IsNullOrEmpty(descriptionRaw))
        {
            description = Regex.Replace(descriptionRaw, @"\s+", " ").Trim().Replace("Lire la suite", string.Empty).Trim();
            description = WebUtility.HtmlDecode(description);
        }

        return new BdAlbum(id, titre, numAlbum, serieId, serieTitre, $"{Options.BaseUrl}{serieHref}",
            auteurs, editeur, string.IsNullOrEmpty(collection) ? null : collection, annee, ean, description, coverUrl, url);
    }

    private static string? ExtractLangueFromFlag(string flagPath)
    {
        var match = Regex.Match(flagPath, @"flags/([^.]+)\.png");
        if (!match.Success) return null;
        return match.Groups[1].Value switch
        {
            "France" => "Français",
            "USA" => "Anglais",
            "Japan" => "Japonais",
            "Italy" => "Italien",
            "Germany" => "Allemand",
            "Spain" => "Espagnol",
            "Netherlands" => "Néerlandais",
            "Portugal" => "Portugais",
            _ => match.Groups[1].Value,
        };
    }

    private static string? ExtractOrigineFromFlag(string flagPath)
    {
        var match = Regex.Match(flagPath, @"flags/([^.]+)\.png");
        if (!match.Success) return null;
        return match.Groups[1].Value switch
        {
            "France" or "Italy" or "Germany" or "Spain" or "Netherlands" or "Portugal" or "Belgium" => "Europe",
            "USA" => "USA",
            "Japan" => "Asie",
            _ => "Autre",
        };
    }

    #endregion

    #region ISourceService

    async Task<Page<SourceVolume>> ISourceService.SearchVolumesByNameAsync(
        string query, int page, int? limit, CancellationToken ct)
    {
        var all = await SearchAllSeriesByNameAsync(query, ct);
        var effectiveLimit = limit ?? 20;
        var offset = (page - 1) * effectiveLimit;
        return new Page<SourceVolume>
        {
            Items = all.Skip(offset).Take(effectiveLimit).Select(ToSourceVolume).ToList(),
            PageNumber = page,
            PageSize = effectiveLimit,
            TotalItems = all.Count,
        };
    }

    async Task<SourceVolume?> ISourceService.GetVolumeAsync(string sourceVolumeId, CancellationToken ct)
    {
        if (!int.TryParse(sourceVolumeId, out var id)) return null;
        var serie = await GetSerieAsync(id, ct);
        return serie is null ? null : ToSourceVolume(serie);
    }

    async Task<Page<SourceIssue>> ISourceService.GetIssuesPageAsync(
        string sourceVolumeId, int page, int? limit, CancellationToken ct)
    {
        if (!int.TryParse(sourceVolumeId, out var id)) return new Page<SourceIssue>();
        var all = await GetAllAlbumSummariesForSerieAsync(id, ct);
        var effectiveLimit = limit ?? 20;
        var offset = (page - 1) * effectiveLimit;
        return new Page<SourceIssue>
        {
            Items = all.Skip(offset).Take(effectiveLimit).Select(ToSourceIssue).ToList(),
            PageNumber = page,
            PageSize = effectiveLimit,
            TotalItems = all.Count,
        };
    }

    async Task<IReadOnlyList<SourceIssue>> ISourceService.GetAllIssuesForVolumeAsync(
        string sourceVolumeId, CancellationToken ct)
    {
        if (!int.TryParse(sourceVolumeId, out var id)) return [];
        var albums = await GetAllAlbumsForSerieAsync(id, ct);
        return albums.Select(ToSourceIssue).ToList();
    }

    async Task<SourceIssue?> ISourceService.GetIssueAsync(string sourceIssueId, CancellationToken ct)
    {
        if (!int.TryParse(sourceIssueId, out var id)) return null;
        var album = await GetAlbumAsync(id, ct);
        return album is null ? null : ToSourceIssue(album);
    }

    private static SourceVolume ToSourceVolume(BdSerieSearchResult s) =>
        new(s.Id.ToString(), SourceKeyConst, s.Titre, ParseYear(s.AnneeDebut), s.NombreTomes ?? 0, null, null, s.CoverUrl, s.Url, Language: s.Langue);

    private static SourceVolume ToSourceVolume(BdSerie s) =>
        new(s.Id.ToString(), SourceKeyConst, s.Titre, ParseYear(s.AnneeDebut), s.NombreAlbums ?? s.Albums.Count, null, s.Description, s.CoverUrl, s.Url, Language: s.Langue);

    private static SourceIssue ToSourceIssue(BdAlbumSummary a) =>
        new(a.Id.ToString(), SourceKeyConst, a.Titre, a.NumeroAlbum ?? string.Empty, ParseYearAsDate(a.Annee), null, a.CoverUrl, a.Url);

    private static SourceIssue ToSourceIssue(BdAlbum a) =>
        new(a.Id.ToString(), SourceKeyConst, a.Titre, a.NumeroAlbum ?? string.Empty, ParseYearAsDate(a.Annee), a.Description, a.CoverUrl, a.Url);

    private static int? ParseYear(string? s) => int.TryParse(s, out var y) ? y : null;

    private static DateTime? ParseYearAsDate(string? s) => ParseYear(s) is { } y ? new DateTime(y, 1, 1) : null;

    #endregion

    // ── Bas niveau HTTP ──────────────────────────────────────────────────────

    // Réessaie sur un autre proxy du pool en cas d'échec imputable au proxy/à l'IP de sortie
    // actuelle :
    //  - le proxy refuse la connexion/le tunnel (HttpRequestException — ex. un proxy gratuit
    //    Webshare qui refuse le CONNECT HTTPS avec un 402) ;
    //  - le site bloque explicitement cette IP (BedethequeBlockedException — 429/403/503 ou
    //    message "Bloquage de l'IP" détecté dans le HTML) ;
    //  - le proxy n'arrive pas à joindre bedetheque.com et met trop longtemps à abandonner :
    //    Webshare peut mettre jusqu'à ~90s avant de renvoyer "target_connect_timeout" côté
    //    serveur, alors que Options.TimeoutSeconds (30s par défaut) fait déjà échouer la requête
    //    côté client avec une TaskCanceledException — sans ce cas, un proxy mort n'était jamais
    //    détecté ni remplacé (confirmé par les logs d'activité Webshare : le même proxy en échec
    //    était réinterrogé toutes les ~30s par le healthcheck périodique, sans jamais tourner).
    // Dans tous les cas, changer de proxy est le seul remède : on fait tourner vers le suivant du
    // pool et on réessaie. N'a d'effet que si UseProxy est actif ; sinon la boucle échoue dès la
    // première tentative comme avant.
    private const int MaxProxyRetries = 5;

    private async Task<string> GetHtmlAsync(string url, string? referer = null, CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= MaxProxyRetries; attempt++)
        {
            try
            {
                return await FetchHtmlAsync(url, referer, ct);
            }
            catch (Exception ex) when (Options.UseProxy && attempt < MaxProxyRetries
                && (ex is HttpRequestException || ex is BedethequeBlockedException
                    || (ex is TaskCanceledException && !ct.IsCancellationRequested)))
            {
                SendTrace($"[Proxy] Request failed (attempt {attempt}/{MaxProxyRetries}): {ex.Message} — rotating to next proxy.", ETraceLevel.WARNING);
                NotifyProxyBanned();
                ResetCookies();
                _http = BuildHttpClient();
            }
        }
        return await FetchHtmlAsync(url, referer, ct);
    }

    // Les cookies de session ont pu être posés via l'IP maintenant bannie — on les efface avant
    // de repartir sur un nouveau proxy, sinon un cookie de blocage éventuel survivrait au switch.
    private void ResetCookies()
    {
        foreach (Cookie cookie in _cookies.GetCookies(new Uri(Options.BaseUrl)))
            cookie.Expired = true;
    }

    private async Task<string> FetchHtmlAsync(string url, string? referer, CancellationToken ct)
    {
        var http = _http;
        var task = _rateLimiter.EnqueueAsync(async consumerCt =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("sec-fetch-mode", "navigate");
            req.Headers.TryAddWithoutValidation("sec-fetch-dest", "document");
            req.Headers.TryAddWithoutValidation("sec-fetch-site", referer is null ? "none" : "same-origin");
            if (referer is not null) req.Headers.Referrer = new Uri(referer);

            var response = await http.SendAsync(req, consumerCt);
            ThrowIfBlocked(response.StatusCode);
            var content = await response.Content.ReadAsStringAsync(consumerCt);
            ThrowIfBlockedHtml(content);
            return content;
        });
        return await task.WaitAsync(ct) ?? throw new BedethequeBlockedException($"Empty response for: {url}");
    }

    private static void ThrowIfBlocked(HttpStatusCode status)
    {
        if (status == HttpStatusCode.TooManyRequests)
            throw new BedethequeBlockedException("Too many requests (429) — automated access detected.", 429);
        if (status == HttpStatusCode.Forbidden)
            throw new BedethequeBlockedException("Access denied (403).", 403);
        if (status == HttpStatusCode.ServiceUnavailable)
            throw new BedethequeBlockedException("Service unavailable (503) — anti-bot protection likely.", 503);
    }

    private static void ThrowIfBlockedHtml(string html)
    {
        if (html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("captcha", StringComparison.OrdinalIgnoreCase))
            throw new BedethequeBlockedException("Blocking page detected (CAPTCHA or Cloudflare challenge).");

        if (html.Contains("Bloquage de l'IP", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("bdgest.com?subject=Bloquage", StringComparison.OrdinalIgnoreCase))
        {
            var ipMatch = Regex.Match(html, @"Bloquage de l'IP\s*:\s*([\d\.]+)", RegexOptions.IgnoreCase);
            var ip = ipMatch.Success ? ipMatch.Groups[1].Value : "unknown";
            throw new BedethequeBlockedException($"IP blocked by the site (IP: {ip}).");
        }
    }
}

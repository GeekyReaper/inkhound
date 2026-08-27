using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
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

    private readonly CookieContainer _cookies = new();
    private HttpClient _http;
    private RateLimiter _rateLimiter = null!;

    // FlareSolverr (navigateur headless réel piloté à distance) : chemin HTTP unique utilisé pour
    // TOUTES les requêtes quand configuré — voir GetHtmlAsync/GetHtmlViaFlareSolverrAsync. Une
    // session persistante est créée une seule fois puis réutilisée (évite de re-résoudre le
    // challenge Cloudflare à chaque appel) ; les appels sont sérialisés (_flareSolverrLock) car un
    // navigateur headless ne supporte pas des requêtes concurrentes fiables.
    private FlareSolverrClient? _flareSolverr;
    private string? _flareSolverrSessionId;
    private readonly SemaphoreSlim _flareSolverrLock = new(1, 1);

    // Cache mémoire (24h, par instance) des informations de série — voir GetOrFetchSerieAsync.
    private static readonly TimeSpan SerieCacheDuration = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<int, SerieCacheEntry> _serieCache = new();
    private sealed record SerieCacheEntry(BdSerie Detail, DateTime CachedAtUtc, bool Complete);

    // Cache mémoire (24h, par instance) des détails d'album — voir GetAlbumAsync.
    private static readonly TimeSpan AlbumCacheDuration = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<int, AlbumCacheEntry> _albumCache = new();
    private sealed record AlbumCacheEntry(BdAlbum Detail, DateTime CachedAtUtc);

    public BedethequeSourceService()
    {
        _http = BuildHttpClient();
        _flareSolverr = BuildFlareSolverrClient();
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

        // La config FlareSolverr a pu changer (URL, activation) — on jette l'ancien client/session
        // et on en reconstruit un neuf paresseusement au prochain appel.
        var oldFlareSolverr = _flareSolverr;
        _flareSolverr = BuildFlareSolverrClient();
        _flareSolverrSessionId = null;
        oldFlareSolverr?.Dispose();

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

    private FlareSolverrClient? BuildFlareSolverrClient() =>
        Options.UseFlareSolverr && !string.IsNullOrWhiteSpace(Options.FlareSolverrUrl)
            ? new FlareSolverrClient(Options.FlareSolverrUrl)
            : null;

    #region API Mapping — recherche de séries

    // Recherche via l'autocomplete AJAX du site (/ajax/tout?term=), pas via le formulaire
    // /search/albums : ce dernier exige un header Referer pointant vers la page de recherche pour
    // renvoyer de vrais résultats (vérifié : sans lui, réponse 200 mais formulaire vide,
    // silencieusement) — un Referer que FlareSolverr ne peut pas poser sur une navigation directe.
    // /ajax/tout n'a pas cette contrainte et renvoie directement des séries (pas des albums à
    // regrouper) avec leur ID réel, ce qui simplifie aussi tout le flux : plus besoin de dédupliquer
    // par nom ni de résoudre l'ID via la page d'un album.
    //
    // L'endpoint ne matche que depuis le début du nom de série et pas sur plusieurs mots : on
    // envoie le premier mot et on filtre côté client pour les requêtes multi-mots.
    public async Task<IReadOnlyList<BdSerieSearchResult>> SearchAllSeriesByNameAsync(string query, CancellationToken ct = default)
    {
        var strippedQuery = StripLeadingArticle(query);
        var firstWord = strippedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? strippedQuery;

        var url = $"{Options.BaseUrl}/ajax/tout?term={Uri.EscapeDataString(firstWord)}";
        var json = await GetHtmlAsync(url, referer: Options.BaseUrl, navigate: false, ct: ct);

        var items = ParseAjaxToutItemsResilient(json);
        var series = new List<(int Id, string Titre, string FlagSrc)>();
        foreach (var item in items)
        {
            if (item.GetProperty("category").GetString() != "Séries") continue;

            var rawId = item.GetProperty("id").GetString() ?? string.Empty;
            if (!int.TryParse(rawId.TrimStart('S'), out var id)) continue;

            var titre = item.GetProperty("label").GetString();
            if (string.IsNullOrEmpty(titre)) continue;

            series.Add((id, WebUtility.HtmlDecode(titre), item.GetProperty("desc").GetString() ?? string.Empty));
        }

        if (strippedQuery.Contains(' '))
            series = series.Where(s => s.Titre.Contains(strippedQuery, StringComparison.OrdinalIgnoreCase)).ToList();

        // Filtre par langue AVANT enrichissement (pas après) : chaque série enrichie coûte une
        // requête réseau supplémentaire en file derrière le sémaphore FlareSolverr — le filtre
        // exploite le drapeau déjà présent dans la réponse AJAX, sans avoir besoin d'enrichir pour
        // connaître la langue.
        if (Options.SearchLanguageFilter != BedethequeSearchLanguage.All)
        {
            var wanted = LanguageFilterLabel(Options.SearchLanguageFilter);
            series = series.Where(s => string.Equals(ExtractLangueFromFlag(s.FlagSrc), wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        using var semaphore = new SemaphoreSlim(Math.Max(1, Options.MaxParallelRequests));
        var enrichTasks = series.Select(async s =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var detail = await GetOrFetchSerieAsync(s.Id, requireComplete: false, ct);
                var origine = ExtractOrigineFromFlag(s.FlagSrc);
                var langue = ExtractLangueFromFlag(s.FlagSrc);
                var coverUrl = detail?.Albums.FirstOrDefault()?.CoverUrl
                    ?? detail?.CoverUrl
                    ?? $"{Options.BaseUrl}/cache/thb_series/PlancheS_{s.Id}.jpg";

                return new BdSerieSearchResult(
                    s.Id, s.Titre, detail?.Genre, origine, detail?.Langue ?? langue,
                    detail?.AnneeDebut, detail?.AnneeFin, detail?.NombreAlbums ?? detail?.Albums.Count,
                    coverUrl, $"{Options.BaseUrl}/serie-{s.Id}-BD-x.html");
            }
            finally { semaphore.Release(); }
        });

        return (await Task.WhenAll(enrichTasks)).ToList().AsReadOnly();
    }

    private static string LanguageFilterLabel(BedethequeSearchLanguage lang) => lang switch
    {
        BedethequeSearchLanguage.Francais => "Français",
        BedethequeSearchLanguage.Anglais => "Anglais",
        BedethequeSearchLanguage.Japonais => "Japonais",
        BedethequeSearchLanguage.Italien => "Italien",
        BedethequeSearchLanguage.Allemand => "Allemand",
        BedethequeSearchLanguage.Espagnol => "Espagnol",
        BedethequeSearchLanguage.Neerlandais => "Néerlandais",
        BedethequeSearchLanguage.Portugais => "Portugais",
        _ => lang.ToString(),
    };

    // FlareSolverr renvoie driver.page_source (le DOM tel que rendu par Chrome), jamais la réponse
    // HTTP brute — pour un endpoint JSON, certaines entrées peuvent être corrompues par ce rendu
    // (ex. catégorie "Auteurs", dont le label embarque un tag <i class="icon-user"> que Chrome
    // interprète parfois comme du vrai DOM plutôt que du texte, cassant le JSON à cet endroit). On
    // découpe donc le tableau en objets top-level et on parse chacun individuellement, en ignorant
    // silencieusement ceux qui échouent plutôt que de perdre tout le résultat.
    private static List<JsonElement> ParseAjaxToutItemsResilient(string json)
    {
        var results = new List<JsonElement>();
        var trimmed = json.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[')
            return results;

        foreach (var objJson in SplitTopLevelJsonObjects(trimmed))
        {
            try
            {
                using var doc = JsonDocument.Parse(objJson);
                results.Add(doc.RootElement.Clone());
            }
            catch (JsonException) { /* entrée corrompue par le rendu Chrome, ignorée */ }
        }
        return results;
    }

    // Découpe un tableau JSON ("[{...},{...}]") en substrings de ses objets top-level.
    private static List<string> SplitTopLevelJsonObjects(string jsonArray)
    {
        var result = new List<string>();
        int depth = 0, start = -1;
        bool inString = false, escape = false;

        for (int i = 1; i < jsonArray.Length; i++)
        {
            var c = jsonArray[i];
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') { inString = true; continue; }
            if (c == '{')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    result.Add(jsonArray[start..(i + 1)]);
                    start = -1;
                }
            }
        }
        return result;
    }

    // Articles français courants placés en tête de titre par l'utilisateur mais que le site
    // range en fin de titre : "Les Légendaires" → "Légendaires (Les)". Couvre l'apostrophe droite
    // et l'apostrophe typographique (celle qu'insèrent certains claviers/correcteurs).
    private static readonly string[] LeadingArticles = ["l'", "l'", "les ", "le ", "la ", "des ", "un ", "une "];

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

    #endregion

    #region API Mapping — détail série + albums

    public async Task<BdSerie?> GetSerieAsync(int id, CancellationToken ct = default)
        => await GetOrFetchSerieAsync(id, requireComplete: true, ct);

    // Point d'entrée unique pour récupérer les informations d'une série, avec cache mémoire (24h,
    // par instance) partagé entre GetSerieAsync et l'enrichissement de SearchAllSeriesByNameAsync
    // pour éviter de refetcher la même page /serie-{id}-BD-x.html deux fois.
    // requireComplete: true (GetSerieAsync — flux "ajouter à la bibliothèque"/rematch) exige la
    // liste complète des albums et n'accepte une entrée en cache que si elle est déjà complète ;
    // false (enrichissement de recherche) se contente d'un aperçu (page 1) et accepte n'importe
    // quelle entrée fraîche, complète ou non — moins cher en requêtes pour une recherche qui peut
    // remonter plusieurs séries à enrichir.
    private async Task<BdSerie?> GetOrFetchSerieAsync(int id, bool requireComplete, CancellationToken ct)
    {
        if (_serieCache.TryGetValue(id, out var cached)
            && DateTime.UtcNow - cached.CachedAtUtc < SerieCacheDuration
            && (!requireComplete || cached.Complete))
        {
            return CloneSerie(cached.Detail);
        }

        // Bedetheque pagine la liste des albums d'une série à 10 par page ; "__10000" est le
        // suffixe utilisé par le lien "Tout" du site pour renvoyer la liste complète en un seul GET
        // (vérifié : fonctionne aussi avec le slug générique "x" utilisé ici) — on ne le demande
        // que si l'appelant a besoin de la liste complète, sinon la page 1 (plus légère) suffit.
        var path = requireComplete ? $"/serie-{id}-BD-x__10000.html" : $"/serie-{id}-BD-x.html";
        var html = await GetHtmlAsync(path, referer: Options.BaseUrl, ct: ct);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var detail = ParseSerie(doc, id, $"{Options.BaseUrl}/serie-{id}-BD-x.html");
        if (detail is null) return null;

        // "Complet" = la liste d'albums couvre déjà le total annoncé — vrai automatiquement pour
        // les séries courtes même via le fetch léger (page 1), donc un futur appel requireComplete
        // peut réutiliser cette entrée sans refetch.
        var complete = detail.NombreAlbums is not { } total || total <= detail.Albums.Count;
        _serieCache[id] = new SerieCacheEntry(detail, DateTime.UtcNow, complete);
        return CloneSerie(detail);
    }

    // Copie superficielle (+ nouvelle liste Albums) pour qu'un appelant qui modifierait l'objet
    // retourné ne corrompe pas l'entrée en cache partagée entre GetSerieAsync et la recherche.
    private static BdSerie CloneSerie(BdSerie source) => source with { Albums = source.Albums.ToList() };

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

    // Cache mémoire (24h, par instance) — GetAllAlbumsForSerieAsync appelle cette méthode une fois
    // par album de la série ; un cache évite de refetcher le même album d'une recherche à l'autre
    // dans la même session.
    public async Task<BdAlbum?> GetAlbumAsync(int id, CancellationToken ct = default)
    {
        if (_albumCache.TryGetValue(id, out var cached) && DateTime.UtcNow - cached.CachedAtUtc < AlbumCacheDuration)
            return CloneAlbum(cached.Detail);

        var url = $"/BD-x-Tome-1-x-{id}.html";
        var html = await GetHtmlAsync(url, referer: Options.BaseUrl, ct: ct);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var album = ParseAlbum(doc, id, $"{Options.BaseUrl}{url}");
        if (album is null) return null;

        _albumCache[id] = new AlbumCacheEntry(album, DateTime.UtcNow);
        return CloneAlbum(album);
    }

    // Copie superficielle (+ nouvelle liste Auteurs) — même raison que CloneSerie ci-dessus.
    private static BdAlbum CloneAlbum(BdAlbum source) => source with { Auteurs = source.Auteurs.ToList() };

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

        var albums = ParseAlbumList(doc);

        // Bedetheque n'a pas d'illustration dédiée à la série (le pattern "thb_series/PlancheS_"
        // n'existe pas sur le site — vérifié, toujours 404) : le site lui-même utilise la
        // couverture du premier album comme og:image sur la page série, donc on fait pareil,
        // avec repli sur la vignette du premier album si la balise meta est absente.
        var coverUrl = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", string.Empty);
        if (string.IsNullOrEmpty(coverUrl))
            coverUrl = albums.FirstOrDefault()?.CoverUrl;

        // L'éditeur de la série est affiché sous forme d'une mention "© Editeur - Année" juste
        // sous l'image de couverture (ex : "© Le Lombard - 2026"), PAS dans <ul class="serie-info">.
        // Le XPath doit être circonscrit à div.serie-image : une recherche non circonscrite peut
        // matcher un autre élément portant "copyrightserie" dans sa classe ailleurs sur la page et
        // renvoyer null (confirmé par comparaison avec bdguest-scrapper, qui scope cette recherche
        // et récupère l'éditeur correctement). Le "©" est optionnel dans le regex (certaines pages
        // omettent le symbole). Repli sur l'éditeur du premier album si la mention est absente
        // ("souvent affiché sous l'image", pas toujours).
        var copyrightText = doc.DocumentNode
            .SelectSingleNode("//div[contains(@class,'serie-image')]//div[contains(@class,'copyrightserie')]")
            ?.InnerText.Trim();
        string? editeur = null;
        if (!string.IsNullOrEmpty(copyrightText))
        {
            var m = Regex.Match(copyrightText, @"^©?\s*(?<publisher>.+?)\s*-\s*\d{4}\s*$");
            editeur = m.Success ? m.Groups["publisher"].Value.Trim() : copyrightText.TrimStart('©').Trim();
        }
        editeur = string.IsNullOrEmpty(editeur) ? albums.FirstOrDefault()?.Editeur : editeur;

        return new BdSerie(id, titre, genre, parution, nombreAlbums, origine, langue, anneeDebut, anneeFin, description, coverUrl, serieUrl, albums, editeur);
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

    // Point de passage HTTP unique du service : route vers FlareSolverr si configuré, sinon vers
    // une requête directe classique. `navigate` distingue une page HTML classique (true) d'un
    // endpoint AJAX/JSON (false) — nécessaire côté FlareSolverr pour désencapsuler correctement la
    // réponse (voir GetHtmlViaFlareSolverrAsync).
    private async Task<string> GetHtmlAsync(string url, string? referer = null, bool navigate = true, CancellationToken ct = default)
        => _flareSolverr is not null
            ? await GetHtmlViaFlareSolverrAsync(url, navigate, ct)
            : await GetHtmlDirectAsync(url, referer, navigate, ct);

    private async Task<string> GetHtmlDirectAsync(string url, string? referer, bool navigate, CancellationToken ct)
    {
        var http = _http;
        var task = _rateLimiter.EnqueueAsync(async consumerCt =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (navigate)
            {
                req.Headers.TryAddWithoutValidation("sec-fetch-mode", "navigate");
                req.Headers.TryAddWithoutValidation("sec-fetch-dest", "document");
                req.Headers.TryAddWithoutValidation("sec-fetch-site", referer is null ? "none" : "same-origin");
            }
            else
            {
                req.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
                req.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
                req.Headers.TryAddWithoutValidation("sec-fetch-site", "same-origin");
            }
            if (referer is not null) req.Headers.Referrer = new Uri(referer);

            var response = await http.SendAsync(req, consumerCt);
            ThrowIfBlocked(response.StatusCode, url);
            var content = await response.Content.ReadAsStringAsync(consumerCt);
            ThrowIfBlockedHtml(content, url);
            return content;
        });
        return await task.WaitAsync(ct) ?? throw new BedethequeBlockedException($"Empty response for: {url}");
    }

    // Envoie la requête via FlareSolverr (navigateur headless réel qui résout le challenge
    // Cloudflare). Une session unique est créée paresseusement puis réutilisée pour éviter de
    // re-résoudre le challenge à chaque appel ; les appels sont sérialisés (_flareSolverrLock) car
    // une session FlareSolverr ne supporte pas des requêtes concurrentes fiables (un seul
    // navigateur/onglet).
    private async Task<string> GetHtmlViaFlareSolverrAsync(string url, bool navigate, CancellationToken ct)
    {
        var flareSolverr = _flareSolverr!;
        var fullUrl = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"{Options.BaseUrl}{url}";

        await _flareSolverrLock.WaitAsync(ct);
        try
        {
            _flareSolverrSessionId ??= await flareSolverr.CreateSessionAsync(null, ct);

            try
            {
                var (status, html, _, _) = await flareSolverr.RequestGetAsync(_flareSolverrSessionId, fullUrl, ct);
                ThrowIfBlocked((HttpStatusCode)status, fullUrl);

                // Pour les endpoints AJAX/JSON, Chrome (piloté par FlareSolverr) affiche la réponse
                // encapsulée dans <html><head></head><body>...</body></html> au lieu du JSON brut
                // (FlareSolverr renvoie driver.page_source, jamais la réponse HTTP brute) — on
                // désencapsule pour retrouver la réponse originale avant de continuer.
                if (!navigate)
                    html = UnwrapFlareSolverrTextResponse(html);

                ThrowIfBlockedHtml(html, fullUrl);
                return html;
            }
            catch (BedethequeBlockedException)
            {
                // Session potentiellement corrompue : on la jette pour forcer une re-résolution
                // propre au prochain appel. Pas de retry automatique ici — c'est l'appelant qui
                // décide de réessayer.
                await flareSolverr.DestroySessionAsync(_flareSolverrSessionId, CancellationToken.None);
                _flareSolverrSessionId = null;
                throw;
            }
        }
        finally
        {
            _flareSolverrLock.Release();
        }
    }

    // Retire l'enveloppe HTML que Chrome ajoute autour d'une réponse non-HTML (ex. JSON) quand on
    // y navigue directement — utilisé par FlareSolverr, qui pilote un vrai navigateur.
    private static string UnwrapFlareSolverrTextResponse(string html)
    {
        var match = Regex.Match(html, @"<body[^>]*>(.*)</body>", RegexOptions.Singleline);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : html;
    }

    private static void ThrowIfBlocked(HttpStatusCode status, string url)
    {
        if (status == HttpStatusCode.TooManyRequests)
            throw new BedethequeBlockedException($"Too many requests (429) — automated access detected. [{url}]", 429);
        if (status == HttpStatusCode.Forbidden)
            throw new BedethequeBlockedException($"Access denied (403). [{url}]", 403);
        if (status == HttpStatusCode.ServiceUnavailable)
            throw new BedethequeBlockedException($"Service unavailable (503) — anti-bot protection likely. [{url}]", 503);
    }

    private static void ThrowIfBlockedHtml(string html, string url)
    {
        if (html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("captcha", StringComparison.OrdinalIgnoreCase))
            throw new BedethequeBlockedException($"Blocking page detected (CAPTCHA or Cloudflare challenge). [{url}]");

        if (html.Contains("Bloquage de l'IP", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("bdgest.com?subject=Bloquage", StringComparison.OrdinalIgnoreCase))
        {
            var ipMatch = Regex.Match(html, @"Bloquage de l'IP\s*:\s*([\d\.]+)", RegexOptions.IgnoreCase);
            var ip = ipMatch.Success ? ipMatch.Groups[1].Value : "unknown";
            throw new BedethequeBlockedException($"IP blocked by the site (IP: {ip}). [{url}]");
        }
    }
}

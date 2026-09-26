using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Inkhound.Core.News;

namespace Inkhound.Core.Bedetheque;

/// <summary>
/// Source d'actualité Bedetheque : top des ventes hebdomadaire (<c>bdgest.com/top/ventes</c>, site
/// frère derrière le même Cloudflare) et nouveautés (<c>bedetheque.com/nouveautes</c>). Toutes les
/// requêtes passent par <see cref="BedethequeSourceService"/> — même rate limiter, même session
/// FlareSolverr, et mêmes caches 24 h pour les pages album/série (partagés avec l'ajout de volume
/// et le refresh).
/// </summary>
public class BedethequeNewsProvider(BedethequeSourceService bedetheque) : INewsProvider
{
    private const string BdgestBaseUrl = "https://www.bdgest.com";

    public string ProviderKey => bedetheque.SourceKey;

    #region INewsProvider

    public async Task<NewsTopSalesSnapshot?> FetchTopSalesAsync(DateOnly? week = null, CancellationToken ct = default)
    {
        var url = week is { } w
            ? $"{BdgestBaseUrl}/top/ventes?semaine={w:yyyy-MM-dd}"
            : $"{BdgestBaseUrl}/top/ventes";
        var doc = await bedetheque.GetDocumentAsync(url, ct);
        return ParseTopSales(doc);
    }

    // Deux lectures de la même fenêtre : la vue "Liste" donne les champs structurés (série, n°,
    // titre, éditeur, drapeau d'origine) mais seulement le mois de sortie ; la vue "Couverture"
    // donne la date exacte ("Parution le : jj/mm/aaaa"). Jointure par id d'album. Sans filtre
    // d'origine : une seule page couvre BD, manga et comics (le type vient du drapeau).
    public async Task<IReadOnlyList<NewsScrapedEntry>> FetchReleasesAsync(DateOnly? month = null, CancellationToken ct = default)
    {
        var dl = month is { } m ? $"DL={m:MM}/{m:yyyy}&" : string.Empty;
        var listDoc = await bedetheque.GetDocumentAsync($"/nouveautes?{dl}Affichage=Liste", ct);
        HtmlDocument? coverDoc = null;
        try
        {
            coverDoc = await bedetheque.GetDocumentAsync($"/nouveautes?{dl}Affichage=Couverture", ct);
        }
        catch (Exception ex) when (ex is not BedethequeBlockedException && !ct.IsCancellationRequested)
        {
            // Dates exactes indisponibles : repli sur le 1er du mois de la section (voir ParseReleases).
        }
        return ParseReleases(listDoc, coverDoc);
    }

    public async Task<NewsAlbumEnrichment?> EnrichAlbumAsync(string albumId, CancellationToken ct = default)
    {
        if (!int.TryParse(albumId, out var id)) return null;
        var album = await bedetheque.GetAlbumAsync(id, ct);
        if (album is null || album.SerieId == 0) return null;

        var images = (album.Images ?? []).Select(i => new NewsImage(i.Kind, i.ThumbUrl, i.Url)).ToList();
        var coverLarge = images.FirstOrDefault(i => i.Kind == "Cover")?.Url
            ?? BedethequeSourceService.ToLargeCoverUrl(album.CoverUrl);

        return new NewsAlbumEnrichment
        {
            AlbumId = albumId,
            SeriesId = album.SerieId.ToString(CultureInfo.InvariantCulture),
            SeriesTitle = album.SerieTitre,
            AlbumTitle = string.IsNullOrEmpty(album.Titre) ? null : album.Titre,
            AlbumNumber = album.NumeroAlbum,
            Publisher = album.Editeur,
            Collection = album.Collection,
            Year = album.Annee,
            LegalDeposit = album.DepotLegal,
            Ean = album.Ean,
            Genre = album.Genre,
            Description = album.Description,
            Rating = album.Note,
            RatingCount = album.NombreVotes,
            Pages = album.Planches,
            CoverUrl = album.CoverUrl,
            CoverLargeUrl = coverLarge,
            Url = album.Url,
            Authors = album.Auteurs.Select(a => new NewsAuthor(a.Nom, a.Role)).ToList(),
            Images = images,
        };
    }

    // Liste complète des albums (même entrée de cache que l'ajout à la bibliothèque).
    public async Task<NewsSeriesDetail?> GetSeriesAsync(string seriesId, CancellationToken ct = default)
    {
        if (!int.TryParse(seriesId, out var id)) return null;
        var serie = await bedetheque.GetSerieAsync(id, ct);
        if (serie is null) return null;

        return new NewsSeriesDetail
        {
            SeriesId = seriesId,
            Title = serie.Titre,
            Genre = serie.Genre,
            Status = serie.Parution,
            AlbumCount = serie.NombreAlbums,
            Origin = serie.Origine,
            Language = serie.Langue,
            StartYear = serie.AnneeDebut,
            EndYear = serie.AnneeFin,
            Description = serie.Description,
            Publisher = serie.Editeur,
            CoverUrl = serie.CoverUrl,
            Url = serie.Url,
            Albums = serie.Albums
                .Select(a => new NewsSeriesAlbum(a.Id.ToString(CultureInfo.InvariantCulture), a.Titre, a.NumeroAlbum, a.Annee, a.CoverUrl, a.Category))
                .ToList(),
        };
    }

    #endregion

    #region Parsing — top des ventes

    // <h1>… Top des ventes - <span class="orange">Semaine du 14/09/2026</span></h1>
    // <ol class="top-ventes"><li><div class="place">n°1</div><a class="couv" href="…-{albumId}.html"><img src="thb_couv…"></a>
    //   <div class="evolution">(icon-star | span.plus "+2" | span.moins "-3" | vide)</div>
    //   <div class="main"><h3><a>Série</a><br>14. Titre</h3>
    //     <div class="infos"><i class="icon-building"/> <span>Éditeur</span> <i class="icon-calendar"/> Parution: <span>18/09/2026</span>
    //       <i class="icon-time"/> <span>4ème semaine</span></div><p>Résumé[…]</p></div></li>
    internal static NewsTopSalesSnapshot? ParseTopSales(HtmlDocument doc)
    {
        var items = doc.DocumentNode.SelectNodes("//ol[contains(@class,'top-ventes')]/li");
        if (items is null) return null;

        var weekLabel = doc.DocumentNode
            .SelectSingleNode("//h1[contains(.,'Top des ventes')]//span[contains(@class,'orange')]")
            ?.InnerText;
        var week = ParseFrenchDate(weekLabel) ?? MondayOf(DateOnly.FromDateTime(DateTime.UtcNow));

        var entries = new List<NewsScrapedEntry>();
        foreach (var li in items)
        {
            var rankMatch = Regex.Match(li.SelectSingleNode(".//div[contains(@class,'place')]")?.InnerText ?? string.Empty, @"\d+");
            if (!rankMatch.Success) continue;

            var link = li.SelectSingleNode(".//a[contains(@class,'couv')]");
            var href = link?.GetAttributeValue("href", string.Empty) ?? string.Empty;
            var albumId = ExtractAlbumId(href);
            if (albumId is null) continue;

            var cover = link?.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty) is { Length: > 0 } c ? c : null;

            // Le <br> n'apparaît pas dans InnerText : on scinde le HTML brut pour séparer le lien
            // série (avant) du texte "N. Titre" (après).
            var h3 = li.SelectSingleNode(".//div[contains(@class,'main')]/h3");
            var seriesTitle = BedethequeSourceService.CleanScrapedText(h3?.SelectSingleNode(".//a")?.InnerText);
            string? number = null, title = null;
            if (h3 is not null)
            {
                var parts = Regex.Split(h3.InnerHtml, @"<br\s*/?>", RegexOptions.IgnoreCase);
                if (parts.Length > 1)
                {
                    var raw = BedethequeSourceService.CleanScrapedText(Regex.Replace(parts[1], "<[^>]+>", string.Empty));
                    var m = Regex.Match(raw, @"^(\S+?)\s*\.\s*(.*)$");
                    if (m.Success) { number = m.Groups[1].Value; title = m.Groups[2].Value; }
                    else title = raw;
                }
            }
            if (string.IsNullOrEmpty(seriesTitle))
                seriesTitle = BedethequeSourceService.CleanScrapedText(link?.GetAttributeValue("title", string.Empty));

            var infos = li.SelectSingleNode(".//div[contains(@class,'infos')]");
            var publisher = InfoAfterIcon(infos, "icon-building");
            var releaseDate = ParseFrenchDate(InfoAfterIcon(infos, "icon-calendar"));
            var weeksText = InfoAfterIcon(infos, "icon-time");
            int? weeks = weeksText is not null && Regex.Match(weeksText, @"\d+") is { Success: true } wm ? int.Parse(wm.Value) : null;

            var (evolution, delta) = ParseEvolution(li.SelectSingleNode(".//div[contains(@class,'evolution')]"));

            var summary = BedethequeSourceService.CleanScrapedText(li.SelectSingleNode(".//div[contains(@class,'main')]/p")?.InnerText);

            entries.Add(new NewsScrapedEntry
            {
                AlbumId = albumId,
                SeriesTitle = seriesTitle,
                AlbumNumber = string.IsNullOrEmpty(number) ? null : number,
                AlbumTitle = string.IsNullOrEmpty(title) ? null : title,
                Publisher = publisher,
                ReleaseDate = releaseDate,
                ShortDescription = string.IsNullOrEmpty(summary) ? null : summary,
                CoverUrl = cover,
                CoverLargeUrl = BedethequeSourceService.ToLargeCoverUrl(cover),
                AlbumUrl = string.IsNullOrEmpty(href) ? null : href,
                Rank = int.Parse(rankMatch.Value),
                Evolution = evolution,
                EvolutionDelta = delta,
                WeeksInChart = weeks,
            });
        }

        return new NewsTopSalesSnapshot(week, entries);
    }

    private static string? InfoAfterIcon(HtmlNode? infos, string iconClass)
    {
        var text = BedethequeSourceService.CleanScrapedText(
            infos?.SelectSingleNode($".//i[contains(@class,'{iconClass}')]/following-sibling::span[1]")?.InnerText);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static (string Evolution, int? Delta) ParseEvolution(HtmlNode? node)
    {
        if (node is null) return ("Stable", null);
        if (node.SelectSingleNode(".//i[contains(@class,'icon-star')]") is not null) return ("New", null);
        if (node.SelectSingleNode(".//span[contains(@class,'plus')]") is { } plus)
            return ("Up", Regex.Match(plus.InnerText, @"\d+") is { Success: true } m ? int.Parse(m.Value) : null);
        if (node.SelectSingleNode(".//span[contains(@class,'moins')]") is { } minus)
            return ("Down", Regex.Match(minus.InnerText, @"\d+") is { Success: true } m ? int.Parse(m.Value) : null);
        return ("Stable", null);
    }

    #endregion

    #region Parsing — nouveautés

    // Vue "Liste" : une section par mois —
    //   <div class="widget-line-title"><h3>Les nouveautés de octobre 2026 (36)</h3></div>
    //   <div class="block-big …"><ul class="nouveautes-list">
    //     <li><span class="ico"><a href="…Origine=1…"><img src="…/flags/europe.png"></a></span>
    //       <a class="editeur"><span>Delcourt</span></a> -
    //       <a class="image-tooltip serie" rel="…/thb_couv/Couv_543915.jpg" href="…-543915.html">
    //         <span class="serie">Mawrth Valliis</span><span class="num"> -2- </span><span class="numa"></span>
    //         <span class="titre">Tome 2</span></a></li>
    //     <li class="sep"><hr></li>…
    // Vue "Couverture" (optionnelle) : <a href="…-{albumId}.html" title="Éditeur - Série -2- Titre - Parution le : 01/10/2026">.
    internal static List<NewsScrapedEntry> ParseReleases(HtmlDocument listDoc, HtmlDocument? coverDoc)
    {
        var dates = coverDoc is null ? new Dictionary<string, DateOnly>() : ParseReleaseDates(coverDoc);
        var results = new List<NewsScrapedEntry>();
        var seen = new HashSet<string>();

        var lists = listDoc.DocumentNode.SelectNodes("//ul[contains(@class,'nouveautes-list')]");
        if (lists is null) return results;

        foreach (var ul in lists)
        {
            var heading = ul.SelectSingleNode("preceding::div[contains(@class,'widget-line-title')][1]//h3")?.InnerText;
            var sectionMonth = ParseFrenchMonth(heading);

            foreach (var li in ul.SelectNodes("./li") ?? Enumerable.Empty<HtmlNode>())
            {
                var link = li.SelectSingleNode(".//a[contains(@class,'serie')]");
                var href = link?.GetAttributeValue("href", string.Empty) ?? string.Empty;
                var albumId = ExtractAlbumId(href);
                if (albumId is null || !seen.Add(albumId)) continue;

                var seriesTitle = BedethequeSourceService.CleanScrapedText(link!.SelectSingleNode(".//span[@class='serie']")?.InnerText);
                if (string.IsNullOrEmpty(seriesTitle)) continue;

                var number = CleanNumber(link.SelectSingleNode(".//span[@class='num']")?.InnerText)
                    ?? CleanNumber(link.SelectSingleNode(".//span[@class='numa']")?.InnerText);
                var title = BedethequeSourceService.CleanScrapedText(link.SelectSingleNode(".//span[@class='titre']")?.InnerText);
                var publisher = BedethequeSourceService.CleanScrapedText(li.SelectSingleNode(".//a[contains(@class,'editeur')]")?.InnerText);

                var flagLink = li.SelectSingleNode(".//span[contains(@class,'ico')]//a");
                var category = CategoryFromFlag(flagLink?.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty))
                    ?? CategoryFromOrigine(flagLink?.GetAttributeValue("href", string.Empty));

                var cover = link.GetAttributeValue("rel", string.Empty) is { Length: > 0 } rel ? rel : null;
                DateOnly? releaseDate = dates.TryGetValue(albumId, out var d) ? d : sectionMonth;

                results.Add(new NewsScrapedEntry
                {
                    AlbumId = albumId,
                    SeriesTitle = seriesTitle,
                    AlbumNumber = number,
                    AlbumTitle = string.IsNullOrEmpty(title) ? null : title,
                    Publisher = string.IsNullOrEmpty(publisher) ? null : publisher,
                    ReleaseDate = releaseDate,
                    Category = category,
                    CoverUrl = cover,
                    CoverLargeUrl = BedethequeSourceService.ToLargeCoverUrl(cover),
                    AlbumUrl = href,
                });
            }
        }
        return results;
    }

    internal static Dictionary<string, DateOnly> ParseReleaseDates(HtmlDocument coverDoc)
    {
        var result = new Dictionary<string, DateOnly>();
        var links = coverDoc.DocumentNode.SelectNodes("//a[contains(@title,'Parution le')]");
        if (links is null) return result;

        foreach (var link in links)
        {
            var albumId = ExtractAlbumId(link.GetAttributeValue("href", string.Empty));
            var m = Regex.Match(link.GetAttributeValue("title", string.Empty), @"Parution le\s*:\s*(\d{2}/\d{2}/\d{4})");
            if (albumId is null || !m.Success) continue;
            if (ParseFrenchDate(m.Groups[1].Value) is { } date)
                result.TryAdd(albumId, date);
        }
        return result;
    }

    // " -2- " → "2", " -INT03-" → "INT03", vide → null.
    private static string? CleanNumber(string? raw)
    {
        var value = BedethequeSourceService.CleanScrapedText(raw).Trim('-').Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static NewsCategory? CategoryFromFlag(string? flagSrc)
    {
        var m = Regex.Match(flagSrc ?? string.Empty, @"flags/([^./]+)\.png", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Value.ToLowerInvariant() switch
        {
            "europe" => NewsCategory.Bd,
            "manga" => NewsCategory.Manga,
            "comics" => NewsCategory.Comics,
            _ => null,
        };
    }

    // Paramètre de filtre du site : 1 et 4 = franco-belge, 2 = manga, 3 = comics.
    private static NewsCategory? CategoryFromOrigine(string? href)
    {
        var m = Regex.Match(href ?? string.Empty, @"Origine=(\d)");
        if (!m.Success) return null;
        return m.Groups[1].Value switch
        {
            "1" or "4" => NewsCategory.Bd,
            "2" => NewsCategory.Manga,
            "3" => NewsCategory.Comics,
            _ => null,
        };
    }

    #endregion

    #region Helpers

    // ".../BD-Nestor-Burma-Tome-14-Nestor-Burma-dans-l-ile-543160.html" → "543160".
    internal static string? ExtractAlbumId(string? href)
    {
        var m = Regex.Match(href ?? string.Empty, @"-(\d+)\.html", RegexOptions.IgnoreCase);
        return m.Success && m.Groups[1].Value != "0" ? m.Groups[1].Value : null;
    }

    private static DateOnly? ParseFrenchDate(string? text)
    {
        var m = Regex.Match(text ?? string.Empty, @"(\d{2}/\d{2}/\d{4})");
        return m.Success && DateOnly.TryParseExact(m.Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    private static readonly string[] FrenchMonths =
        ["janvier", "fevrier", "mars", "avril", "mai", "juin", "juillet", "aout", "septembre", "octobre", "novembre", "decembre"];

    // "Les nouveautés de octobre 2026 (36)" → 2026-10-01 (le site écrit "aout" sans accent ; on
    // retire les accents pour couvrir aussi "février"/"décembre").
    internal static DateOnly? ParseFrenchMonth(string? heading)
    {
        var normalized = RemoveDiacritics(BedethequeSourceService.CleanScrapedText(heading)).ToLowerInvariant();
        var m = Regex.Match(normalized, @"([a-z]+)\s+(\d{4})");
        while (m.Success)
        {
            var index = Array.IndexOf(FrenchMonths, m.Groups[1].Value);
            if (index >= 0) return new DateOnly(int.Parse(m.Groups[2].Value), index + 1, 1);
            m = m.NextMatch();
        }
        return null;
    }

    private static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    internal static DateOnly MondayOf(DateOnly date)
        => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    #endregion
}

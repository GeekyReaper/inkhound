using System.Text.Json;
using Foundation.Core.Model;
using Inkhound.Core.Bedetheque;
using Inkhound.Core.DbStorage;
using Inkhound.Core.Models;
using Inkhound.Core.News;
using Microsoft.EntityFrameworkCore;

namespace Inkhound.Core;

/// <summary>
/// Module News : flux top ventes / nouveautés persistés en base (tables <c>NewsAlbums</c> et
/// <c>NewsEntries</c>, historique par période), job de rafraîchissement (relecture des pages liste
/// + enrichissement d'un lot d'albums par flux, déclenché par la tâche <c>News</c> du scheduler ou
/// à la main), lectures paginées pour l'UI et détail live d'un album (caches 24 h de la source).
/// </summary>
public partial class InkhoundManager
{
    /// <summary>Lien vers le volume de la bibliothèque qui suit déjà la série d'un item.</summary>
    public record NewsLibraryLink(Guid LibraryId, Guid VolumeId);

    /// <summary>Item d'un flux : album, apparition dans le flux (rang…), enrichissement et statut bibliothèque.</summary>
    public record NewsItem(NewsAlbum Album, NewsEntry? Entry, NewsAlbumEnrichment? Enrichment, NewsLibraryLink? Library);

    /// <summary>Classement d'une semaine : semaines disponibles (plus récente d'abord), semaine affichée, items par rang.</summary>
    public record NewsTopSalesResult(IReadOnlyList<string> Periods, string? Period, IReadOnlyList<NewsItem> Items);

    /// <summary>Page de nouveautés + mois disponibles (plus récent d'abord).</summary>
    public record NewsReleasesResult(IReadOnlyList<string> Months, Page<NewsItem> Page);

    /// <summary>Détail d'un album : données en base (si l'album est apparu dans un flux), enrichissement, série, statut bibliothèque.</summary>
    public record NewsAlbumDetailResult(
        NewsAlbum? Album, NewsAlbumEnrichment Enrichment, NewsSeriesDetail? Series, NewsLibraryLink? Library,
        IReadOnlyList<NewsEntry> Appearances);

    // Nombre de semaines de classement proposées dans le sélecteur d'historique.
    private const int NewsMaxPeriods = 104;

    // Un enrichissement plus ancien est rejoué à l'ouverture de la page détail (planches ajoutées,
    // note mise à jour…) — la lecture passe par le cache 24 h de la source.
    private static readonly TimeSpan NewsEnrichmentMaxAge = TimeSpan.FromDays(7);

    // Un seul job News à la fois (manuel ou planifié) — deux jobs scraperaient les mêmes pages.
    private int _newsRefreshRunning;

    private NewsService News => GetService<NewsService, NewsOptions>();

    #region Job

    /// <summary>
    /// Lance le job News et retourne immédiatement son <see cref="JobContext"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Un job News est déjà en cours.</exception>
    public JobContext LaunchJobRefreshNews(RefreshNewsJobParameters parameters)
    {
        if (Interlocked.CompareExchange(ref _newsRefreshRunning, 1, 0) != 0)
            throw new InvalidOperationException("A news refresh is already running.");

        // StartJob ici (pas dans un helper async) : le job courant est un AsyncLocal.
        var job = StartJob("News — refresh feeds", parameters);
        if (job.State == JobState.ERROR)
        {
            Interlocked.Exchange(ref _newsRefreshRunning, 0);
            return job;
        }

        job.SetState(JobState.RUNNING);
        _ = RunRefreshNewsJobAsync(job, parameters);
        return job;
    }

    // Variante planifiée : même job, awaité pour que la garde _schedulerBusy couvre l'exécution.
    private async Task RunScheduledNewsAsync()
    {
        if (Interlocked.CompareExchange(ref _newsRefreshRunning, 1, 0) != 0)
        {
            JobSendTrace("[Scheduler] News — a refresh is already running", ETraceLevel.WARNING);
            return;
        }

        var parameters = new RefreshNewsJobParameters
        {
            EnrichBatchSize = GetService<SchedulerService, SchedulerOptions>().NewsEnrichBatchSize
        };
        var job = StartJob("News — refresh feeds", parameters);
        if (job.State == JobState.ERROR)
        {
            Interlocked.Exchange(ref _newsRefreshRunning, 0);
            return;
        }

        job.SetState(JobState.RUNNING);
        await RunRefreshNewsJobAsync(job, parameters);
    }

    private async Task RunRefreshNewsJobAsync(JobContext job, RefreshNewsJobParameters parameters)
    {
        try
        {
            if ((await Bedetheque.GetState()).State != EState.OK)
            {
                JobSendTrace("[News] Bedetheque service unavailable — refresh aborted", ETraceLevel.ERROR);
                EndJob(false);
                return;
            }

            var news = News;
            var provider = news.Provider;
            var interval = TimeSpan.FromHours(news.ListRefreshIntervalHours);
            var refreshTopSales = parameters.ForceListRefresh || await IsNewsFeedStaleAsync(provider.ProviderKey, NewsFeed.TopSales, interval);
            var refreshReleases = parameters.ForceListRefresh || await IsNewsFeedStaleAsync(provider.ProviderKey, NewsFeed.Releases, interval);

            job.CallbackHandler.UpdateTotal((refreshTopSales ? 1 : 0) + (refreshReleases ? 1 : 0) + 2 * parameters.EnrichBatchSize);
            var errors = 0;

            try
            {
                if (refreshTopSales)
                {
                    errors += await RunNewsStepAsync(job, "Top sales list", async () =>
                    {
                        var snapshot = await provider.FetchTopSalesAsync();
                        if (snapshot is null || snapshot.Entries.Count == 0)
                            throw new InvalidOperationException("no ranking found on the page");
                        await UpsertNewsEntriesAsync(provider.ProviderKey, NewsFeed.TopSales,
                            snapshot.Entries.Select(e => (snapshot.Week.ToString("yyyy-MM-dd"), e)));
                        JobSendTrace($"[News] Top sales — week of {snapshot.Week:yyyy-MM-dd}, {snapshot.Entries.Count} album(s)");
                    });
                }
                else JobSendTrace("[News] Top sales list is fresh — skipped");

                if (refreshReleases)
                {
                    errors += await RunNewsStepAsync(job, "New releases list", async () =>
                    {
                        var entries = await provider.FetchReleasesAsync();
                        if (entries.Count == 0)
                            throw new InvalidOperationException("no release found on the page");
                        await UpsertNewsEntriesAsync(provider.ProviderKey, NewsFeed.Releases,
                            entries.Select(e => (NewsReleasePeriod(e.ReleaseDate), e)));
                        JobSendTrace($"[News] New releases — {entries.Count} album(s)");
                    });
                }
                else JobSendTrace("[News] New releases list is fresh — skipped");

                foreach (var feed in new[] { NewsFeed.TopSales, NewsFeed.Releases })
                {
                    var candidates = await GetNewsEnrichCandidatesAsync(provider.ProviderKey, feed, parameters.EnrichBatchSize, news.MaxEnrichAttempts);
                    JobSendTrace($"[News] {feed} — {candidates.Count} album(s) to enrich");
                    foreach (var album in candidates)
                    {
                        var ok = await EnrichNewsAlbumAsync(provider, album.AlbumId) is not null;
                        if (!ok) errors++;
                        job.Progress.Increment(ok);
                        job.CallbackHandler.Callback(job.Progress);
                    }
                    // Lot incomplet : la barre de progression doit quand même aboutir.
                    for (var i = candidates.Count; i < parameters.EnrichBatchSize; i++)
                        job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                }
            }
            catch (BedethequeBlockedException ex)
            {
                // Bloqué par le site : inutile d'insister sur la suite du lot.
                JobSendTrace($"[News] Blocked by the site: {ex.Message} — refresh stopped", ETraceLevel.ERROR);
                errors++;
            }

            EndJob(errors == 0);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[News] Unexpected error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
        finally
        {
            Interlocked.Exchange(ref _newsRefreshRunning, 0);
        }
    }

    // Exécute une étape de lecture de liste ; un blocage remonte (arrêt du job), toute autre erreur
    // est tracée et comptée sans interrompre les étapes suivantes.
    private async Task<int> RunNewsStepAsync(JobContext job, string label, Func<Task> action)
    {
        var failed = 0;
        try
        {
            await JobRunTimedAsync($"[News] {label}", action);
        }
        catch (BedethequeBlockedException) { throw; }
        catch (Exception)
        {
            failed = 1;
        }
        job.Progress.Increment(failed == 0);
        job.CallbackHandler.Callback(job.Progress);
        return failed;
    }

    private async Task<bool> IsNewsFeedStaleAsync(string provider, NewsFeed feed, TimeSpan interval)
    {
        var last = await GetDb().NewsEntries
            .Where(e => e.Provider == provider && e.Feed == feed)
            .MaxAsync(e => (DateTime?)e.FetchedAtUtc);
        return last is null || DateTime.UtcNow - last.Value >= interval;
    }

    // Mois de sortie "yyyy-MM" — "unknown" si la source n'a pas donné de date.
    private static string NewsReleasePeriod(DateOnly? releaseDate)
        => releaseDate is { } d ? d.ToString("yyyy-MM") : "unknown";

    // Insère/met à jour les albums (champs « liste » uniquement — une valeur absente n'écrase jamais
    // une valeur connue, ex. la catégorie d'un album du top ventes vue dans les nouveautés) et leurs
    // apparitions (unicité Provider/Feed/Period/AlbumId).
    private async Task UpsertNewsEntriesAsync(string provider, NewsFeed feed, IEnumerable<(string Period, NewsScrapedEntry Entry)> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;

        var now = DateTime.UtcNow;
        var ctx = GetDb();
        var albumIds = list.Select(i => i.Entry.AlbumId).Distinct().ToList();
        var periods = list.Select(i => i.Period).Distinct().ToList();

        var albums = await ctx.NewsAlbums
            .Where(a => a.Provider == provider && albumIds.Contains(a.AlbumId))
            .ToDictionaryAsync(a => a.AlbumId);
        var entries = await ctx.NewsEntries
            .Where(e => e.Provider == provider && e.Feed == feed && periods.Contains(e.Period) && albumIds.Contains(e.AlbumId))
            .ToListAsync();

        foreach (var (period, scraped) in list)
        {
            if (!albums.TryGetValue(scraped.AlbumId, out var album))
            {
                album = new NewsAlbum { Provider = provider, AlbumId = scraped.AlbumId, FirstSeenUtc = now };
                albums[scraped.AlbumId] = album;
                ctx.NewsAlbums.Add(album);
            }

            if (!string.IsNullOrEmpty(scraped.SeriesTitle)) album.SeriesTitle = scraped.SeriesTitle;
            album.AlbumNumber = scraped.AlbumNumber ?? album.AlbumNumber;
            album.AlbumTitle = scraped.AlbumTitle ?? album.AlbumTitle;
            album.Publisher = scraped.Publisher ?? album.Publisher;
            album.ReleaseDate = scraped.ReleaseDate ?? album.ReleaseDate;
            album.Category = scraped.Category ?? album.Category;
            album.ShortDescription = scraped.ShortDescription ?? album.ShortDescription;
            album.CoverUrl = scraped.CoverUrl ?? album.CoverUrl;
            album.CoverLargeUrl = scraped.CoverLargeUrl ?? album.CoverLargeUrl;
            album.AlbumUrl = scraped.AlbumUrl ?? album.AlbumUrl;
            album.LastSeenUtc = now;

            var entry = entries.FirstOrDefault(e => e.Period == period && e.AlbumId == scraped.AlbumId);
            if (entry is null)
            {
                entry = new NewsEntry { Id = Guid.NewGuid(), Provider = provider, Feed = feed, AlbumId = scraped.AlbumId, Period = period };
                entries.Add(entry);
                ctx.NewsEntries.Add(entry);
            }
            entry.Rank = scraped.Rank;
            entry.Evolution = scraped.Evolution;
            entry.EvolutionDelta = scraped.EvolutionDelta;
            entry.WeeksInChart = scraped.WeeksInChart;
            entry.FetchedAtUtc = now;
        }

        await ctx.SaveChangesAsync();
    }

    // Top ventes : albums de la semaine la plus récente, par rang. Nouveautés : date de sortie
    // décroissante (déjà parus d'abord, puis à paraître). Albums déjà enrichis ou en échec répété exclus.
    private async Task<List<NewsAlbum>> GetNewsEnrichCandidatesAsync(string provider, NewsFeed feed, int batchSize, int maxAttempts)
    {
        if (batchSize < 1) return [];
        var ctx = GetDb();
        var pending = ctx.NewsAlbums.Where(a => a.Provider == provider && a.EnrichedAtUtc == null && a.EnrichAttempts < maxAttempts);

        if (feed == NewsFeed.TopSales)
        {
            var ranked = await ctx.NewsEntries
                .Where(e => e.Provider == provider && e.Feed == NewsFeed.TopSales)
                .Join(pending, e => e.AlbumId, a => a.AlbumId, (e, a) => new { e.Period, e.Rank, Album = a })
                .OrderByDescending(x => x.Period).ThenBy(x => x.Rank)
                .Take(batchSize * 4)
                .ToListAsync();
            return ranked.Select(x => x.Album).DistinctBy(a => a.AlbumId).Take(batchSize).ToList();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var released = await pending
            .Where(a => ctx.NewsEntries.Any(e => e.Provider == provider && e.Feed == NewsFeed.Releases && e.AlbumId == a.AlbumId))
            .Where(a => a.ReleaseDate == null || a.ReleaseDate <= today)
            .OrderByDescending(a => a.ReleaseDate)
            .Take(batchSize)
            .ToListAsync();
        if (released.Count >= batchSize) return released;

        var upcoming = await pending
            .Where(a => ctx.NewsEntries.Any(e => e.Provider == provider && e.Feed == NewsFeed.Releases && e.AlbumId == a.AlbumId))
            .Where(a => a.ReleaseDate > today)
            .OrderBy(a => a.ReleaseDate)
            .Take(batchSize - released.Count)
            .ToListAsync();
        return [.. released, .. upcoming];
    }

    // Enrichit un album et persiste le résultat (si l'album est connu en base). null = introuvable
    // ou erreur (tentative comptée) ; un blocage du site remonte tel quel sans compter de tentative.
    private async Task<NewsAlbumEnrichment?> EnrichNewsAlbumAsync(INewsProvider provider, string albumId)
    {
        NewsAlbumEnrichment? enrichment = null;
        try
        {
            enrichment = await provider.EnrichAlbumAsync(albumId);
            if (enrichment is null)
                JobSendTrace($"[News] Album {albumId} not found", ETraceLevel.WARNING);
        }
        catch (BedethequeBlockedException) { throw; }
        catch (Exception ex)
        {
            JobSendTrace($"[News] Album {albumId} enrichment failed: {ex.Message}", ETraceLevel.WARNING);
        }

        var ctx = GetDb();
        var album = await ctx.NewsAlbums.FirstOrDefaultAsync(a => a.Provider == provider.ProviderKey && a.AlbumId == albumId);
        if (album is null) return enrichment;

        if (enrichment is null)
        {
            album.EnrichAttempts++;
        }
        else
        {
            ApplyNewsEnrichment(album, enrichment);
            JobSendTrace($"[News] Enriched {album.SeriesTitle} {album.AlbumNumber} — series {enrichment.SeriesId}");
        }
        await ctx.SaveChangesAsync();
        return enrichment;
    }

    private static void ApplyNewsEnrichment(NewsAlbum album, NewsAlbumEnrichment enrichment)
    {
        album.SeriesId = enrichment.SeriesId;
        album.EnrichmentJson = JsonSerializer.Serialize(enrichment);
        album.EnrichedAtUtc = DateTime.UtcNow;
        album.EnrichAttempts = 0;
        if (string.IsNullOrEmpty(album.SeriesTitle)) album.SeriesTitle = enrichment.SeriesTitle;
        album.AlbumTitle ??= enrichment.AlbumTitle;
        album.AlbumNumber ??= enrichment.AlbumNumber;
        album.Publisher ??= enrichment.Publisher;
        album.CoverUrl ??= enrichment.CoverUrl;
        if (enrichment.CoverLargeUrl is not null) album.CoverLargeUrl = enrichment.CoverLargeUrl;
    }

    private static NewsAlbumEnrichment? ReadNewsEnrichment(NewsAlbum album)
    {
        if (string.IsNullOrEmpty(album.EnrichmentJson)) return null;
        try { return JsonSerializer.Deserialize<NewsAlbumEnrichment>(album.EnrichmentJson); }
        catch (JsonException) { return null; }
    }

    #endregion

    #region Lectures

    /// <summary>Classement de la semaine <paramref name="period"/> (<c>yyyy-MM-dd</c>), ou de la plus récente.</summary>
    public async Task<NewsTopSalesResult> GetNewsTopSalesAsync(string? period)
    {
        var provider = News.Provider.ProviderKey;
        var ctx = GetDb();
        var periods = await ctx.NewsEntries
            .Where(e => e.Provider == provider && e.Feed == NewsFeed.TopSales)
            .Select(e => e.Period).Distinct()
            .OrderByDescending(p => p)
            .Take(NewsMaxPeriods)
            .ToListAsync();

        var selected = period is not null && periods.Contains(period) ? period : periods.FirstOrDefault();
        if (selected is null) return new NewsTopSalesResult(periods, null, []);

        var rows = await ctx.NewsEntries
            .Where(e => e.Provider == provider && e.Feed == NewsFeed.TopSales && e.Period == selected)
            .Join(ctx.NewsAlbums.Where(a => a.Provider == provider), e => e.AlbumId, a => a.AlbumId, (e, a) => new { Entry = e, Album = a })
            .OrderBy(x => x.Entry.Rank)
            .ToListAsync();

        var items = await BuildNewsItemsAsync(ctx, provider, rows.Select(r => (r.Album, (NewsEntry?)r.Entry)).ToList());
        return new NewsTopSalesResult(periods, selected, items);
    }

    /// <summary>
    /// Nouveautés, date de sortie décroissante, filtrées par catégorie et/ou mois (<c>yyyy-MM</c>).
    /// </summary>
    public async Task<NewsReleasesResult> GetNewsReleasesAsync(NewsCategory? category, string? month, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var provider = News.Provider.ProviderKey;
        var ctx = GetDb();

        var months = await ctx.NewsEntries
            .Where(e => e.Provider == provider && e.Feed == NewsFeed.Releases && e.Period != "unknown")
            .Select(e => e.Period).Distinct()
            .OrderByDescending(p => p)
            .ToListAsync();

        var releaseEntries = ctx.NewsEntries.Where(e => e.Provider == provider && e.Feed == NewsFeed.Releases);
        if (!string.IsNullOrEmpty(month))
            releaseEntries = releaseEntries.Where(e => e.Period == month);

        var query = ctx.NewsAlbums.Where(a => a.Provider == provider && releaseEntries.Any(e => e.AlbumId == a.AlbumId));
        if (category is { } c)
            query = query.Where(a => a.Category == c);

        var total = await query.CountAsync();
        var albums = await query
            .OrderByDescending(a => a.ReleaseDate)
            .ThenBy(a => a.SeriesTitle)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = await BuildNewsItemsAsync(ctx, provider, albums.Select(a => (a, (NewsEntry?)null)).ToList());
        return new NewsReleasesResult(months, new Page<NewsItem>
        {
            Items = items,
            PageNumber = page,
            PageSize = pageSize,
            TotalItems = total,
        });
    }

    /// <summary>
    /// Détail d'un album : enrichissement (rejoué en direct s'il est absent ou ancien — cache 24 h de
    /// la source), fiche de la série (en direct, <c>null</c> si la source est indisponible),
    /// statut bibliothèque et apparitions dans les flux.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Album introuvable chez la source.</exception>
    public async Task<NewsAlbumDetailResult> GetNewsAlbumDetailAsync(string albumId, CancellationToken ct = default)
    {
        var provider = News.Provider;
        var ctx = GetDb();
        var album = await ctx.NewsAlbums.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Provider == provider.ProviderKey && a.AlbumId == albumId, ct);

        var enrichment = album is not null ? ReadNewsEnrichment(album) : null;
        if (enrichment is null || album?.EnrichedAtUtc is not { } at || DateTime.UtcNow - at > NewsEnrichmentMaxAge)
        {
            try
            {
                enrichment = await EnrichNewsAlbumAsync(provider, albumId) ?? enrichment;
            }
            catch (BedethequeBlockedException) when (enrichment is not null)
            {
                // Source bloquée : on se contente de l'enrichissement déjà en base.
            }
            if (album is not null)
                album = await ctx.NewsAlbums.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Provider == provider.ProviderKey && a.AlbumId == albumId, ct);
        }
        if (enrichment is null)
            throw new KeyNotFoundException($"Album '{albumId}' not found.");

        NewsSeriesDetail? series = null;
        try
        {
            series = await provider.GetSeriesAsync(enrichment.SeriesId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            JobSendTrace($"[News] Series {enrichment.SeriesId} unavailable: {ex.Message}", ETraceLevel.WARNING);
        }

        var links = await GetNewsLibraryLinksAsync(ctx, provider.ProviderKey, [enrichment.SeriesId]);
        var appearances = await ctx.NewsEntries.AsNoTracking()
            .Where(e => e.Provider == provider.ProviderKey && e.AlbumId == albumId)
            .OrderByDescending(e => e.Period)
            .ToListAsync(ct);

        return new NewsAlbumDetailResult(album, enrichment, series,
            links.GetValueOrDefault(enrichment.SeriesId), appearances);
    }

    /// <summary>
    /// Résout (enrichissement à la demande si nécessaire) la série d'un album — préalable au bouton
    /// « Add » sur un item pas encore enrichi par le job.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Album introuvable chez la source.</exception>
    public async Task<(string SeriesId, NewsLibraryLink? Library)> ResolveNewsAlbumSeriesAsync(string albumId)
    {
        var provider = News.Provider;
        var ctx = GetDb();
        var seriesId = await ctx.NewsAlbums
            .Where(a => a.Provider == provider.ProviderKey && a.AlbumId == albumId)
            .Select(a => a.SeriesId)
            .FirstOrDefaultAsync();

        seriesId ??= (await EnrichNewsAlbumAsync(provider, albumId))?.SeriesId
            ?? throw new KeyNotFoundException($"Album '{albumId}' not found.");

        var links = await GetNewsLibraryLinksAsync(ctx, provider.ProviderKey, [seriesId]);
        return (seriesId, links.GetValueOrDefault(seriesId));
    }

    private async Task<List<NewsItem>> BuildNewsItemsAsync(DbStorageContext ctx, string provider, List<(NewsAlbum Album, NewsEntry? Entry)> rows)
    {
        var seriesIds = rows.Select(r => r.Album.SeriesId).OfType<string>().Distinct().ToList();
        var links = await GetNewsLibraryLinksAsync(ctx, provider, seriesIds);
        return rows
            .Select(r => new NewsItem(r.Album, r.Entry, ReadNewsEnrichment(r.Album),
                r.Album.SeriesId is { } sid ? links.GetValueOrDefault(sid) : null))
            .ToList();
    }

    // Une seule requête pour tout le lot : volumes de la source dont l'id de série est dans la liste
    // (SourceType toujours en minuscules pour Bedetheque, voir Mapper.Map(BdSerie)).
    private static async Task<Dictionary<string, NewsLibraryLink>> GetNewsLibraryLinksAsync(
        DbStorageContext ctx, string provider, IReadOnlyCollection<string> seriesIds)
    {
        if (seriesIds.Count == 0) return new();
        var volumes = await ctx.Volumes
            .Where(v => v.SourceType.ToLower() == provider && seriesIds.Contains(v.SourceId))
            .Select(v => new { v.SourceId, v.LibraryId, v.Id })
            .ToListAsync();
        return volumes
            .GroupBy(v => v.SourceId)
            .ToDictionary(g => g.Key, g => new NewsLibraryLink(g.First().LibraryId, g.First().Id));
    }

    #endregion
}

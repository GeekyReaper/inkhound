using Inkhound.Core.ApiTokens;
using Inkhound.Core.Bedetheque;
using Inkhound.Core.Chatbot;
using Inkhound.Core.Bedetheque.Catalog;
using Inkhound.Core.ComicVine;
using Inkhound.Core.Models;
using Inkhound.Core.News;
using Inkhound.Core.Security;
using Inkhound.Core.Sources;
using Foundation.Core.Model;
using Foundation.Core;
using Foundation.Core.Interface;

using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using Inkhound.Core.DbStorage;
using Inkhound.Core.ComicArchiveGenerator;
using Inkhound.Core.Kavita;
using Inkhound.Core.Kavita.Models;
using Inkhound.Core.Prowlarr;
using Inkhound.Core.QBittorrent;
using Inkhound.Core.WebshareProxy;
using Inkhound.Core.Scoring;
using Inkhound.Core.Analysis;
using Inkhound.Core.CbzQuality.Analysis;
using Inkhound.Core.CbzQuality.Models;

using SharpCompress.Compressors.ZStandard.Unsafe;
using System.Text.Json;

namespace Inkhound.Core;

public partial class InkhoundManager : BaseServiceManager
{
    private readonly string _dbPath;

    // Bornes communes aux trois caches de résultats de jobs (recherche multi-source, Prowlarr par
    // issue, Prowlarr par volume). La durée est calée sur BaseServiceManager.JobRetention : passé
    // ce délai le job lui-même n'est plus consultable, son résultat n'a donc plus de lecteur.
    private static readonly TimeSpan JobResultRetention = TimeSpan.FromMinutes(15);
    private const int JobResultMaxEntries = 32;

    //public InkhoundDbContext? Database { get; private set; }

    public InkhoundManager(string dbPath = "data/inkhound.db")
    {
        _dbPath = dbPath;

        // Purge périodique des entrées expirées par la boucle de monitoring, et vidage à la demande
        // via PurgeAllCaches (voir l'API /api/system/memory).
        RegisterCache(_searchResults);
        RegisterCache(_prowlarrResults);
        RegisterCache(_prowlarrVolumeResults);
    }

    // Checks if the SQLite database exists and creates it if needed, then sets the Database property



    public StateServiceManager GetCurrentState() => CurrentState;

    #region Internal Service Management
    public List<string> GetServiceNames()
        => [.. Services.Values.Select(s => s.GetServiceName())];


    // Chaque appel retourne une nouvelle instance de DbStorageContext (voir DbStorageService.Database) —
    // ne jamais la faire persister au-delà d'une seule méthode / d'une seule requête.
    private DbStorageContext GetDb()
        => GetService<DbStorageService, DbStorageOption>().Database
           ?? throw new InvalidOperationException("Database is not initialized.");


    public List<OptionDefinition> GetOptionsForService(string serviceName)
    {
        var db = GetService<DbStorageService, DbStorageOption>();
        return db.GetOptionsForService(serviceName);
    }

    public async Task<bool> UpdateOptionsForService(string serviceName, Dictionary<string, string> updates)
    {
        var db = GetService<DbStorageService, DbStorageOption>();
        var existing = db.GetOptionsForService(serviceName);

        foreach (var option in existing)
        {
            if (updates.TryGetValue(option.Name, out var value))
                option.Value = value;
        }

        if (!db.SetOptionsForService(existing)) return false;

        var service = Services.Values.FirstOrDefault(s => s.GetServiceName() == serviceName);
        if (service is not null)
            await service.LoadOptions(existing);

        return true;
    }

    // Force un recalcul immédiat de l'état d'un service (bypass le cache de StateRefreshDelay).
    // Utile pour les services scrapés (ex. Bedetheque, mis en cache 180 min) après un changement
    // de configuration (proxy activé, etc.) : sans ça, un état ERROR resterait affiché jusqu'à
    // expiration du cache même une fois le problème résolu.
    public async Task<StateService?> RefreshServiceStateAsync(string serviceName)
    {
        var service = Services.Values.FirstOrDefault(s => s.GetServiceName() == serviceName);
        return service is null ? null : await service.GetState(force: true);
    }

    public async Task AutomaticLoadServices()
    {
        // Instantiate services
        // WebshareProxy first so it's already registered as the active proxy provider before the
        // other services build their first HttpClient.
        GetService<WebshareProxyService, WebshareProxyOptions>();
        var databaseService = GetService<DbStorageService, DbStorageOption>();
        var comicVine = GetService<ComicVineSourceService, ComicVineOptions>();
        var bedetheque = GetService<BedethequeSourceService, BedethequeOptions>();
        var archiveService = GetService<ArchiveService, ArchiveOption>();
        var kavitaService = GetService<KavitaService, KavitaOptions>();
        GetService<ProwlarrService, ProwlarrOptions>();
        GetService<QBittorrentService, QBittorrentOptions>();
        GetService<ApiTokenService, ApiTokenOptions>();
        GetService<SchedulerService, SchedulerOptions>();

        // La passerelle doit être attachée AVANT la boucle de chargement des options ci-dessous :
        // c'est ce LoadOptions qui construit les commandes du bot et démarre la boucle Matrix si le
        // module est activé, et elles ont besoin d'un accès au domaine.
        GetService<ChatbotService, ChatbotOptions>().Attach(new InkhoundChatbotGateway(this));

        // Module News : la source d'actualité réutilise la pile HTTP et les caches de Bedetheque ;
        // son état reflète celui de Bedetheque (état en cache, aucune requête supplémentaire).
        GetService<NewsService, NewsOptions>().Attach(new BedethequeNewsProvider(bedetheque), () => bedetheque.CurrentState);

        // Load database options and initialize database
        var databaseoption = new DbStorageOption { Path = _dbPath, UseInMemory = false };

        if (await databaseService.LoadOptions(databaseoption.GetOptions()))
        {
            _hasUsers = await GetDb().Users.AnyAsync();

            foreach (var service in Services)
            {
                if (databaseService.GetServiceName() != service.Value.GetServiceName())
                {
                    // Try to load stored options for this service from the database
                    var storedOptions = databaseService.GetOptionsForService(service.Value.GetServiceName());
                    var currentOptions = service.Value.GetOptions();

                    if (storedOptions.Count > 0)
                    {
                        // Complète avec les définitions d'option ajoutées depuis le dernier démarrage, et
                        // purge celles devenues obsolètes (renommées/supprimées côté code) — sinon une
                        // nouvelle option n'apparaîtrait jamais en base ni côté UI, et une option renommée
                        // laisserait un doublon orphelin. SetOptionsForService supprime toute option absente
                        // de la liste passée : on doit donc lui fournir l'ensemble stocké + les manquantes
                        // (moins les obsolètes), jamais un sous-ensemble seul.
                        var missingOptions = currentOptions.Where(c => !storedOptions.Any(s => s.Name == c.Name)).ToList();
                        var obsoleteOptions = storedOptions.Where(s => !currentOptions.Any(c => c.Name == s.Name)).ToList();

                        // SortOrder et Section sont pilotés par le code, pas par l'utilisateur : on les
                        // resynchronise sur les options déjà stockées à chaque démarrage, même en
                        // l'absence d'ajout/suppression.
                        var driftedOptions = storedOptions
                            .Join(currentOptions, s => s.Name, c => c.Name, (stored, current) => (stored, current))
                            .Where(pair => pair.stored.SortOrder != pair.current.SortOrder
                                        || pair.stored.Section != pair.current.Section)
                            .ToList();
                        foreach (var (stored, current) in driftedOptions)
                        {
                            stored.SortOrder = current.SortOrder;
                            stored.Section = current.Section;
                        }

                        if (missingOptions.Count > 0 || obsoleteOptions.Count > 0 || driftedOptions.Count > 0)
                        {
                            var merged = storedOptions.Except(obsoleteOptions).Concat(missingOptions).ToList();
                            databaseService.Database?.SetOptionsForService(merged, service.Value.GetServiceName());
                            storedOptions = merged;
                        }

                        await service.Value.LoadOptions(storedOptions);
                    }
                    else
                    { // No stored options, save current defaults to database
                        databaseService.Database?.SetOptionsForService(currentOptions, service.Value.GetServiceName());
                    }
                }
            }

            // Index mémoire du catalogue local Bedetheque — la table existe (migrations jouées par
            // DbStorageService.LoadOptions → CheckInternalState) et les options Bedetheque sont chargées.
            try
            {
                await LoadBedethequeCatalogAsync();
            }
            catch (Exception ex)
            {
                JobSendTrace($"[Bedetheque] Catalog load failed: {ex.Message}", ETraceLevel.ERROR);
            }
        }

        // Démarre la boucle de planification cron une fois les options de tous les services chargées.
        StartScheduler();
    }

    public async Task ManuelLoadServiceComicvine(ComicVineOptions options)
    {
        var comicVine = GetService<ComicVineSourceService, ComicVineOptions>();
        await comicVine.LoadOptions(options.GetOptions());

    }


    public async Task ManuelLoadServiceDbStorage(DbStorageOption options)
    {
        var database = GetService<DbStorageService, DbStorageOption>();
        await database.LoadOptions(options.GetOptions());

    }

    #endregion

    #region Library CRUD  

    public async Task<List<Library>> GetLibrariesAsync()
    {
        return await GetDb().Libraries.ToListAsync();
    }

    public async Task<Library?> GetLibraryAsync(Guid id)
    {
        return await GetDb().Libraries.FindAsync(id);
    }

    // Les compteurs d'issues sont établis sur les issues réelles, toutes catégories confondues —
    // et non sur Volume.CountOfIssues/CountOfDownloadedIssues, qui ne retiennent que les issues
    // Standard (ils mesurent la complétion d'une série). L'encart Libraries du Dashboard s'aligne
    // ainsi sur la carte « Issues » du haut, qui compte elle aussi toutes les issues.
    public record DashboardLibraryStats(
        Guid Id, string Name, int VolumesCount,
        int IssuesCount, int DownloadedIssuesCount, int DownloadingIssuesCount, int MissingIssuesCount);

    // Une issue MISSING (catégorie Standard) dont l'acquisition ferait passer son Volume à 100 %
    // de complétude, ou très proche. Alimente la section « Most wanted » du Dashboard.
    public record DashboardMostWantedIssue(
        Guid IssueId, Guid VolumeId, Guid LibraryId, string VolumeTitle,
        VolumeImage? Image,
        int IssueNumber, string? IssueTitle,
        int OwnedCount, int TotalCount, int MissingCount,
        int CurrentCompletionPercent, int ProjectedCompletionPercent);

    public record DashboardStats(
        int LibrariesCount,
        int VolumesCount, int VolumesMonitored, int VolumesCompleted, int VolumesPaused,
        int IssuesCount, int IssuesDownloaded, int IssuesDownloading, int IssuesMissing,
        long TotalDownloadedBytes,
        List<DashboardLibraryStats> Libraries,
        List<Volume> RecentVolumes,
        List<DashboardMostWantedIssue> MostWanted);

    // « Most wanted » : un volume est éligible s'il est MONITORED et qu'il ne lui manque pas plus
    // de MostWantedMaxMissing issues Standard ; MostWantedRowLimit lignes sont renvoyées.
    private const int MostWantedMaxMissing = 2;
    private const int MostWantedRowLimit   = 6;

    // Vue d'ensemble toutes bibliothèques confondues pour la page Dashboard — aucun agrégat de
    // ce type n'existait jusqu'ici (les autres méthodes de lecture sont scopées à une
    // library/un volume). Lecture simple, pas un Job.
    public async Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        var ctx = GetDb();

        var librariesCount = await ctx.Libraries.CountAsync(ct);

        var volumesCount     = await ctx.Volumes.CountAsync(ct);
        var volumesMonitored = await ctx.Volumes.CountAsync(v => v.Status == VolumeStatus.MONITORED, ct);
        var volumesCompleted = await ctx.Volumes.CountAsync(v => v.Status == VolumeStatus.COMPLETED, ct);
        var volumesPaused    = await ctx.Volumes.CountAsync(v => v.Status == VolumeStatus.PAUSED, ct);

        var issuesCount       = await ctx.Issues.CountAsync(ct);
        var issuesDownloaded  = await ctx.Issues.CountAsync(i => i.Status == IssueStatus.DOWNLOADED, ct);
        var issuesDownloading = await ctx.Issues.CountAsync(i => i.Status == IssueStatus.DOWNLOADING, ct);
        var issuesMissing     = await ctx.Issues.CountAsync(i => i.Status == IssueStatus.MISSING, ct);

        // FileSizeBytes est un int — cast en long avant SUM pour éviter un dépassement sur une
        // grosse bibliothèque (le total cumulé dépasse largement int.MaxValue).
        var totalDownloadedBytes = await ctx.Issues
            .Where(i => i.Status == IssueStatus.DOWNLOADED)
            .SumAsync(i => (long)i.FileSizeBytes, ct);

        var libraries = await ctx.Libraries.ToListAsync(ct);

        // Deux requêtes groupées plutôt qu'un aller-retour par bibliothèque : Volume/Issue n'ont
        // aucune navigation EF déclarée (FK scalaires), d'où les jointures explicites.
        var volumeCountByLibrary = (await ctx.Volumes
                .GroupBy(v => v.LibraryId)
                .Select(g => new { LibraryId = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.LibraryId, x => x.Count);

        var issueCounts = await (from i in ctx.Issues
                                 join v in ctx.Volumes on i.VolumeId equals v.Id
                                 group i by new { v.LibraryId, i.Status } into g
                                 select new { g.Key.LibraryId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var libraryStats = libraries.Select(lib =>
        {
            int CountFor(IssueStatus status) => issueCounts
                .FirstOrDefault(c => c.LibraryId == lib.Id && c.Status == status)?.Count ?? 0;

            var downloaded  = CountFor(IssueStatus.DOWNLOADED);
            var downloading = CountFor(IssueStatus.DOWNLOADING);
            var missing     = CountFor(IssueStatus.MISSING);

            return new DashboardLibraryStats(
                lib.Id, lib.Name,
                volumeCountByLibrary.GetValueOrDefault(lib.Id),
                downloaded + downloading + missing,
                downloaded, downloading, missing);
        }).ToList();

        var recentVolumes = await ctx.Volumes
            .OrderByDescending(v => v.DateAdded)
            .Take(6)
            .ToListAsync(ct);

        // ---- Most wanted : issues MISSING dont l'acquisition rapproche un volume de 100 % ----

        // 1. Nb d'issues Standard MISSING par volume, borné à <= K directement en SQL (GROUP BY).
        var missingByVolume = await ctx.Issues
            .Where(i => i.Status == IssueStatus.MISSING && i.Category == IssueCategory.Standard)
            .GroupBy(i => i.VolumeId)
            .Select(g => new { VolumeId = g.Key, MissingCount = g.Count() })
            .Where(x => x.MissingCount <= MostWantedMaxMissing)
            .ToListAsync(ct);

        var candidateVolumeIds = missingByVolume.Select(x => x.VolumeId).ToList();

        // 2. Restreindre aux volumes réellement suivis et incomplets.
        var eligibleVolumes = await ctx.Volumes
            .Where(v => candidateVolumeIds.Contains(v.Id)
                     && v.Status == VolumeStatus.MONITORED
                     && v.CountOfIssues > 0
                     && v.CountOfDownloadedIssues < v.CountOfIssues)
            .ToListAsync(ct);

        var volumeById           = eligibleVolumes.ToDictionary(v => v.Id);
        var missingCountByVolume = missingByVolume.ToDictionary(x => x.VolumeId, x => x.MissingCount);
        var eligibleIds          = eligibleVolumes.Select(v => v.Id).ToList();

        // 3. Charger uniquement les issues MISSING Standard de ces volumes (<= K par volume).
        var missingIssues = await ctx.Issues
            .Where(i => eligibleIds.Contains(i.VolumeId)
                     && i.Status == IssueStatus.MISSING
                     && i.Category == IssueCategory.Standard)
            .ToListAsync(ct);

        // 4. Construire + trier + limiter (logique pure, testée : RankMostWanted).
        var mostWanted = RankMostWanted(
            missingIssues.Select(i => (i, volumeById[i.VolumeId], missingCountByVolume[i.VolumeId])),
            MostWantedRowLimit);

        return new DashboardStats(
            librariesCount,
            volumesCount, volumesMonitored, volumesCompleted, volumesPaused,
            issuesCount, issuesDownloaded, issuesDownloading, issuesMissing,
            totalDownloadedBytes,
            libraryStats,
            recentVolumes,
            mostWanted);
    }

    // Statistiques d'UNE library pour l'encart de sa page — même logique de comptage que
    // GetDashboardStatsAsync (issues réelles toutes catégories, somme des tailles en long), scopée
    // à la library. VolumesBySource est indexé par SourceType normalisé en minuscules
    // ("comicvine", "bedetheque", "manual") — SourceType est une valeur libre en base.
    public record LibraryStats(
        int VolumesCount, int VolumesMonitored, int VolumesCompleted, int VolumesPaused,
        Dictionary<string, int> VolumesBySource,
        int IssuesCount, int IssuesDownloaded, int IssuesDownloading, int IssuesMissing,
        long TotalDownloadedBytes,
        DateTime? LastVolumeAddedAt, DateTime? LastRefreshedAt, DateTime? LastAutoSearchAt);

    public async Task<LibraryStats?> GetLibraryStatsAsync(Guid libraryId, CancellationToken ct = default)
    {
        var ctx = GetDb();
        if (!await ctx.Libraries.AnyAsync(l => l.Id == libraryId, ct)) return null;

        var volumes = ctx.Volumes.Where(v => v.LibraryId == libraryId);
        var issues  = from i in ctx.Issues
                      join v in ctx.Volumes on i.VolumeId equals v.Id
                      where v.LibraryId == libraryId
                      select i;

        var volumesCount     = await volumes.CountAsync(ct);
        var volumesMonitored = await volumes.CountAsync(v => v.Status == VolumeStatus.MONITORED, ct);
        var volumesCompleted = await volumes.CountAsync(v => v.Status == VolumeStatus.COMPLETED, ct);
        var volumesPaused    = await volumes.CountAsync(v => v.Status == VolumeStatus.PAUSED, ct);

        var volumesBySource = (await volumes
                .GroupBy(v => v.SourceType)
                .Select(g => new { SourceType = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .GroupBy(x => x.SourceType.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        var issuesDownloaded  = await issues.CountAsync(i => i.Status == IssueStatus.DOWNLOADED, ct);
        var issuesDownloading = await issues.CountAsync(i => i.Status == IssueStatus.DOWNLOADING, ct);
        var issuesMissing     = await issues.CountAsync(i => i.Status == IssueStatus.MISSING, ct);

        var totalDownloadedBytes = await issues
            .Where(i => i.Status == IssueStatus.DOWNLOADED)
            .SumAsync(i => (long)i.FileSizeBytes, ct);

        // MaxAsync sur un type nullable renvoie null quand la séquence est vide (pas d'exception).
        var lastVolumeAddedAt = await volumes.Select(v => (DateTime?)v.DateAdded).MaxAsync(ct);
        var lastRefreshedAt   = await volumes.Select(v => v.LastRefreshedAt).MaxAsync(ct);
        var lastAutoSearchAt  = await volumes.Select(v => v.LastAutoSearchAt).MaxAsync(ct);

        return new LibraryStats(
            volumesCount, volumesMonitored, volumesCompleted, volumesPaused,
            volumesBySource,
            issuesDownloaded + issuesDownloading + issuesMissing,
            issuesDownloaded, issuesDownloading, issuesMissing,
            totalDownloadedBytes,
            lastVolumeAddedAt, lastRefreshedAt, lastAutoSearchAt);
    }

    // Construit et ordonne les lignes « Most wanted » à partir des issues MISSING Standard
    // candidates et de leur volume parent. Tri : moins d'issues manquantes d'abord (une série
    // à qui il ne manque qu'un tome passe en tête), puis volume au plus grand nombre de tomes
    // (compléter une série de 20 tomes est plus gratifiant qu'une série de 2), puis complétude
    // actuelle la plus haute. Pur : aucune dépendance DB, testé par MostWantedRankerTests.
    internal static List<DashboardMostWantedIssue> RankMostWanted(
        IEnumerable<(Issue Issue, Volume Volume, int VolumeMissingCount)> candidates,
        int limit)
    {
        static int Pct(int n, int total) => total > 0 ? (int)Math.Round(n / (double)total * 100.0) : 0;

        static bool HasCover(VolumeImage? img) => img is not null && (img.SmallUrl is not null || img.ThumbUrl is not null);

        return candidates
            .Select(c =>
            {
                var (i, v, missing) = c;
                return new DashboardMostWantedIssue(
                    i.Id, v.Id, v.LibraryId, v.Title,
                    HasCover(i.Image) ? i.Image : v.Image,
                    i.IssueNumber, i.Title,
                    v.CountOfDownloadedIssues, v.CountOfIssues, missing,
                    Pct(v.CountOfDownloadedIssues, v.CountOfIssues),
                    Pct(v.CountOfDownloadedIssues + 1, v.CountOfIssues));
            })
            .OrderBy(r => r.MissingCount)
            .ThenByDescending(r => r.TotalCount)
            .ThenByDescending(r => r.CurrentCompletionPercent)
            .ThenBy(r => r.VolumeTitle)
            .ThenBy(r => r.IssueNumber)
            .Take(limit)
            .ToList();
    }

    public async Task<Library> CreateLibraryAsync(string name, string path, int kavitaLibraryId, string kavitaPath = "")
    {
        var ctx = GetDb();
        var library = new Library
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = path,
            KavitaLibraryId = kavitaLibraryId,
            KavitaPath = kavitaPath,
            CreatedAt = DateTime.UtcNow
        };
        ctx.Libraries.Add(library);
        await ctx.SaveChangesAsync();
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Library>(library.Id));
        return library;
    }

    public async Task<Library?> UpdateLibraryAsync(Guid id, string name, string path, int kavitaLibraryId, string kavitaPath = "")
    {
        var ctx = GetDb();
        var library = await ctx.Libraries.FindAsync(id);
        if (library is null) return null;

        library.Name = name;
        library.Path = path;
        library.KavitaLibraryId = kavitaLibraryId;
        library.KavitaPath = kavitaPath;
        await ctx.SaveChangesAsync();
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Library>(library.Id));
        return library;
    }

    /// <summary>
    /// Supprime une library et tout ce qui en dépend en base (volumes, issues, downloads, indexers
    /// sélectionnés). Avec <paramref name="deleteFiles"/>, le répertoire de chaque volume est supprimé
    /// via <see cref="DeleteVolumeDirectory"/> (garde-fou de confinement sous Library.Path — on ne
    /// supprime jamais Library.Path lui-même, qui peut contenir des dossiers étrangers au pipeline).
    /// Les échecs disque n'annulent pas la suppression en base : ils sont cumulés dans FileWarning.
    /// </summary>
    public async Task<(bool Found, string? FileWarning)> DeleteLibraryAsync(Guid id, bool deleteFiles = false)
    {
        var ctx = GetDb();
        var library = await ctx.Libraries.FindAsync(id);
        if (library is null) return (false, null);

        var volumes = await ctx.Volumes.Where(v => v.LibraryId == id).ToListAsync();

        List<string> warnings = [];
        if (deleteFiles)
        {
            foreach (var volume in volumes)
            {
                var warning = DeleteVolumeDirectory(volume, library);
                if (warning is not null) warnings.Add($"{volume.Title}: {warning}");
            }
        }

        var volumeIds = volumes.Select(v => v.Id).ToList();
        var issueIds  = await ctx.Issues.Where(i => volumeIds.Contains(i.VolumeId)).Select(i => i.Id).ToListAsync();
        ctx.IssueDownloads.RemoveRange(ctx.IssueDownloads.Where(d => issueIds.Contains(d.IssueId)));
        ctx.IssueTorrentBans.RemoveRange(ctx.IssueTorrentBans.Where(b => issueIds.Contains(b.IssueId)));
        ctx.Issues.RemoveRange(ctx.Issues.Where(i => volumeIds.Contains(i.VolumeId)));
        ctx.Volumes.RemoveRange(volumes);
        ctx.SelectedIndexers.RemoveRange(ctx.SelectedIndexers.Where(s => s.LibraryId == id));
        ctx.Libraries.Remove(library);
        await ctx.SaveChangesAsync();
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Library>(id));

        if (deleteFiles && library.KavitaLibraryId > 0)
        {
            await ScanKavitaLibraryAsync(library.KavitaLibraryId);
        }

        return (true, warnings.Count == 0 ? null : string.Join(" ", warnings));
    }

    /// <summary>
    /// Supprime un volume, ses issues et leurs lignes de download. Quand <paramref name="deleteFiles"/>
    /// vaut <c>true</c>, le répertoire du volume dans la librairie est supprimé récursivement (fichiers
    /// étrangers au pipeline compris) — l'échec de cette suppression disque n'annule pas la suppression
    /// en base, il est remonté via <c>FileWarning</c>.
    /// </summary>
    /// <returns><c>Found</c> à false si le volume n'existe pas ; <c>FileWarning</c> non null si le
    /// répertoire n'a pas pu être supprimé (ou a été refusé par le garde-fou de confinement).</returns>
    public async Task<(bool Found, string? FileWarning)> DeleteVolumeAsync(Guid id, bool deleteFiles = false)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync(id);
        if (volume is null) return (false, null);

        var library = await ctx.Libraries.FindAsync(volume.LibraryId);

        // Suppression disque AVANT la suppression en base : le chemin se calcule depuis le volume et la librairie.
        string? fileWarning = null;
        if (deleteFiles)
        {
            fileWarning = DeleteVolumeDirectory(volume, library);
        }

        var issueIds = await ctx.Issues.Where(i => i.VolumeId == id).Select(i => i.Id).ToListAsync();
        ctx.IssueDownloads.RemoveRange(ctx.IssueDownloads.Where(d => issueIds.Contains(d.IssueId)));
        ctx.IssueTorrentBans.RemoveRange(ctx.IssueTorrentBans.Where(b => issueIds.Contains(b.IssueId)));
        ctx.Issues.RemoveRange(ctx.Issues.Where(i => i.VolumeId == id));
        ctx.Volumes.Remove(volume);
        await ctx.SaveChangesAsync();
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(id));

        // Le dossier ayant disparu, un scan ciblé n'a plus de sens : on rafraîchit la librairie Kavita entière.
        if (deleteFiles && library is { KavitaLibraryId: > 0 })
        {
            await ScanKavitaLibraryAsync(library.KavitaLibraryId);
        }

        return (true, fileWarning);
    }

    /// <summary>
    /// Supprime récursivement le répertoire d'un volume. La cible est validée par
    /// <see cref="ArchiveService.TryGetVolumeDirectory"/>, qui refuse tout chemin ne désignant pas un
    /// sous-dossier strict de <see cref="Library.Path"/>.
    /// </summary>
    /// <returns>null si le répertoire a été supprimé ou n'existait pas, sinon le message d'avertissement.</returns>
    private string? DeleteVolumeDirectory(Volume volume, Library? library)
    {
        if (library is null)
        {
            JobSendTrace($"DeleteVolume: library {volume.LibraryId} not found — directory left untouched", ETraceLevel.WARNING);
            return "Library not found: the volume directory was left untouched.";
        }

        if (!ArchiveService.TryGetVolumeDirectory(volume, library, out var target, out var guardError))
        {
            JobSendTrace($"DeleteVolume: refusing to delete the directory of '{volume.Title}' — {guardError}", ETraceLevel.ERROR);
            return $"{guardError} No file was deleted.";
        }

        if (!Directory.Exists(target))
        {
            JobSendTrace($"DeleteVolume: directory '{target}' does not exist — nothing to delete");
            return null;
        }

        try
        {
            Directory.Delete(target, recursive: true);
            JobSendTrace($"DeleteVolume: directory '{target}' deleted");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            JobSendTrace($"DeleteVolume: could not delete directory '{target}' — {ex.Message}", ETraceLevel.ERROR);
            return $"The volume was deleted but its directory could not be removed: {ex.Message}";
        }
    }
    #endregion

    #region Volume CRUD
    public async Task<Volume?> GetVolumeAsync(Guid id)
    {
        return await GetDb().Volumes.FindAsync(id);
    }

    // Recalcule CountOfIssues / CountOfDownloadedIssues (et le Status COMPLETED/MONITORED) d'un
    // volume à partir de l'état réel des issues en base. Restreint aux issues Category == Standard
    // : la complétude d'un volume ne dépend que de ses tomes classiques, un Omnibus/Hors-série/...
    // manquant ne doit ni bloquer le passage à COMPLETED ni fausser la barre de progression.
    // Surcharge publique utilisable directement par le controller (ouvre son propre contexte EF Core).
    public async Task<Volume?> RecalculateVolumeStatisticsAsync(Guid volumeId, CancellationToken ct = default)
    {
        var ctx = GetDb();
        if (!await ctx.Volumes.AnyAsync(v => v.Id == volumeId, ct)) return null;
        await RecalculateVolumeStatisticsAsync(ctx, volumeId, ct);
        return await ctx.Volumes.FindAsync([volumeId], ct);
    }

    // Surcharge interne réutilisant le ctx de l'appelant — à utiliser à l'intérieur d'un job, après
    // que les changements sur les Issues concernées ont été persistés via SaveChangesAsync (sans quoi
    // le ChangeTracker EF Core ne voit pas encore les modifications en attente et le compteur reste
    // en retard d'une issue).
    private async Task RecalculateVolumeStatisticsAsync(DbStorageContext ctx, Guid volumeId, CancellationToken ct = default)
    {
        var volume = await ctx.Volumes.FindAsync([volumeId], ct);
        if (volume is null) return;

        volume.CountOfIssues           = await ctx.Issues.CountAsync(i => i.VolumeId == volumeId && i.Category == IssueCategory.Standard, ct);
        volume.CountOfDownloadedIssues = await ctx.Issues.CountAsync(i => i.VolumeId == volumeId && i.Category == IssueCategory.Standard && i.Status == IssueStatus.DOWNLOADED, ct);
        if (volume.Status != VolumeStatus.PAUSED)
        {
            volume.Status = volume.CountOfIssues > 0 && volume.CountOfIssues == volume.CountOfDownloadedIssues
                ? VolumeStatus.COMPLETED
                : VolumeStatus.MONITORED;
        }
        volume.UpdatedAt = DateTime.UtcNow;

        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volumeId));
    }

    public async Task<List<Volume>> GetVolumesByLibraryAsync(Guid libraryId)
    {
        return await GetDb().Volumes
            .Where(v => v.LibraryId == libraryId)
            .OrderBy(v => v.Title)
            .ToListAsync();
    }

    public async Task<List<Issue>> GetIssuesByVolumeAsync(Guid volumeId)
    {
        return await GetDb().Issues
            .Where(i => i.VolumeId == volumeId)
            .OrderBy(i => i.IssueNumber)
            .ToListAsync();
    }

    public async Task<Issue?> GetIssueAsync(Guid id, CancellationToken ct = default)
        => await GetDb().Issues.FindAsync([id], ct);

    #endregion



    #region ComicVine Search and Import

    // Résultats de recherche multi-source en attente de récupération par le contrôleur, indexés
    // par JobId — un Job ne peut pas porter de valeur de retour vers l'appelant HTTP d'origine
    // (fire-and-forget), donc on garde le résultat final en mémoire le temps que le frontend
    // vienne le chercher une fois le job terminé (cf. GetSearchJobResult).
    // Borné en durée ET en nombre d'entrées (ExpiringCache) : ces résultats sont lus une seule
    // fois, par jobId, juste après la fin du job. Un dictionnaire nu les gardait pour la durée de
    // vie du process — un jeu complet de résultats par recherche, y compris celles du scheduler.
    private readonly ExpiringCache<Guid, SearchVolumesJobResult> _searchResults =
        new("SearchVolumes results", JobResultRetention, JobResultMaxEntries);

    public SearchVolumesJobResult? GetSearchJobResult(Guid jobId)
        => _searchResults.TryGet(jobId, out var result) ? result : null;

    // Lance la recherche multi-source en tâche de fond et retourne immédiatement le JobContext
    // (donc son JobId) pour que le frontend puisse s'abonner à sa progression/ses traces via
    // SignalR sans attendre la fin de la recherche.
    public JobContext LaunchJobSearchVolumes(SearchVolumesJobParameters parameters)
    {
        var job = StartJob($"Search — {parameters.Name}", parameters);
        if (job.State != JobState.ERROR)
        {
            job.SetState(JobState.RUNNING);
            _ = RunSearchVolumesJobAsync(job, parameters);
        }
        return job;
    }

    private async Task RunSearchVolumesJobAsync(JobContext job, SearchVolumesJobParameters parameters)
    {
        try
        {
            var result = await SearchVolumesAsync(parameters.Name, parameters.Page, parameters.PageSize, job);
            _searchResults.Set(job.JobId, result);
            EndJob(true);
        }
        catch (Exception ex)
        {
            JobSendTrace($"Unexpected error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
    }

    // Interroge en parallèle toutes les sources de métadonnées enregistrées (ComicVine,
    // Bedetheque, ...) et fusionne leurs résultats en une seule liste, chaque entrée indiquant
    // sa source d'origine (SourceVolume.Source). Une source indisponible (état != OK) ou en
    // erreur est simplement ignorée plutôt que de faire échouer la recherche entière.
    // Le paramètre optionnel `job` (fourni par LaunchJobSearchVolumes) alimente la progression
    // (un pas par source) et les traces au fil de l'eau ; sans lui, la méthode fonctionne comme
    // un simple agrégateur silencieux.
    public async Task<SearchVolumesJobResult> SearchVolumesAsync(
        string name, int page = 1, int? pageSize = null, JobContext? job = null, CancellationToken ct = default)
    {
        var sources = Services.Values.OfType<ISourceService>().ToList();
        job?.CallbackHandler.UpdateTotal(sources.Count);

        var stats = new ConcurrentBag<SourceSearchStats>();
        var tasks = sources.Select(async src =>
        {
            var sw = Stopwatch.StartNew();
            var state = await ((IService)src).GetState();
            if (state.State != EState.OK)
            {
                JobSendTrace($"[{src.SourceKey}] Source unavailable, skipped.", ETraceLevel.WARNING);
                stats.Add(new SourceSearchStats(src.SourceKey, 0, 0, false, "Service unavailable"));
                job?.Progress.Increment(false);
                job?.CallbackHandler.Callback(job.Progress);
                return null;
            }
            try
            {
                JobSendTrace($"[{src.SourceKey}] Searching \"{name}\"…");
                var result = await src.SearchVolumesByNameAsync(name, page, pageSize, ct);
                sw.Stop();
                JobSendTrace($"[{src.SourceKey}] {result.Items.Count} result(s) in {sw.ElapsedMilliseconds} ms");
                stats.Add(new SourceSearchStats(src.SourceKey, result.Items.Count, sw.ElapsedMilliseconds, true, null));
                job?.Progress.Increment(true);
                job?.CallbackHandler.Callback(job.Progress);
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                JobSendTrace($"[{src.SourceKey}] Failed: {ex.Message}", ETraceLevel.WARNING);
                // Catalogue local vide : code dédié pour que l'UI propose le lien vers la page de
                // chargement au lieu d'un simple "failed".
                var errorCode = ex is BedethequeCatalogNotLoadedException ? SourceSearchStats.CatalogNotLoaded : null;
                stats.Add(new SourceSearchStats(src.SourceKey, 0, sw.ElapsedMilliseconds, false, ex.Message, errorCode));
                job?.Progress.Increment(false);
                job?.CallbackHandler.Callback(job.Progress);
                return null;
            }
        });

        var results = (await Task.WhenAll(tasks))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        // Score chaque résultat par pertinence par rapport à la requête (indépendamment de sa
        // source), puis trie l'ensemble fusionné dessus — sinon les résultats apparaissent
        // groupés par source (tous les ComicVine, puis tous les Bedetheque) plutôt que par
        // pertinence réelle. La langue favorisée vient de la première source qui en déclare une
        // (aujourd'hui : SearchLanguageFilter de Bedetheque, null en "All").
        var preferredLanguage = sources.Select(s => s.PreferredLanguage).FirstOrDefault(l => l is not null);
        var scoredItems = results
            .SelectMany(p => p.Items)
            .Select(v => v with { Score = SearchScoring.ScoreTitleMatch(name, v.Name, v.CountOfIssues, v.Language, preferredLanguage) })
            .OrderByDescending(v => v.Score)
            .ToList();

        var page2 = new Page<SourceVolume>
        {
            Items = scoredItems,
            PageNumber = page,
            PageSize = pageSize ?? results.Select(p => p.PageSize).DefaultIfEmpty(20).Max(),
            TotalItems = results.Sum(p => p.TotalItems),
        };

        return new SearchVolumesJobResult { Page = page2, Stats = stats.OrderBy(s => s.Source).ToList() };
    }

    public async Task<Page<SourceIssue>> GetIssuesBySourceAsync(
        string source, string sourceVolumeId, int page = 1, int? pageSize = null, CancellationToken ct = default)
    {
        var svc = Services.Values.OfType<ISourceService>().FirstOrDefault(s => s.SourceKey == source)
            ?? throw new InvalidOperationException($"Unknown source '{source}'");
        return await svc.GetIssuesPageAsync(sourceVolumeId, page, pageSize, ct);
    }

    private const int ComicVineMaxPageSize = 100;

    private record CvLinkCandidateVolume(CvVolume volume, List<ParsedVolumeName> candidates);

    // Recherche automatique du volume ComicVine correspondant le mieux à un nom de dossier/fichier :
    // extrait des candidats (SourceAnalyzer), interroge l'API pour chacun, puis note chaque paire
    // volume/candidat (ScoringSource) pour ne garder que le meilleur score.
    public async Task<CvVolume?> AutomaticSearchVolume(string volumeName, string favoriteCountryCode,
        ProgressionCallback? progression = null, CancellationToken ct = default)
    {
        var comicVine = GetService<ComicVineSourceService, ComicVineOptions>();

        // Calculate all candidates
        var candidates = SourceAnalyzer.ExtractVolumeNameCandidates(volumeName);
        JobSendTrace($"Volume name canditates {candidates.Count}", ETraceLevel.DEBUG);
        if (candidates.Count == 0)
            return null;

        // 2. Search and get Volume details
        var result = new Dictionary<int, CvLinkCandidateVolume>();
        foreach (var candidate in candidates)
        {
            var resultpage = await comicVine.SearchVolumesByNameAsync(candidate.Title, limit: 20, ct: ct); // Take only first page

            JobSendTrace($"Search for {candidate.Title}  and found {resultpage.Results.Count} results", ETraceLevel.DEBUG);
            foreach (var item in resultpage.Results)
            {
                if (result.ContainsKey(item.Id))
                {
                    result[item.Id].candidates.Add(candidate);
                }
                else
                {
                    var v = await comicVine.GetVolumeAsync(item.Id, ct);
                    if (v != null)
                    {
                        result.Add(item.Id, new CvLinkCandidateVolume(v, [candidate]));
                    }
                }
            }
        }

        JobSendTrace($"{result.Count} volumes could be candidate", ETraceLevel.DEBUG);

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
                var score = ScoringSource.ScoreVolume(item.Value.volume, candidate, favoriteCountryCode);
                if (score > bestScore)
                {
                    bestScore = score;
                    BestVolume = item.Value.volume;
                    JobSendTrace($"Update best score {bestScore} for volume {item.Value.volume.Name}", ETraceLevel.DEBUG);
                }
            }
            if (bestScore > maxScore)
            {
                return BestVolume;
            }
        }
        return BestVolume;
    }

    // Résout un volume ComicVine (si non fourni) puis retrouve l'issue correspondant au numéro
    // extrait du nom de fichier.
    public async Task<CvFindResult> FindVolume(string issueFilename, string favoriteCountryCode, CvVolume? cvVolume = null,
        CancellationToken ct = default)
    {
        var comicVine = GetService<ComicVineSourceService, ComicVineOptions>();

        var parts = issueFilename.Replace('\\', '/').Split('/', 2);
        var folderName = parts.Length == 2 ? parts[0] : Path.GetFileNameWithoutExtension(parts[0]);
        var fileName = parts.Length == 2 ? Path.GetFileNameWithoutExtension(parts[1]) : folderName;

        var issueNum = SourceAnalyzer.ParseIssueNumber(fileName);

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
            var issuePage = await comicVine.GetIssuesPageAsync(cvVolume.Id, page, ComicVineMaxPageSize, ELevelDetail.SUMMARY, ct);
            var match = issuePage.Results.FirstOrDefault(
                i => int.TryParse(i.IssueNumber, out var n) && n == issueNum);

            if (match is not null)
            {
                var issue = await comicVine.GetIssueAsync(match.Id, ct);
                return new CvFindResult(cvVolume, issue);
            }

            if (issuePage.Results.Count + issuePage.Offset >= issuePage.NumberOfTotalResults)
                break;

            page++;
        }

        return new CvFindResult(cvVolume, null);
    }


    #endregion


    #region Archive

    public async Task<List<DirectoryInfo>> GetDirectoriesAsync(string path)
    {
        return await ArchiveService.GetDirectoriesAsync(path);
    }

    public async Task<List<FileInfo>> GetFilesAsync(string path, string filter = "*")
    {
        return await ArchiveService.GetFilesAsync(path, filter);
    }

    #endregion


    #region Kavita

    private List<KavitaLibrary>? _kavitaLibrariesCache;

    public async Task<List<KavitaLibrary>> GetKavitaLibrariesAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _kavitaLibrariesCache is not null && _kavitaLibrariesCache.Count > 0)
            return _kavitaLibrariesCache;

        var kavita = GetService<KavitaService, KavitaOptions>();
        _kavitaLibrariesCache = await kavita.GetLibrariesAsync();
        return _kavitaLibrariesCache;
    }

    public async Task<bool> ScanKavitaLibraryAsync(int libraryId, bool force = false)
    {
        var kavita = GetService<KavitaService, KavitaOptions>();
        return await kavita.ScanLibraryAsync(libraryId, force);
    }

    #endregion

    #region WebshareProxy

    public IReadOnlyList<ProxyInfo> GetWebshareProxies()
        => GetService<WebshareProxyService, WebshareProxyOptions>().Proxies;

    public ProxyInfo? GetCurrentWebshareProxy()
        => GetService<WebshareProxyService, WebshareProxyOptions>().CurrentProxy;

    public ProxyInfo? RotateWebshareProxy()
        => GetService<WebshareProxyService, WebshareProxyOptions>().NextProxy();

    public Task<WebshareStatistics> GetWebshareProxyStatisticsAsync(CancellationToken ct = default)
        => GetService<WebshareProxyService, WebshareProxyOptions>().GetStatisticsAsync(ct);

    // Inspecte les options déjà chargées de chaque service enregistré et retourne ceux dont le
    // booléen UseProxy est actif. WebshareProxyService lui-même n'a pas cette option, il est donc
    // naturellement exclu du résultat.
    public List<string> GetServicesUsingProxy()
        => Services.Values
            .Where(s => s.GetOptions().Any(o => o.Name == "UseProxy" && o.GetBool()))
            .Select(s => s.GetServiceName())
            .ToList();

    #endregion

    #region ApiTokens

    public Task<List<ApiToken>> GetApiTokensAsync()
        => GetDb().ApiTokens.OrderByDescending(t => t.CreatedAt).ToListAsync();

    public async Task<(ApiToken Token, string RawValue)> CreateApiTokenAsync(string name, int? expiresInDays)
    {
        var (raw, prefix, hash) = ApiTokenGenerator.Generate();
        var token = new ApiToken
        {
            Id = Guid.NewGuid(),
            Name = name,
            Prefix = prefix,
            TokenHash = hash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresInDays is null ? null : DateTime.UtcNow.AddDays(expiresInDays.Value)
        };

        var db = GetDb();
        db.ApiTokens.Add(token);
        await db.SaveChangesAsync();
        return (token, raw);
    }

    public async Task DeleteApiTokenAsync(Guid id)
    {
        var db = GetDb();
        var token = await db.ApiTokens.FindAsync(id) ?? throw new KeyNotFoundException($"ApiToken '{id}' not found.");
        db.ApiTokens.Remove(token);
        await db.SaveChangesAsync();
    }

    // Appelée par ApiKeyAuthenticationHandler à chaque requête portant un header X-Api-Key.
    public async Task<ApiToken?> ValidateApiTokenAsync(string rawToken)
    {
        if (!GetService<ApiTokenService, ApiTokenOptions>().Enabled) return null;
        if (string.IsNullOrWhiteSpace(rawToken) || !ApiTokenGenerator.HasPrefix(rawToken)) return null;

        var hash = ApiTokenGenerator.Hash(rawToken);
        var db = GetDb();
        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (token is null) return null;
        if (token.ExpiresAt is not null && token.ExpiresAt <= DateTime.UtcNow) return null;

        token.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return token;
    }

    #endregion

    #region Users

    // Cache synchrone lu par le sélecteur du scheme "Smart" (Inkhound.Web/Program.cs), qui ne peut pas
    // être async. Initialisé dans AutomaticLoadServices() une fois la base garantie prête, puis tenu à
    // jour à chaque création/suppression d'utilisateur.
    private volatile bool _hasUsers;
    public bool HasUsers => _hasUsers;

    public Task<List<User>> GetUsersAsync()
        => GetDb().Users.OrderBy(u => u.Login).ToListAsync();

    public Task<User?> GetUserByIdAsync(Guid id)
        => GetDb().Users.FirstOrDefaultAsync(u => u.Id == id);

    public async Task<User> CreateUserAsync(string login, string password)
    {
        if (string.IsNullOrWhiteSpace(login))    throw new ArgumentException("Login is required.");
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password is required.");

        var db = GetDb();
        if (await db.Users.AnyAsync(u => u.Login == login))
            throw new InvalidOperationException($"Login '{login}' is already taken.");

        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            PasswordHash = PasswordHasher.Hash(password),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        _hasUsers = true;
        return user;
    }

    public async Task<User> UpdateUserAsync(Guid id, string? login, string? password)
    {
        var db = GetDb();
        var user = await db.Users.FindAsync(id) ?? throw new KeyNotFoundException($"User '{id}' not found.");

        if (login is not null && login != user.Login && await db.Users.AnyAsync(u => u.Login == login))
            throw new InvalidOperationException($"Login '{login}' is already taken.");

        if (login is not null)    user.Login = login;
        if (password is not null) user.PasswordHash = PasswordHasher.Hash(password);
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return user;
    }

    public async Task DeleteUserAsync(Guid id)
    {
        var db = GetDb();
        var user = await db.Users.FindAsync(id) ?? throw new KeyNotFoundException($"User '{id}' not found.");
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        _hasUsers = await db.Users.AnyAsync();
    }

    // Appelée par AuthController.Login.
    public async Task<User?> ValidateUserAsync(string login, string password)
    {
        var user = await GetDb().Users.FirstOrDefaultAsync(u => u.Login == login);
        return user is not null && PasswordHasher.Verify(password, user.PasswordHash) ? user : null;
    }

    #endregion

    #region Library Actions

    public async Task<Library?> LaunchJobSynchronizeLibrary(SynchronizeLibraryJobParameters parameters)
    {
        var job = StartJob($"Synchronize library {parameters.LibraryId}", parameters);
        job.SetState(JobState.RUNNING);

        var ctx = GetDb();
        var library = await ctx.Libraries.FindAsync(parameters.LibraryId);
        if (library is null)
        {
            EndJob(false);
            return null;
        }

        var comicVine = GetService<ComicVineSourceService, ComicVineOptions>();
        if (comicVine.CurrentState.State != EState.OK)
        {
            EndJob(false);
            return null;
        }

        try
        {

        // STEP 1. Scan directories in library path and match with ComicVine volumes
        var libraryDir = new DirectoryInfo(library.Path);
        var directories = await ArchiveService.GetDirectoriesAsync(library.Path);

        JobSendTrace("[Sync] Found " + directories.Count + " directories in library path");

        job.CallbackHandler.UpdateTotal(directories.Count);

        var existingVolumes = await ctx.Volumes
            .Where(v => v.LibraryId == parameters.LibraryId)
            .ToDictionaryAsync(v => v.SourceId, v => v);

        JobSendTrace($"[Sync] Found {existingVolumes.Count} existing volumes in database for this library");

        foreach (var dir in directories)
        {

            Volume volume;
            if (existingVolumes.Values.Any(v => ArchiveService.GetPath(v, library) == dir.FullName))
            {
                JobSendTrace($"[Sync] Directory {dir.FullName} is already matched to an existing volume, skipping");
                // This directory is already matched to an existing volume, skip it
                volume = existingVolumes.Values.First(v => ArchiveService.GetPath(v, library) == dir.FullName);
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);

            }
            else
            {
                // Try to find a ComicVine volume match for this directory name
                JobSendTrace($"[Sync] Searching for ComicVine volume for directory: {dir.Name}");
                var cvVolume = await AutomaticSearchVolume(dir.Name, parameters.CountryCode, job.CallbackHandler);
                if (cvVolume is null)
                {
                    JobSendTrace($"[Sync] No ComicVine match for directory: {dir.Name}", ETraceLevel.DEBUG);
                    job.Progress.Increment(false);
                    job.CallbackHandler.Callback(job.Progress);
                    continue;
                }


                var sourceId = cvVolume.Id.ToString();
                JobSendTrace($"[Sync] Found ComicVine match for directory {dir.Name} : {cvVolume.Name} (sourceId={sourceId})");
                if (existingVolumes.TryGetValue(sourceId, out var existingVolume))
                {
                    volume = existingVolume;
                    // This ComicVine volume is already in the database, but with the wrong path
                    dir.MoveTo(ArchiveService.GetPath(volume, library));
                    JobSendTrace($"[Sync] Updated path for existing volume {volume.Title} to {dir.FullName}");
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);

                }
                else
                {

                    var now = DateTime.UtcNow;
                    volume = Mapper.Map(cvVolume);
                    volume.Id = Guid.NewGuid();
                    volume.LibraryId = parameters.LibraryId;
                    volume.Status = VolumeStatus.PAUSED;
                    volume.CreatedAt = now;
                    volume.UpdatedAt = now;
                    volume.DateAdded = now;
                    ctx.Volumes.Add(volume);
                    JobSendTrace($"[Sync] Added new volume {volume.Title} to database with path {dir.FullName}");

                    // ADD issue in MISSING status for all issues in this volume, will be updated to DOWNLOADED if a matching CBZ file is found in step 2
                    JobSendTrace($"[Sync] Adding issues for volume {volume.Title}");
                    var cvIssuesFull = await comicVine.GetAllIssuesForVolumeAsync(cvVolume.Id, ELevelDetail.FULL);
                    foreach (var cvIssue in cvIssuesFull)
                    {
                        var issue = Mapper.Map(cvIssue);
                        issue.Id = Guid.NewGuid();
                        issue.VolumeId = volume.Id;
                        issue.Status = IssueStatus.MISSING;
                        ctx.Issues.Add(issue);
                    }

                    JobSendTrace($"[Sync] Added {cvIssuesFull.Count} issues for volume {volume.Title} to database");
                    await ctx.SaveChangesAsync(); // Save here to get the Volume ID for issue linking
                    OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));

                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                }
            }



            // Finish matching this volume to the directory (in case it was just created or had wrong path)


            // STEP 2. Find CBZ files in volume directory and match to ComicVine issues
            var cbzFiles = await ArchiveService.GetFilesAsync(dir.FullName, "*.cbz");
            JobSendTrace($"[Sync] Found {cbzFiles.Count} CBZ files in directory {dir.FullName} for volume {volume.Title}");
            job.CallbackHandler.UpdateTotal(cbzFiles.Count);
            int.TryParse(volume.SourceId, out var sourceVolumeId);

            //var cvIssues = await comicVine.GetAllIssuesForVolumeAsync(sourceVolumeId, ELevelDetail.SUMMARY);

            var localIssues = await ctx.Issues
                .Where(i => i.VolumeId == volume.Id).ToListAsync();
            JobSendTrace($"[Sync] Found {localIssues.Count} issues in database for volume {volume.Title}");

            foreach (var cbzFile in cbzFiles)
            {
                if (localIssues.Any(v => v.CbzFilename == cbzFile.Name))
                {
                    JobSendTrace($"[Sync] CBZ file {cbzFile.Name} is already matched to an existing issue, skipping");
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    continue; // This CBZ file is already matched to an existing issue, skip it
                }



                var issueNum = SourceAnalyzer.ParseIssueNumber(cbzFile.Name);
                if (issueNum is null)
                {
                    JobSendTrace($"[Sync] Unable to parse issue number from CBZ file {cbzFile.Name}", ETraceLevel.WARNING);
                    job.Progress.Increment(false);
                    job.CallbackHandler.Callback(job.Progress);
                    continue;
                }
                if (!localIssues.Any(v => v.IssueNumber == issueNum))
                {
                    JobSendTrace($"[Sync] No issue with number {issueNum} found in database for volume {volume.Title}, skipping CBZ file {cbzFile.Name}", ETraceLevel.WARNING);
                    job.Progress.Increment(false);
                    job.CallbackHandler.Callback(job.Progress);
                    continue; // No issue with this number in the database, skip it
                }

                var existingIssue = localIssues.First(v => v.IssueNumber == issueNum);
                var issuenumberfilename = ArchiveService.GetPath(existingIssue, volume);
                if (existingIssue.Status != IssueStatus.DOWNLOADED)
                { // This issue was previously matched without a file, so just update the filename and path
                    JobSendTrace($"[Sync] Matching CBZ file {cbzFile.Name} to issue {existingIssue.Title} (issue number {existingIssue.IssueNumber})");
                    ArchiveService.AttachFileToIssue(cbzFile, existingIssue, volume, library);
                    await ctx.SaveChangesAsync();
                    OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(existingIssue.Id));
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    continue;
                }
                var issueExistFile = new FileInfo(ArchiveService.GetPath(existingIssue, volume, library));
                if (issueExistFile.Exists)
                {
                    JobSendTrace($"[Sync] Existing file found for issue {existingIssue.Title} (issue number {existingIssue.IssueNumber})");
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    if (issueExistFile.Name != issuenumberfilename || issueExistFile.Length < cbzFile.Length)
                    {
                        issueExistFile.Delete();
                        ArchiveService.AttachFileToIssue(cbzFile, existingIssue, volume, library);
                        await ctx.SaveChangesAsync();
                        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(existingIssue.Id));
                        job.Progress.Increment(true);
                        job.CallbackHandler.Callback(job.Progress);

                        continue;
                    }
                    // The existing file is correct, just update the path if needed and delete the new file
                    if (cbzFile.Name != issuenumberfilename)
                    {
                        JobSendTrace($"[Sync] Updating path for existing issue {existingIssue.Title} to match CBZ file {cbzFile.Name}");
                        cbzFile.Delete();
                    }

                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    continue;

                }

                JobSendTrace($"[Sync] Attaching CBZ file {cbzFile.Name} to issue {existingIssue.Title} (issue number {existingIssue.IssueNumber})");
                ArchiveService.AttachFileToIssue(cbzFile, existingIssue, volume, library);
                await ctx.SaveChangesAsync();
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(existingIssue.Id));
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);



            }

            // STEP 3. Update volume status and counts
            var volumeIssues = await ctx.Issues
                .Where(i => i.VolumeId == volume.Id).ToListAsync();

            List<VolumeAuthor> allIssueAuthors = [];
            foreach (var cvIssue in volumeIssues)
            {
                allIssueAuthors.AddRange(cvIssue.Authors);
            }

            var roleByName = allIssueAuthors
                .GroupBy(a => a.Name)
                .ToDictionary(g => g.Key, g => g.First().Role);

            volume.Authors = volume.Authors
                .Select(a => string.IsNullOrEmpty(a.Role) && roleByName.TryGetValue(a.Name, out var role)
                    ? new VolumeAuthor(a.Name, role ?? string.Empty)
                    : a)
                .ToList();
            await ctx.SaveChangesAsync();
            await RecalculateVolumeStatisticsAsync(ctx, volume.Id);
        }

        library.UpdatedAt = DateTime.UtcNow;
        JobSendTrace($"[Sync] Synchronization complete for library {library.Name}");
        ctx.SaveChanges();
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Library>(library.Id));

        EndJob(job.Progress.Error < directories.Count);
        return library;
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Sync] Unhandled error during synchronization: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
            return library;
        }
    }

    // Recalcule CountOfIssues / CountOfDownloadedIssues (+ Status) de tous les volumes d'une
    // library, un par un, via la méthode partagée RecalculateVolumeStatisticsAsync. Chaque volume
    // recalculé émet son propre OnDataUpdated<Volume> — le frontend (LibraryComponent) rafraîchit
    // déjà sa liste de volumes sur cet événement, aucune plomberie supplémentaire n'est nécessaire.
    public async Task LaunchJobRecalculateLibraryStatistics(RecalculateLibraryStatisticsJobParameters parameters)
    {
        var ctx = GetDb();
        var library = await ctx.Libraries.FindAsync(parameters.LibraryId);
        var jobTitle = library is not null
            ? $"Recalculate statistics — {library.Name}"
            : $"Recalculate statistics — {parameters.LibraryId}";

        var job = StartJob(jobTitle, parameters);
        job.SetState(JobState.RUNNING);
        try
        {
            if (library is null) { EndJob(false); return; }

            var volumeIds = await ctx.Volumes
                .Where(v => v.LibraryId == parameters.LibraryId)
                .Select(v => v.Id)
                .ToListAsync();

            JobSendTrace($"[Stats] {volumeIds.Count} volumes to recalculate in library {library.Name}");
            job.CallbackHandler.UpdateTotal(volumeIds.Count);

            foreach (var volumeId in volumeIds)
            {
                await RecalculateVolumeStatisticsAsync(ctx, volumeId);
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);
            }

            JobSendTrace($"[Stats] Statistics recalculation complete for library {library.Name}");
            EndJob(true);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Stats] Unhandled error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
    }

    #endregion

    #region Volume Actions
    // Résultat renvoyé immédiatement par AddVolumeFrom*Async : le Volume existe déjà en base
    // (insert rapide), et JobId permet au frontend de suivre en tâche de fond le peuplement des
    // issues (potentiellement plusieurs appels HTTP externes) sans attendre sa fin.
    public record AddVolumeResult(Volume Volume, Guid JobId);

    public async Task<AddVolumeResult> AddVolumeFromComicVineAsync(
        Guid libraryId, int comicVineVolumeId, CancellationToken ct = default)
    {
        var ctx = GetDb();

        _ = await ctx.Libraries.FindAsync([libraryId], ct)
            ?? throw new KeyNotFoundException($"Library {libraryId} not found.");

        var duplicate = await ctx.Volumes.FirstOrDefaultAsync(
            v => v.LibraryId == libraryId && v.SourceType == "ComicVine" && v.SourceId == comicVineVolumeId.ToString(), ct);
        if (duplicate is not null)
            throw new InvalidOperationException($"Volume {comicVineVolumeId} already exists in this library.");

        var comicVine = GetService<ComicVineSourceService, ComicVineOptions>();
        if (comicVine.CurrentState.State != EState.OK)
            throw new InvalidOperationException("ComicVine service is not available");

        var cvVolume = await comicVine.GetVolumeAsync(comicVineVolumeId, ct)
            ?? throw new KeyNotFoundException($"ComicVine volume {comicVineVolumeId} not found.");

        var now = DateTime.UtcNow;
        var volume = Mapper.Map(cvVolume);
        volume.Id = Guid.NewGuid();
        volume.LibraryId = libraryId;
        volume.Status = VolumeStatus.MONITORED;
        volume.CreatedAt = now;
        volume.UpdatedAt = now;
        volume.DateAdded = now;
        volume.CountOfIssues = 0;
        ctx.Volumes.Add(volume);
        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));

        // Le peuplement des issues continue en tâche de fond (Job) — le Volume existe déjà et a
        // été notifié, le frontend n'a pas besoin d'attendre pour naviguer/afficher la suite.
        var job = StartJob($"Add volume — {volume.Title}");
        if (job.State != JobState.ERROR)
        {
            job.SetState(JobState.RUNNING);
            _ = RunAddComicVineIssuesJobAsync(job, volume.Id, comicVineVolumeId);
        }
        return new AddVolumeResult(volume, job.JobId);
    }

    private async Task RunAddComicVineIssuesJobAsync(JobContext job, Guid volumeId, int comicVineVolumeId)
    {
        var ctx = GetDb();
        try
        {
            var volume = await ctx.Volumes.FindAsync(volumeId);
            if (volume is null) { EndJob(false); return; }

            var comicVine = GetService<ComicVineSourceService, ComicVineOptions>();
            var cvIssues = await comicVine.GetAllIssuesForVolumeAsync(
                comicVineVolumeId, ELevelDetail.FULL, progression: job.CallbackHandler);
            // Repart de zéro pour la phase de création des issues — le fetch ci-dessus a déjà fait
            // progresser job.Progress jusqu'à 100% en interne, sans Reset() cette phase hériterait
            // d'un Completed déjà au max et resterait figée à ~100% au lieu d'un 0→100% propre.
            job.Progress.Reset();
            job.CallbackHandler.UpdateTotal(cvIssues.Count);

            List<VolumeAuthor> allIssueAuthors = [];

            foreach (var cvIssue in cvIssues)
            {
                var issue = Mapper.Map(cvIssue);
                issue.Id = Guid.NewGuid();
                issue.Status = IssueStatus.MISSING;
                issue.VolumeId = volume.Id;
                allIssueAuthors.AddRange(issue.Authors);
                ctx.Issues.Add(issue);
                JobSendTrace($"[Add] {volume.Title} — issue #{issue.IssueNumber} ({issue.Category})");
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);
            }
            if (cvIssues.Count > 0)
            {
                var roleByName = allIssueAuthors
                            .GroupBy(a => a.Name)
                            .ToDictionary(g => g.Key, g => g.First().Role);

                volume.Authors = volume.Authors
                    .Select(a => string.IsNullOrEmpty(a.Role) && roleByName.TryGetValue(a.Name, out var role)
                        ? new VolumeAuthor(a.Name, role ?? string.Empty)
                        : a)
                    .ToList();

                volume.CountOfIssues = cvIssues.Count;
                volume.UpdatedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));
            }
            EndJob(true);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Add] Unexpected error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
    }

    // Résumé retourné par un rematch — diffusé via JobSendTrace par l'appelant (RunRematchVolumeJobAsync),
    // pas exposé en HTTP synchrone (le rematch s'exécute désormais en Job, cf. LaunchJobRematchVolume).
    public record RematchResult(int IssuesAdded, int IssuesUpdated, int IssuesRemoved);

    public async Task<RematchResult?> RematchVolumeFromComicVineAsync(
        Guid volumeId, int comicVineVolumeId, CancellationToken ct = default, ProgressionCallback? progression = null,
        bool newIssuesOnly = false)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync([volumeId], ct);
        if (volume is null) return null;

        var comicVine = GetService<ComicVineSourceService, ComicVineOptions>();
        if (comicVine.CurrentState.State != EState.OK)
            throw new InvalidOperationException("ComicVine service is not available");

        var cvVolume = await comicVine.GetVolumeAsync(comicVineVolumeId, ct)
            ?? throw new KeyNotFoundException($"ComicVine volume {comicVineVolumeId} not found.");

        var mapped = Mapper.Map(cvVolume);
        volume.SourceId     = mapped.SourceId;
        volume.SourceType   = mapped.SourceType;
        volume.Title        = mapped.Title;
        volume.Year         = mapped.Year;
        volume.Description  = mapped.Description;
        volume.Image        = mapped.Image;
        volume.Publisher    = mapped.Publisher;
        volume.Authors      = mapped.Authors;
        volume.Genres       = mapped.Genres;
        volume.UpdatedAt    = DateTime.UtcNow;
        JobSendTrace($"[Rematch] Volume metadata: title='{volume.Title}', year={volume.Year}, publisher='{volume.Publisher}'");

        // Mode "NEW issues only" : metadata Volume déjà recopiée ci-dessus, on ne récupère que les
        // issues source encore absentes en base et on laisse les connues intactes.
        if (newIssuesOnly)
            return await SyncNewComicVineIssuesAsync(ctx, volume, cvVolume, comicVine, ct, progression);

        var cvIssues = await comicVine.GetAllIssuesForVolumeAsync(comicVineVolumeId, ELevelDetail.FULL, ct, progression);
        var existingIssues = await ctx.Issues.Where(i => i.VolumeId == volumeId).ToListAsync(ct);

        var matchedExistingIds = new HashSet<Guid>();
        int issuesAdded = 0, issuesUpdated = 0, issuesRemoved = 0;

        foreach (var cvIssue in cvIssues)
        {
            var cvId = cvIssue.Id.ToString();
            int.TryParse(cvIssue.IssueNumber, out var issueNum);

            // Correspondance par SourceId en priorité, puis par numéro d'issue — le repli par
            // numéro ne se restreint PAS aux issues sans SourceId : un Rematch vers une source
            // différente laisse justement un ancien SourceId (d'une autre source) qui ne peut
            // jamais matcher cvId ci-dessus, donc jamais tomber dans ce repli si on l'exigeait vide
            // — l'issue restait orpheline et une nouvelle était créée au même numéro (bug constaté).
            var existing =
                existingIssues.FirstOrDefault(i => i.SourceId == cvId) ??
                existingIssues.FirstOrDefault(i => i.IssueNumber == issueNum && !matchedExistingIds.Contains(i.Id));

            var mappedIssue = Mapper.Map(cvIssue);

            if (existing is not null)
            {
                matchedExistingIds.Add(existing.Id);
                existing.SourceId     = mappedIssue.SourceId;
                existing.Title        = mappedIssue.Title;
                existing.Year         = mappedIssue.Year;
                existing.Description  = mappedIssue.Description;
                existing.Image        = mappedIssue.Image;
                existing.Authors      = mappedIssue.Authors;
                if (existing.Status == IssueStatus.MISSING)
                    existing.IssueNumber = issueNum;
                issuesUpdated++;
                JobSendTrace($"[Rematch] Issue #{existing.IssueNumber} metadata updated (status unchanged: {existing.Status})");
            }
            else
            {
                var newIssue = mappedIssue;
                newIssue.Id      = Guid.NewGuid();
                newIssue.VolumeId = volumeId;
                newIssue.Status  = IssueStatus.MISSING;
                ctx.Issues.Add(newIssue);
                issuesAdded++;
                JobSendTrace($"[Rematch] New issue #{issueNum} — '{newIssue.Title}' detected (added as MISSING)");
            }
        }

        // Supprimer les issues MISSING non appariées
        foreach (var orphan in existingIssues.Where(i => !matchedExistingIds.Contains(i.Id)))
        {
            if (orphan.Status == IssueStatus.MISSING)
            {
                ctx.Issues.Remove(orphan);
                issuesRemoved++;
                JobSendTrace($"[Rematch] Orphaned issue #{orphan.IssueNumber} removed (was MISSING, no longer on source)");
            }
            // DOWNLOADED / DOWNLOADING → conservé
        }

        await ctx.SaveChangesAsync(ct);
        await RecalculateVolumeStatisticsAsync(ctx, volumeId, ct);
        return new RematchResult(issuesAdded, issuesUpdated, issuesRemoved);
    }

    // Mode "NEW issues only" du Refresh ComicVine. La metadata Volume a déjà été recopiée par
    // l'appelant. La liste d'ids source vient directement de cvVolume.Issues (déjà récupérée par
    // GetVolumeAsync, aucun appel réseau de plus) — ces refs NE portent PAS le numéro, donc un
    // GetIssueAsync (détail : issue_number + person_credits) reste nécessaire pour chaque nouvel
    // id. Les issues déjà connues ne sont pas touchées (pas de maj, pas de suppression).
    private async Task<RematchResult?> SyncNewComicVineIssuesAsync(
        DbStorageContext ctx, Volume volume, CvVolume cvVolume,
        ComicVineSourceService comicVine, CancellationToken ct, ProgressionCallback? progression)
    {
        var known = (await ctx.Issues.Where(i => i.VolumeId == volume.Id)
            .Select(i => i.SourceId).ToListAsync(ct)).ToHashSet();
        var newIds = (cvVolume.Issues ?? [])
            .Select(r => r.Id)
            .Where(id => !known.Contains(id.ToString()))
            .Distinct()
            .ToList();

        JobSendTrace($"[Refresh] {newIds.Count} new ComicVine issue(s) to fetch (of {cvVolume.Issues?.Count ?? 0} on source)");
        progression?.UpdateTotal(newIds.Count);
        var progress = new Progression { Total = newIds.Count };

        var added = 0;
        foreach (var id in newIds)
        {
            var cvIssue = await comicVine.GetIssueAsync(id, ct);
            if (cvIssue is not null)
            {
                var issue = Mapper.Map(cvIssue);
                issue.Id       = Guid.NewGuid();
                issue.VolumeId = volume.Id;
                issue.Status   = IssueStatus.MISSING;
                ctx.Issues.Add(issue);
                added++;
                JobSendTrace($"[Refresh] New issue #{issue.IssueNumber} — '{issue.Title}' added as MISSING");
            }
            progress.Increment(cvIssue is not null);
            progression?.Callback(progress);
            await Task.Delay(500, ct); // parité avec GetAllIssuesForVolumeAsync (politesse au-delà du RateLimiter)
        }

        await ctx.SaveChangesAsync(ct);
        await RecalculateVolumeStatisticsAsync(ctx, volume.Id, ct);
        return new RematchResult(added, 0, 0);
    }

    // Miroir de AddVolumeFromComicVineAsync pour la source Bedetheque : utilise les modèles natifs
    // riches de BedethequeSourceService (BdSerie/BdAlbum, avec auteurs) plutôt que le DTO mince
    // SourceVolume/SourceIssue utilisé pour l'agrégation de recherche.
    public async Task<AddVolumeResult> AddVolumeFromBedethequeAsync(
        Guid libraryId, int bdSerieId, CancellationToken ct = default)
    {
        var ctx = GetDb();

        _ = await ctx.Libraries.FindAsync([libraryId], ct)
            ?? throw new KeyNotFoundException($"Library {libraryId} not found.");

        var duplicate = await ctx.Volumes.FirstOrDefaultAsync(
            v => v.LibraryId == libraryId && v.SourceType == "bedetheque" && v.SourceId == bdSerieId.ToString(), ct);
        if (duplicate is not null)
            throw new InvalidOperationException($"Volume {bdSerieId} already exists in this library.");

        var bedetheque = GetService<BedethequeSourceService, BedethequeOptions>();
        if (bedetheque.CurrentState.State != EState.OK)
            throw new InvalidOperationException("Bedetheque service is not available");

        var bdSerie = await bedetheque.GetSerieAsync(bdSerieId, ct)
            ?? throw new KeyNotFoundException($"Bedetheque serie {bdSerieId} not found.");

        var now = DateTime.UtcNow;
        var volume = Mapper.Map(bdSerie);
        volume.Id = Guid.NewGuid();
        volume.LibraryId = libraryId;
        volume.Status = VolumeStatus.MONITORED;
        volume.CreatedAt = now;
        volume.UpdatedAt = now;
        volume.DateAdded = now;
        volume.CountOfIssues = 0;
        ctx.Volumes.Add(volume);
        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));

        // Le peuplement des issues continue en tâche de fond (Job) — un appel HTTP par album côté
        // Bedetheque, donc potentiellement long ; le Volume existe déjà et a été notifié.
        var job = StartJob($"Add volume — {volume.Title}");
        if (job.State != JobState.ERROR)
        {
            job.SetState(JobState.RUNNING);
            _ = RunAddBedethequeIssuesJobAsync(job, volume.Id, bdSerieId);
        }
        return new AddVolumeResult(volume, job.JobId);
    }

    // Bedetheque ne porte pas d'auteurs au niveau Serie (contrairement à ComicVine et son
    // cvVolume.People) : Volume.Authors doit être reconstruit comme l'union dédoublonnée (par nom,
    // premier rôle non vide rencontré) des auteurs de tous les albums. Les entrées déjà présentes
    // sur le volume sont conservées (rôle complété si vide), les nouvelles ajoutées à la suite.
    private static void MergeIssueAuthorsIntoVolume(Volume volume, IEnumerable<VolumeAuthor> issueAuthors)
    {
        var roleByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        List<string> order = [];
        foreach (var a in issueAuthors)
        {
            if (string.IsNullOrWhiteSpace(a.Name)) continue;
            if (!roleByName.TryGetValue(a.Name, out var known))
            {
                roleByName[a.Name] = a.Role ?? string.Empty;
                order.Add(a.Name);
            }
            else if (string.IsNullOrEmpty(known) && !string.IsNullOrEmpty(a.Role))
            {
                roleByName[a.Name] = a.Role;
            }
        }

        var merged = volume.Authors
            .Select(a => string.IsNullOrEmpty(a.Role) && roleByName.TryGetValue(a.Name, out var role)
                ? new VolumeAuthor(a.Name, role)
                : a)
            .ToList();
        var present = new HashSet<string>(merged.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
        merged.AddRange(order.Where(n => !present.Contains(n)).Select(n => new VolumeAuthor(n, roleByName[n])));
        volume.Authors = merged;
    }

    private async Task RunAddBedethequeIssuesJobAsync(JobContext job, Guid volumeId, int bdSerieId)
    {
        var ctx = GetDb();
        try
        {
            var volume = await ctx.Volumes.FindAsync(volumeId);
            if (volume is null) { EndJob(false); return; }

            var bedetheque = GetService<BedethequeSourceService, BedethequeOptions>();
            var bdAlbums = await bedetheque.GetAllAlbumsForSerieAsync(bdSerieId, progression: job.CallbackHandler);
            // Repart de zéro pour la phase de création des issues — voir le commentaire équivalent
            // dans RunAddComicVineIssuesJobAsync.
            job.Progress.Reset();
            job.CallbackHandler.UpdateTotal(bdAlbums.Count);

            List<VolumeAuthor> allIssueAuthors = [];

            foreach (var bdAlbum in bdAlbums)
            {
                var issue = Mapper.Map(bdAlbum);
                issue.Id = Guid.NewGuid();
                issue.Status = IssueStatus.MISSING;
                issue.VolumeId = volume.Id;
                allIssueAuthors.AddRange(issue.Authors);
                ctx.Issues.Add(issue);
                JobSendTrace($"[Add] {volume.Title} — issue #{issue.IssueNumber} ({issue.Category})");
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);
            }
            if (bdAlbums.Count > 0)
            {
                MergeIssueAuthorsIntoVolume(volume, allIssueAuthors);

                // Complétude basée uniquement sur les tomes Standard — voir RecalculateVolumeStatisticsAsync.
                volume.CountOfIssues = bdAlbums.Count(a => a.Category == "Standard");
                volume.UpdatedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));
            }
            EndJob(true);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Add] Unexpected error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
    }

    // Miroir de RematchVolumeFromComicVineAsync pour la source Bedetheque.
    public async Task<RematchResult?> RematchVolumeFromBedethequeAsync(
        Guid volumeId, int bdSerieId, CancellationToken ct = default, ProgressionCallback? progression = null,
        bool newIssuesOnly = false)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync([volumeId], ct);
        if (volume is null) return null;

        var bedetheque = GetService<BedethequeSourceService, BedethequeOptions>();
        if (bedetheque.CurrentState.State != EState.OK)
            throw new InvalidOperationException("Bedetheque service is not available");

        // forceRefresh: true — un Refresh est une demande explicite d'aller voir la source
        // maintenant ; sans ça le cache 24h masquerait un tome ajouté récemment (bénéficie aux
        // deux modes).
        var bdSerie = await bedetheque.GetSerieAsync(bdSerieId, ct, forceRefresh: true)
            ?? throw new KeyNotFoundException($"Bedetheque serie {bdSerieId} not found.");

        var mapped = Mapper.Map(bdSerie);
        volume.SourceId     = mapped.SourceId;
        volume.SourceType   = mapped.SourceType;
        volume.Title        = mapped.Title;
        volume.Year         = mapped.Year;
        volume.Description  = mapped.Description;
        volume.Image        = mapped.Image;
        volume.Publisher    = mapped.Publisher;
        volume.Authors      = mapped.Authors;
        volume.Genres       = mapped.Genres;
        volume.Language           = mapped.Language;
        volume.PublicationStatus  = mapped.PublicationStatus;
        volume.Origin             = mapped.Origin;
        volume.Website            = mapped.Website;
        volume.UpdatedAt    = DateTime.UtcNow;
        JobSendTrace($"[Rematch] Volume metadata: title='{volume.Title}', year={volume.Year}, publisher='{volume.Publisher}'");

        // Mode "NEW issues only" : metadata Serie déjà recopiée, on ne récupère la page détail que
        // pour les albums encore absents en base et on laisse les issues connues intactes.
        if (newIssuesOnly)
            return await SyncNewBedethequeAlbumsAsync(ctx, volume, bdSerie, bedetheque, ct, progression);

        var bdAlbums = await bedetheque.GetAllAlbumsForSerieAsync(bdSerieId, ct, progression);
        var existingIssues = await ctx.Issues.Where(i => i.VolumeId == volumeId).ToListAsync(ct);

        var matchedExistingIds = new HashSet<Guid>();
        int issuesAdded = 0, issuesUpdated = 0, issuesRemoved = 0;

        foreach (var bdAlbum in bdAlbums)
        {
            var bdId = bdAlbum.Id.ToString();
            var mappedIssue = Mapper.Map(bdAlbum);

            // Correspondance par SourceId en priorité, puis par (Category, IssueNumber) — le repli
            // ne se restreint PAS aux issues sans SourceId : un Rematch vers une source différente
            // laisse justement un ancien SourceId (d'une autre source) qui ne peut jamais matcher
            // bdId ci-dessus, donc jamais tomber dans ce repli si on l'exigeait vide — l'issue
            // restait orpheline et une nouvelle était créée au même numéro (bug constaté).
            // Category est incluse dans le repli (pas seulement IssueNumber) : le numéro seul n'est
            // pas unique au sein d'une série (ex. "INT1" et "INTTT1" sont tous deux Omnibus/1) —
            // best-effort, ne garantit pas d'éliminer toutes les collisions.
            var existing =
                existingIssues.FirstOrDefault(i => i.SourceId == bdId) ??
                existingIssues.FirstOrDefault(i => i.Category == mappedIssue.Category && i.IssueNumber == mappedIssue.IssueNumber && !matchedExistingIds.Contains(i.Id));

            if (existing is not null)
            {
                matchedExistingIds.Add(existing.Id);
                existing.SourceId     = mappedIssue.SourceId;
                // IssueNumber/Category recopiés SANS condition de Status (contrairement au reste du
                // rematch avant l'introduction de la catégorisation) : les issues téléchargées avant
                // BedethequeAlbumClassifier peuvent porter un IssueNumber/Category historiquement
                // erroné (ex. 0/Standard pour un hors-série) qui doit se corriger lui aussi. Si
                // RegenerateComicInfo est coché sur ce Refresh, RegenerateComicInfoForDownloadedIssuesAsync
                // (exécuté juste après) renomme le fichier .cbz en conséquence, comme il le fait déjà
                // pour Title/Year.
                existing.IssueNumber  = mappedIssue.IssueNumber;
                existing.Category     = mappedIssue.Category;
                existing.Title        = mappedIssue.Title;
                existing.Year         = mappedIssue.Year;
                existing.Description  = mappedIssue.Description;
                existing.Image        = mappedIssue.Image;
                existing.Authors      = mappedIssue.Authors;
                existing.Ean                   = mappedIssue.Ean;
                existing.Collection             = mappedIssue.Collection;
                existing.Publisher              = mappedIssue.Publisher;
                existing.LegalDepositDate       = mappedIssue.LegalDepositDate;
                existing.OfficialPageCount      = mappedIssue.OfficialPageCount;
                existing.Genre                  = mappedIssue.Genre;
                existing.CommunityRating        = mappedIssue.CommunityRating;
                existing.CommunityRatingCount   = mappedIssue.CommunityRatingCount;
                issuesUpdated++;
                JobSendTrace($"[Rematch] Issue #{existing.IssueNumber} ({existing.Category}) metadata updated (status unchanged: {existing.Status})");
            }
            else
            {
                var newIssue = mappedIssue;
                newIssue.Id      = Guid.NewGuid();
                newIssue.VolumeId = volumeId;
                newIssue.Status  = IssueStatus.MISSING;
                ctx.Issues.Add(newIssue);
                issuesAdded++;
                JobSendTrace($"[Rematch] New issue #{newIssue.IssueNumber} ({newIssue.Category}) — '{newIssue.Title}' detected (added as MISSING)");
            }
        }

        // Supprimer les issues MISSING non appariées
        foreach (var orphan in existingIssues.Where(i => !matchedExistingIds.Contains(i.Id)))
        {
            if (orphan.Status == IssueStatus.MISSING)
            {
                ctx.Issues.Remove(orphan);
                issuesRemoved++;
                JobSendTrace($"[Rematch] Orphaned issue #{orphan.IssueNumber} removed (was MISSING, no longer on source)");
            }
            // DOWNLOADED / DOWNLOADING → conservé
        }

        // volume.Authors a été remis à vide par Mapper.Map(bdSerie) plus haut — on le reconstruit
        // depuis les albums (appariés mis à jour + nouveaux + téléchargés conservés).
        MergeIssueAuthorsIntoVolume(volume, ctx.Issues.Local
            .Where(i => i.VolumeId == volumeId && ctx.Entry(i).State != EntityState.Deleted)
            .SelectMany(i => i.Authors));

        await ctx.SaveChangesAsync(ct);
        await RecalculateVolumeStatisticsAsync(ctx, volumeId, ct);
        return new RematchResult(issuesAdded, issuesUpdated, issuesRemoved);
    }

    // Mode "NEW issues only" du Refresh Bedetheque. La metadata Serie a déjà été recopiée par
    // l'appelant. bdSerie.Albums est la liste RÉSUMÉ classée sur la série ENTIÈRE (Category/Idx
    // résolus par BedethequeAlbumClassifier + ResolveMissingIndices + NormalizeSingleAlbumSeries
    // sur tout le set) — on la garde telle quelle et on ne fait un GetAlbumAsync (page détail :
    // auteurs/EAN/...) QUE pour les albums absents en base. On NE re-classe JAMAIS un sous-ensemble.
    private async Task<RematchResult?> SyncNewBedethequeAlbumsAsync(
        DbStorageContext ctx, Volume volume, BdSerie bdSerie,
        BedethequeSourceService bedetheque, CancellationToken ct, ProgressionCallback? progression)
    {
        var known = (await ctx.Issues.Where(i => i.VolumeId == volume.Id)
            .Select(i => i.SourceId).ToListAsync(ct)).ToHashSet();
        var newSummaries = bdSerie.Albums
            .Where(a => !known.Contains(a.Id.ToString()))
            .ToList();

        JobSendTrace($"[Refresh] {newSummaries.Count} new Bedetheque album(s) to fetch (of {bdSerie.Albums.Count} on source)");
        progression?.UpdateTotal(newSummaries.Count);
        var progress = new Progression { Total = newSummaries.Count };

        var added = 0;
        for (var i = 0; i < newSummaries.Count; i++)
        {
            var summary = newSummaries[i];
            JobSendTrace($"[Refresh] Album {i + 1}/{newSummaries.Count} — {summary.Titre}");
            var album = await bedetheque.GetAlbumAsync(summary.Id, ct);
            if (album is not null)
            {
                // Category/Idx viennent de la page liste (résolus sur la série entière), pas de la
                // page détail — cf. GetAllAlbumsForSerieAsync.
                var issue = Mapper.Map(album with { Category = summary.Category, Idx = summary.Idx });
                issue.Id       = Guid.NewGuid();
                issue.VolumeId = volume.Id;
                issue.Status   = IssueStatus.MISSING;
                ctx.Issues.Add(issue);
                added++;
                JobSendTrace($"[Refresh] New issue #{issue.IssueNumber} ({issue.Category}) — '{issue.Title}' added as MISSING");
            }
            progress.Increment(album is not null);
            progression?.Callback(progress);
        }

        // volume.Authors a été remis à vide par Mapper.Map(bdSerie) chez l'appelant — on le
        // reconstruit depuis toutes les issues du volume (connues en base + nouvelles ci-dessus).
        var knownAuthors = await ctx.Issues
            .Where(i => i.VolumeId == volume.Id)
            .Select(i => i.Authors)
            .ToListAsync(ct);
        MergeIssueAuthorsIntoVolume(volume, knownAuthors
            .SelectMany(a => a)
            .Concat(ctx.Issues.Local.Where(i => i.VolumeId == volume.Id && ctx.Entry(i).State == EntityState.Added)
                .SelectMany(i => i.Authors)));

        await ctx.SaveChangesAsync(ct);
        await RecalculateVolumeStatisticsAsync(ctx, volume.Id, ct);
        return new RematchResult(added, 0, 0);
    }

    // Dispatchers génériques utilisés par les contrôleurs Web — routent vers l'implémentation
    // dédiée à la source choisie par l'utilisateur dans les résultats de recherche.
    public Task<AddVolumeResult> AddVolumeFromSourceAsync(Guid libraryId, string source, string sourceId, CancellationToken ct = default) =>
        source switch
        {
            "comicvine" => AddVolumeFromComicVineAsync(libraryId, int.Parse(sourceId), ct),
            "bedetheque" => AddVolumeFromBedethequeAsync(libraryId, int.Parse(sourceId), ct),
            _ => throw new InvalidOperationException($"Unknown source '{source}'"),
        };

    public Task<RematchResult?> RematchVolumeFromSourceAsync(Guid volumeId, string source, string sourceId,
        CancellationToken ct = default, ProgressionCallback? progression = null, bool newIssuesOnly = false) =>
        source.ToLowerInvariant() switch
        {
            "comicvine" => RematchVolumeFromComicVineAsync(volumeId, int.Parse(sourceId), ct, progression, newIssuesOnly),
            "bedetheque" => RematchVolumeFromBedethequeAsync(volumeId, int.Parse(sourceId), ct, progression, newIssuesOnly),
            _ => throw new InvalidOperationException($"Unknown source '{source}'"),
        };

    // Lance un rematch (changement de série) ou un refresh (même série) en tâche de fond, suivi
    // en direct sur la page Volume via SignalR — mêmes traces que la recherche/l'analyse CBZ.
    public JobContext LaunchJobRematchVolume(RematchVolumeJobParameters parameters)
    {
        var ctx = GetDb();
        var volume = ctx.Volumes.Find(parameters.VolumeId);
        var jobLabel = parameters.IsRefresh ? "Refresh" : "Rematch";
        var jobTitle = volume is not null ? $"{jobLabel} — {volume.Title}" : $"{jobLabel} — {parameters.VolumeId}";

        var job = StartJob(jobTitle, parameters);
        if (job.State != JobState.ERROR)
        {
            job.SetState(JobState.RUNNING);
            _ = RunRematchVolumeJobAsync(job, parameters);
        }
        return job;
    }

    // Pré-check synchrone : Source/SourceId doivent être connus AVANT de construire les paramètres
    // du job (contrairement à Rematch, où ils viennent de la requête). Volume introuvable ou manuel
    // → pas de job créé (le bouton "Refresh" est de toute façon masqué côté UI pour un volume manuel).
    // Les booléens pilotent la popup à cases à cocher côté front (sync source / check files / stats /
    // ComicInfo / Kavita) — sync/stats/ComicInfo/Kavita par défaut à true (comportement identique à
    // avant si l'appelant ne les précise pas). syncNewIssuesOnly (radio sous "Sync with source",
    // défaut false = historique) : ne récupérer de la source que les issues/albums encore inconnus.
    // regenerateComicInfoNewOnly (radio sous "Regenerate ComicInfo.xml", défaut false) : ne
    // réinjecter que dans les CBZ sans ComicInfo.xml. checkFiles (défaut false) : vérifier présence
    // disque + fraîcheur d'analyse des CBZ avant le recalc de stats — voir RematchVolumeJobParameters.
    public async Task<JobContext?> LaunchJobRefreshVolume(
        Guid volumeId,
        bool syncFromSource = true,
        bool recalculateStatistics = true,
        bool regenerateComicInfo = true,
        bool scanKavita = true,
        bool syncNewIssuesOnly = false,
        bool regenerateComicInfoNewOnly = false,
        bool checkFiles = false,
        CancellationToken ct = default)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync([volumeId], ct);
        if (volume is null) return null;
        if (string.Equals(volume.SourceType, "manual", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This volume was added manually — there is no external source to refresh from.");

        return LaunchJobRematchVolume(new RematchVolumeJobParameters
        {
            VolumeId = volumeId, Source = volume.SourceType, SourceId = volume.SourceId,
            SyncFromSource = syncFromSource, RecalculateStatistics = recalculateStatistics,
            RegenerateComicInfo = regenerateComicInfo, ScanKavita = scanKavita,
            SyncNewIssuesOnly = syncNewIssuesOnly,
            RegenerateComicInfoNewOnly = regenerateComicInfoNewOnly,
            CheckFiles = checkFiles,
            IsRefresh = true
        });
    }

    // Lance un Job "Refresh" indépendant par volume de la library, en réutilisant
    // LaunchJobRefreshVolume tel quel — pas de job parent (le modèle Job actuel ne supporte pas
    // l'imbrication : un seul _currentJob ambiant à la fois, EndJob() le réinitialise globalement).
    // Chaque volume manuel est silencieusement exclu (LaunchJobRefreshVolume lève une exception
    // pour eux dans tous les cas — comportement connu, cohérent avec le bouton déjà masqué côté
    // page Volume). Les jobs individuels tournent en parallèle (fire-and-forget déjà interne à
    // LaunchJobRefreshVolume) ; les sources externes (ComicVine/Bedetheque) ont déjà leur propre
    // RateLimiter interne, donc pas de throttling supplémentaire nécessaire ici.
    public async Task<List<Guid>> LaunchJobsRefreshLibrary(
        Guid libraryId, bool syncFromSource, bool recalculateStatistics,
        bool regenerateComicInfo, bool scanKavita, bool syncNewIssuesOnly = false,
        bool regenerateComicInfoNewOnly = false, bool checkFiles = false, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var volumeIds = await ctx.Volumes
            .Where(v => v.LibraryId == libraryId)
            .Select(v => v.Id)
            .ToListAsync(ct);

        var jobIds = new List<Guid>();
        foreach (var volumeId in volumeIds)
        {
            try
            {
                var job = await LaunchJobRefreshVolume(
                    volumeId, syncFromSource, recalculateStatistics, regenerateComicInfo, scanKavita,
                    syncNewIssuesOnly, regenerateComicInfoNewOnly, checkFiles, ct);
                if (job is not null) jobIds.Add(job.JobId);
            }
            catch (InvalidOperationException)
            {
                // Volume manuel — pas de source à rafraîchir, exclu silencieusement du lot.
            }
        }
        return jobIds;
    }

    private async Task RunRematchVolumeJobAsync(JobContext job, RematchVolumeJobParameters parameters)
    {
        try
        {
            var ctx = GetDb();
            var volumeBefore = await ctx.Volumes.FindAsync(parameters.VolumeId);
            if (volumeBefore is null)
            {
                JobSendTrace("[Rematch] Volume not found", ETraceLevel.ERROR);
                EndJob(false);
                return;
            }

            // Dossier RÉELLEMENT présent avant mutation — Volume n'a pas de Path propre, son dossier
            // est toujours recalculé depuis Title/Year ; il faut donc capturer l'ancien dossier avant
            // que le rematch n'écrase ces champs, pour pouvoir le renommer ensuite.
            // La capture est inconditionnelle : le nom attendu change aussi quand la normalisation du
            // titre évolue (fusion des espaces), sans que Title/Year bougent — et dans ce cas le
            // chemin calculé ne suffit pas, seul le dossier existant sur disque fait foi.
            string? oldFolderPath = null;
            var libraryBefore = await ctx.Libraries.FindAsync(volumeBefore.LibraryId);
            if (libraryBefore is not null)
                oldFolderPath = ArchiveService.FindExistingVolumeDirectory(volumeBefore, libraryBefore);

            if (parameters.SyncFromSource)
            {
                JobSendTrace($"[Rematch] Fetching {parameters.Source} #{parameters.SourceId}..."
                    + (parameters.SyncNewIssuesOnly ? " (new issues only)" : string.Empty));
                var result = await RematchVolumeFromSourceAsync(
                    parameters.VolumeId, parameters.Source, parameters.SourceId,
                    progression: job.CallbackHandler, newIssuesOnly: parameters.SyncNewIssuesOnly);
                if (result is null)
                {
                    JobSendTrace("[Rematch] Volume not found", ETraceLevel.ERROR);
                    EndJob(false);
                    return;
                }
                JobSendTrace($"[Rematch] Metadata synced — {result.IssuesAdded} issue(s) added, {result.IssuesUpdated} updated, {result.IssuesRemoved} removed");
                // RematchVolumeFromComicVineAsync/BedethequeAsync recalculent déjà les statistiques
                // en interne — inutile de le refaire ici, sauf si "Check files" change des statuts ensuite.

                // Horodate la dernière synchro source (refresh manuel OU job de roulement) — sert au
                // tri du "rolling refresh" du scheduler. Contexte neuf, un seul champ écrit.
                await StampVolumeLastRefreshedAsync(parameters.VolumeId);
            }

            // Renommages AVANT "Check files" : ce dernier sonde les fichiers sous le dossier
            // recalculé, et les déclarerait manquants si dossier ou fichiers portaient encore leur
            // ancien nom. Inconditionnels : le nom attendu change aussi bien quand la source met à
            // jour un titre que quand la normalisation du nom évolue, sans rapport avec les cases
            // cochées dans la popup.
            await RenameVolumeDirectoryIfNeededAsync(parameters.VolumeId, oldFolderPath);
            var renamedIssueIds = await RenameIssueFilesIfNeededAsync(parameters.VolumeId);

            // "Check files" — avant le recalc de stats : peut repasser des issues DOWNLOADED en MISSING.
            var filesFlipped = false;
            if (parameters.CheckFiles)
                filesFlipped = await CheckVolumeFilesAsync(job, parameters.VolumeId);

            // Recalc : le sync l'a déjà fait en interne ; sinon on (re)calcule si l'utilisateur l'a
            // demandé, ou si "Check files" a repassé des issues en MISSING (y compris après un sync).
            // Surcharge publique (contexte neuf) : `ctx` local porte un Volume potentiellement périmé
            // après le sync (fait sur son propre contexte) — le réutiliser réécrirait des métadonnées.
            if ((parameters.RecalculateStatistics && !parameters.SyncFromSource) || filesFlipped)
            {
                await RecalculateVolumeStatisticsAsync(parameters.VolumeId);
                JobSendTrace("[Rematch] Statistics recalculated");
            }

            var syncOk = true;
            if (parameters.RegenerateComicInfo)
                syncOk = await RegenerateComicInfoForDownloadedIssuesAsync(
                    job, parameters.VolumeId, parameters.RegenerateComicInfoNewOnly, renamedIssueIds);

            if (parameters.ScanKavita)
                await TriggerKavitaScanAsync(parameters.VolumeId);

            EndJob(syncOk);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Rematch] Unexpected error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
    }

    public async Task<string> UploadImageAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var archiveService = GetService<ArchiveService, ArchiveOption>();
        var dir = archiveService.ImagesPath;
        Directory.CreateDirectory(dir);
        var filename = $"{Guid.NewGuid()}{extension}";
        var path = Path.Combine(dir, filename);
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct);
        return $"/images/{filename}";
    }

    public async Task<Volume> AddVolumeManuallyAsync(
        Guid libraryId,
        string title,
        int? year,
        string? publisher,
        string? description,
        string? imageUrl,
        List<VolumeAuthor> authors,
        List<string> genres,
        List<(int Number, string? Title, int? Year, string? Description, string? ImageUrl)> issues,
        CancellationToken ct = default)
    {
        var ctx = GetDb();

        _ = await ctx.Libraries.FindAsync([libraryId], ct)
            ?? throw new KeyNotFoundException($"Library {libraryId} not found.");

        var now = DateTime.UtcNow;

        VolumeImage? volumeImage = null;
        if (!string.IsNullOrEmpty(imageUrl))
            volumeImage = new VolumeImage(imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, null);

        var volume = new Volume
        {
            Id = Guid.NewGuid(),
            LibraryId = libraryId,
            SourceId = Guid.NewGuid().ToString(),
            SourceType = "manual",
            Title = title,
            Year = year,
            Publisher = publisher,
            Description = description,
            Image = volumeImage,
            Authors = authors,
            Genres = genres,
            Status = VolumeStatus.MONITORED,
            CreatedAt = now,
            UpdatedAt = now,
            DateAdded = now,
            CountOfIssues = issues.Count
        };

        ctx.Volumes.Add(volume);

        foreach (var issueData in issues)
        {
            VolumeImage? issueImage = null;
            if (!string.IsNullOrEmpty(issueData.ImageUrl))
                issueImage = new VolumeImage(issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, null);

            ctx.Issues.Add(new Issue
            {
                Id = Guid.NewGuid(),
                VolumeId = volume.Id,
                SourceId = string.Empty,
                IssueNumber = issueData.Number,
                Title = issueData.Title,
                Year = issueData.Year,
                Description = issueData.Description,
                Image = issueImage,
                Authors = [],
                Status = IssueStatus.MISSING
            });
        }

        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));

        return volume;
    }

    public async Task<bool> UpdateVolumeManuallyAsync(
        Guid volumeId,
        string title,
        int? year,
        string? publisher,
        string? description,
        string? imageUrl,
        List<VolumeAuthor> authors,
        List<string> genres,
        List<(Guid? Id, int Number, string? Title, int? Year, string? Description, string? ImageUrl)> issues,
        CancellationToken ct = default)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync([volumeId], ct);
        if (volume is null) return false;

        VolumeImage? volumeImage = null;
        if (!string.IsNullOrEmpty(imageUrl))
            volumeImage = new VolumeImage(imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, null);

        volume.Title       = title;
        volume.Year        = year;
        volume.Publisher   = publisher;
        volume.Description = description;
        volume.Image       = volumeImage;
        volume.Authors     = authors;
        volume.Genres      = genres;
        volume.UpdatedAt   = DateTime.UtcNow;

        // ── Issues ────────────────────────────────────────────────────────────
        var existingIssues = await ctx.Issues.Where(i => i.VolumeId == volumeId).ToListAsync(ct);
        var requestedIds   = issues.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();

        // Supprimer les issues MISSING absentes du formulaire
        foreach (var existing in existingIssues.Where(i => !requestedIds.Contains(i.Id)))
        {
            if (existing.Status == IssueStatus.MISSING)
                ctx.Issues.Remove(existing);
            // DOWNLOADED / DOWNLOADING → conservé intact
        }

        // Mettre à jour les issues existantes référencées par Id
        foreach (var req in issues.Where(i => i.Id.HasValue))
        {
            var existing = existingIssues.FirstOrDefault(i => i.Id == req.Id!.Value);
            if (existing is null) continue;

            existing.Title       = req.Title;
            existing.Year        = req.Year;
            existing.Description = req.Description;
            existing.Image       = string.IsNullOrEmpty(req.ImageUrl) ? existing.Image
                : new VolumeImage(req.ImageUrl, req.ImageUrl, req.ImageUrl, req.ImageUrl, req.ImageUrl, req.ImageUrl, req.ImageUrl, req.ImageUrl, req.ImageUrl, null);
            if (existing.Status == IssueStatus.MISSING)
                existing.IssueNumber = req.Number;
        }

        // Créer les nouvelles issues (Id == null)
        foreach (var req in issues.Where(i => !i.Id.HasValue))
        {
            VolumeImage? issueImage = null;
            if (!string.IsNullOrEmpty(req.ImageUrl))
                issueImage = new VolumeImage(req.ImageUrl, req.ImageUrl, req.ImageUrl, req.ImageUrl, req.ImageUrl, req.ImageUrl, req.ImageUrl, req.ImageUrl, req.ImageUrl, null);

            ctx.Issues.Add(new Issue
            {
                Id           = Guid.NewGuid(),
                VolumeId     = volumeId,
                SourceId     = string.Empty,
                IssueNumber  = req.Number,
                Title        = req.Title,
                Year         = req.Year,
                Description  = req.Description,
                Image        = issueImage,
                Authors      = [],
                Status       = IssueStatus.MISSING
            });
        }

        await ctx.SaveChangesAsync(ct);
        await RecalculateVolumeStatisticsAsync(ctx, volumeId, ct);
        return true;
    }

    public async Task LaunchJobRegenerateComicInfo(RegenerateComicInfoJobParameters parameters)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync(parameters.VolumeId);
        var jobTitle = volume is not null
            ? $"Regenerate ComicInfo — {volume.Title}"
            : $"Regenerate ComicInfo — {parameters.VolumeId}";

        var job = StartJob(jobTitle, parameters);
        job.SetState(JobState.RUNNING);
        try
        {
            // Renommage d'abord : la réinjection vise le fichier au nom calculé, il faut donc que le
            // fichier y soit déjà. Le dossier n'est pas concerné ici (Title/Year ne changent pas).
            var renamedIssueIds = await RenameIssueFilesIfNeededAsync(parameters.VolumeId);
            var success = await RegenerateComicInfoForDownloadedIssuesAsync(
                job, parameters.VolumeId, newOnly: false, renamedIssueIds);
            if (success) await TriggerKavitaScanAsync(parameters.VolumeId);
            EndJob(success);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Regen] Unhandled error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
    }

    /// <summary>
    /// Aligne le dossier physique de la série sur le nom recalculé depuis <c>Title</c>/<c>Year</c>.
    /// <paramref name="oldFolderPath"/> est le dossier réellement présent capturé AVANT le sync
    /// (cf. <c>ArchiveService.FindExistingVolumeDirectory</c>) : il couvre le rematch vers une autre
    /// série, mais aussi une simple évolution de la normalisation du titre.
    /// <para>À appeler avant toute étape qui sonde les fichiers sur disque.</para>
    /// </summary>
    private async Task RenameVolumeDirectoryIfNeededAsync(Guid volumeId, string? oldFolderPath)
    {
        if (oldFolderPath is null || !Directory.Exists(oldFolderPath)) return;

        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync(volumeId);
        if (volume is null) return;
        var library = await ctx.Libraries.FindAsync(volume.LibraryId);
        if (library is null) return;

        var newFolderPath = ArchiveService.GetPath(volume, library);
        if (string.Equals(oldFolderPath, newFolderPath, StringComparison.OrdinalIgnoreCase)) return;

        if (Directory.Exists(newFolderPath))
        {
            JobSendTrace("[Sync] Target folder already exists — skipping folder rename", ETraceLevel.WARNING);
            return;
        }

        try
        {
            Directory.Move(oldFolderPath, newFolderPath);
            JobSendTrace($"[Sync] Renamed series folder '{Path.GetFileName(oldFolderPath)}' -> '{Path.GetFileName(newFolderPath)}'");
        }
        catch (IOException ex)
        {
            JobSendTrace($"[Sync] Could not rename volume folder: {ex.Message}", ETraceLevel.WARNING);
        }
    }

    /// <summary>
    /// Aligne le nom des fichiers CBZ déjà téléchargés sur le nom calculé depuis les métadonnées
    /// courantes — que le titre ait changé à la source ou que la normalisation du nom ait évolué.
    /// <para>
    /// Étape à part entière du Refresh, exécutée <b>inconditionnellement</b> et avant "Check files" :
    /// elle ne dépend pas de la case "Regenerate ComicInfo.xml", sans quoi un simple changement de
    /// normalisation laisserait indéfiniment des fichiers au nom périmé. Le dossier de la série est
    /// traité juste avant par <see cref="RenameVolumeDirectoryIfNeededAsync"/>.
    /// </para>
    /// </summary>
    /// <returns>Les ids des issues effectivement renommées (métadonnées à réécrire côté ComicInfo).</returns>
    private async Task<HashSet<Guid>> RenameIssueFilesIfNeededAsync(Guid volumeId)
    {
        var renamed = new HashSet<Guid>();

        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync(volumeId);
        if (volume is null) return renamed;
        var library = await ctx.Libraries.FindAsync(volume.LibraryId);
        if (library is null) return renamed;

        var archiveService = GetService<ArchiveService, ArchiveOption>();
        var folder = ArchiveService.GetPath(volume, library);

        var downloadedIssues = await ctx.Issues
            .Where(i => i.VolumeId == volumeId && i.Status == IssueStatus.DOWNLOADED)
            .ToListAsync();

        foreach (var issue in downloadedIssues)
        {
            if (string.IsNullOrEmpty(issue.CbzFilename)) continue;

            var expectedPath     = ArchiveService.GetPath(issue, volume, library);
            var expectedFileName = Path.GetFileName(expectedPath);
            if (issue.CbzFilename == expectedFileName) continue;

            var currentPath = Path.Combine(folder, issue.CbzFilename);
            if (!File.Exists(currentPath))
            {
                JobSendTrace($"[Sync] Expected file '{issue.CbzFilename}' not found — skipping rename for issue #{issue.IssueNumber}", ETraceLevel.WARNING);
                continue;
            }
            if (File.Exists(expectedPath) && !string.Equals(currentPath, expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                JobSendTrace($"[Sync] Target filename already exists — skipping rename for issue #{issue.IssueNumber}", ETraceLevel.WARNING);
                continue;
            }

            try
            {
                JobSendTrace($"[Sync] Renaming '{issue.CbzFilename}' -> '{expectedFileName}'");
                File.Move(currentPath, expectedPath);
                archiveService.EnsurePermissiveFileMode(expectedPath);
                issue.CbzFilename = expectedFileName;
                renamed.Add(issue.Id);
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issue.Id));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                JobSendTrace($"[Sync] Could not rename '{issue.CbzFilename}': {ex.Message}", ETraceLevel.WARNING);
            }
        }

        if (renamed.Count > 0) await ctx.SaveChangesAsync();
        return renamed;
    }

    // Réinjecte ComicInfo.xml dans les CBZ téléchargés. Les renommages (dossier de la série et
    // fichiers) sont traités en amont par RenameVolumeDirectoryIfNeededAsync /
    // RenameIssueFilesIfNeededAsync. Ne déclenche PAS le scan Kavita — cf. TriggerKavitaScanAsync,
    // appelée séparément (les deux étapes sont des cases à cocher indépendantes côté UI).
    //
    // newOnly (radio "NEW only" de la popup Refresh) : on ne réinjecte que dans les CBZ qui n'ont
    // pas déjà un ComicInfo.xml — sauf pour les issues de renamedIssueIds, dont le fichier vient
    // d'être renommé (métadonnées forcément à jour à réécrire). false (défaut, Rematch inclus) = on
    // réécrit ComicInfo.xml partout.
    private async Task<bool> RegenerateComicInfoForDownloadedIssuesAsync(
        JobContext job, Guid volumeId, bool newOnly = false, HashSet<Guid>? renamedIssueIds = null)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync(volumeId);
        if (volume is null) { JobSendTrace("[Sync] Volume not found", ETraceLevel.ERROR); return false; }

        var library = await ctx.Libraries.FindAsync(volume.LibraryId);
        if (library is null) { JobSendTrace("[Sync] Library not found", ETraceLevel.ERROR); return false; }

        renamedIssueIds ??= [];
        var archiveService = GetService<ArchiveService, ArchiveOption>();
        var downloadedIssues = await ctx.Issues
            .Where(i => i.VolumeId == volumeId && i.Status == IssueStatus.DOWNLOADED)
            .ToListAsync();

        JobSendTrace($"[Sync] {downloadedIssues.Count} downloaded issue(s) to process for {volume.Title}"
            + (newOnly ? " (new only — files already carrying a ComicInfo.xml are skipped)" : string.Empty));
        // Repart de zéro pour cette phase — une phase précédente du même Refresh (sync depuis la
        // source) a pu déjà faire progresser job.Progress jusqu'à 100%, voir RunAddComicVineIssuesJobAsync.
        job.Progress.Reset();
        job.CallbackHandler.UpdateTotal(downloadedIssues.Count);

        var injected = 0;
        var skipped = 0;
        foreach (var issue in downloadedIssues)
        {
            var expectedPath = ArchiveService.GetPath(issue, volume, library);

            // Mode "NEW only" : on saute les archives qui ont déjà un ComicInfo.xml (sauf renommage,
            // qui implique des métadonnées à jour à réécrire).
            if (newOnly && !renamedIssueIds.Contains(issue.Id) && archiveService.CbzContainsComicInfo(expectedPath))
            {
                skipped++;
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);
                continue;
            }

            JobSendTrace($"[Sync] Injecting ComicInfo.xml into {issue.CbzFilename}");
            await archiveService.InjectComicInfoIntoCbzAsync(volume, issue, expectedPath);
            injected++;
            job.Progress.Increment(true);
            job.CallbackHandler.Callback(job.Progress);
        }
        if (newOnly)
            JobSendTrace($"[Sync] ComicInfo.xml: {injected} (re)injected, {skipped} skipped (already present)");

        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volumeId));
        return true;
    }

    // Étape "Check files" du Refresh (avant Recalculate statistics). Pour chaque issue DOWNLOADED
    // ayant un CbzFilename :
    //  1. fichier absent du disque (sondé aux deux chemins possibles, comme DeleteIssueFileAsync)
    //     → l'issue repasse MISSING (ClearIssueDownloadState + suppression des IssueDownload) ;
    //  2. fichier présent mais jamais analysé (AnalyzedAt == null) → analyse CBZ ;
    //  3. fichier présent, déjà analysé, mais SHA-256 courant != AnalysisFileHash → ré-analyse.
    // Persiste et émet OnDataUpdated par issue modifiée. Ne recalcule PAS les stats (fait par
    // l'appelant). Retourne true si ≥1 issue est repassée MISSING.
    private async Task<bool> CheckVolumeFilesAsync(JobContext job, Guid volumeId)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync(volumeId);
        if (volume is null) { JobSendTrace("[CheckFiles] Volume not found", ETraceLevel.ERROR); return false; }

        var library = await ctx.Libraries.FindAsync(volume.LibraryId);
        if (library is null) { JobSendTrace("[CheckFiles] Library not found", ETraceLevel.ERROR); return false; }

        var archiveService = GetService<ArchiveService, ArchiveOption>();
        var scoringSettings = GetService<KavitaService, KavitaOptions>().BuildScoringSettings();

        var issues = await ctx.Issues
            .Where(i => i.VolumeId == volumeId && i.Status == IssueStatus.DOWNLOADED && i.CbzFilename != null)
            .ToListAsync();

        JobSendTrace($"[CheckFiles] {issues.Count} downloaded issue(s) to check for {volume.Title}");
        job.Progress.Reset();
        job.CallbackHandler.UpdateTotal(issues.Count);

        var seriesFolder = ArchiveService.GetPath(volume, library);
        var changedIssueIds = new List<Guid>();
        var (missing, analyzed, reanalyzed, unchanged) = (0, 0, 0, 0);
        foreach (var issue in issues)
        {
            // Le fichier peut porter le nom calculé depuis les métadonnées actuelles OU le nom
            // enregistré à l'import (dérive possible si les métadonnées ont changé sans regen).
            var cbzPath = new[]
                {
                    ArchiveService.GetPath(issue, volume, library),
                    Path.Combine(seriesFolder, issue.CbzFilename!)
                }
                .Distinct()
                .FirstOrDefault(File.Exists);

            if (cbzPath is null)
            {
                JobSendTrace($"[CheckFiles] #{issue.IssueNumber} '{issue.CbzFilename}' — CBZ file gone → back to MISSING", ETraceLevel.WARNING);
                ClearIssueDownloadState(issue);
                ctx.IssueDownloads.RemoveRange(ctx.IssueDownloads.Where(d => d.IssueId == issue.Id));
                missing++;
                changedIssueIds.Add(issue.Id);
            }
            else if (issue.AnalyzedAt is null)
            {
                JobSendTrace($"[CheckFiles] #{issue.IssueNumber} — never analyzed → analyzing");
                await AnalyzeIssueFileAsync(issue, cbzPath, archiveService, scoringSettings);
                analyzed++;
                changedIssueIds.Add(issue.Id);
            }
            else
            {
                var hash = await ArchiveService.ComputeFileHashAsync(cbzPath);
                if (!string.Equals(hash, issue.AnalysisFileHash, StringComparison.OrdinalIgnoreCase))
                {
                    JobSendTrace($"[CheckFiles] #{issue.IssueNumber} — file changed since last analysis → re-analyzing");
                    await AnalyzeIssueFileAsync(issue, cbzPath, archiveService, scoringSettings, hash);
                    reanalyzed++;
                    changedIssueIds.Add(issue.Id);
                }
                else
                {
                    unchanged++;
                }
            }

            job.Progress.Increment(true);
            job.CallbackHandler.Callback(job.Progress);
        }

        await ctx.SaveChangesAsync();
        foreach (var id in changedIssueIds)
            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(id));

        JobSendTrace($"[CheckFiles] Done — {missing} back to MISSING, {analyzed} analyzed, {reanalyzed} re-analyzed, {unchanged} unchanged");
        return missing > 0;
    }

    // Déclenche un scan Kavita du dossier de la série (si KavitaPath configuré sur la library) ou
    // de la library Kavita entière en repli — no-op silencieux (juste une trace) si la library
    // Inkhound n'est rattachée à aucune library Kavita (KavitaLibraryId == 0 et KavitaPath vide).
    private async Task TriggerKavitaScanAsync(Guid volumeId)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync(volumeId);
        if (volume is null) { JobSendTrace("[Kavita] Volume not found", ETraceLevel.ERROR); return; }

        var library = await ctx.Libraries.FindAsync(volume.LibraryId);
        if (library is null) { JobSendTrace("[Kavita] Library not found", ETraceLevel.ERROR); return; }

        if (!string.IsNullOrEmpty(library.KavitaPath))
        {
            var kavita = GetService<KavitaService, KavitaOptions>();
            var kavitaFolderPath = library.KavitaPath.TrimEnd('/', '\\') + "/" + ArchiveService.GetPath(volume);
            JobSendTrace($"[Kavita] Triggering folder scan: {kavitaFolderPath}");
            await kavita.ScanFolderAsync(kavitaFolderPath);
        }
        else if (library.KavitaLibraryId > 0)
        {
            JobSendTrace("[Kavita] No KavitaPath configured — falling back to full library scan");
            await ScanKavitaLibraryAsync(library.KavitaLibraryId);
        }
        else
        {
            JobSendTrace("[Kavita] No Kavita library linked to this library — skipping scan", ETraceLevel.WARNING);
        }
    }

    public async Task<bool> UpdateVolumeAgeRatingAsync(Guid volumeId, AgeRating ageRating, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync([volumeId], ct);
        if (volume is null) return false;
        volume.AgeRating = ageRating;
        volume.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volumeId));
        return true;
    }

    // Bascule manuelle MONITORED ⇄ PAUSED d'un volume incomplet. Refusé (InvalidOperationException →
    // 409) sur un volume COMPLETED : la pause n'a de sens que pour stopper les recherches d'issues
    // manquantes, et un volume complet n'en a aucune. Passer à MONITORED repasse par
    // RecalculateVolumeStatisticsAsync, qui décide seul entre MONITORED et COMPLETED (un volume mis
    // en pause puis complété à la main serait sinon marqué MONITORED à tort). Retourne null si
    // le volume n'existe pas.
    public async Task<Volume?> UpdateVolumeStatusAsync(Guid volumeId, VolumeStatus status, CancellationToken ct = default)
    {
        if (status is not (VolumeStatus.MONITORED or VolumeStatus.PAUSED))
            throw new ArgumentException("Status must be MONITORED or PAUSED.", nameof(status));

        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync([volumeId], ct);
        if (volume is null) return null;
        if (volume.Status == VolumeStatus.COMPLETED)
            throw new InvalidOperationException("A completed volume cannot be paused or monitored.");

        volume.Status = status;
        volume.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct);

        // RecalculateVolumeStatisticsAsync ne touche pas au Status d'un volume PAUSED ; il tranche
        // MONITORED/COMPLETED pour les autres et émet OnDataUpdated.
        await RecalculateVolumeStatisticsAsync(ctx, volumeId, ct);
        return await ctx.Volumes.FindAsync([volumeId], ct);
    }

    // Bascule en masse des volumes d'une library : PAUSED → tous les MONITORED (incomplets) passent
    // en pause ; MONITORED → tous les PAUSED reprennent. Les COMPLETED ne sont jamais touchés. Un
    // volume PAUSED remis en MONITORED est, par construction, incomplet (sa complétion en pause aurait
    // été ignorée par RecalculateVolumeStatisticsAsync) — on recalcule donc ses stats pour trancher
    // MONITORED/COMPLETED, comme UpdateVolumeStatusAsync. Retourne le nombre de volumes modifiés, ou
    // null si la library n'existe pas. Un seul OnDataUpdated(Library) est émis (pas un par volume :
    // la page Library recharge sa liste à la réponse HTTP).
    public async Task<int?> UpdateLibraryVolumesStatusAsync(Guid libraryId, VolumeStatus status, CancellationToken ct = default)
    {
        if (status is not (VolumeStatus.MONITORED or VolumeStatus.PAUSED))
            throw new ArgumentException("Status must be MONITORED or PAUSED.", nameof(status));

        var ctx = GetDb();
        if (!await ctx.Libraries.AnyAsync(l => l.Id == libraryId, ct)) return null;

        var from = status == VolumeStatus.PAUSED ? VolumeStatus.MONITORED : VolumeStatus.PAUSED;
        var volumeIds = await ctx.Volumes
            .Where(v => v.LibraryId == libraryId && v.Status == from)
            .Select(v => v.Id)
            .ToListAsync(ct);

        if (volumeIds.Count == 0) return 0;

        var now = DateTime.UtcNow;
        await ctx.Volumes
            .Where(v => volumeIds.Contains(v.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.Status, status)
                .SetProperty(v => v.UpdatedAt, now), ct);

        if (status == VolumeStatus.MONITORED)
        {
            foreach (var id in volumeIds)
                await RecalculateVolumeStatisticsAsync(ctx, id, ct);
        }

        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Library>(libraryId));
        return volumeIds.Count;
    }

    public async Task<bool> UpdateIssueManuallyAsync(
        Guid issueId, string? title, int? year, string? description, IssueStatus status, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var issue = await ctx.Issues.FindAsync([issueId], ct);
        if (issue is null) return false;

        issue.Title       = title;
        issue.Year        = year;
        issue.Description = description;
        issue.Status      = status;

        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issueId));
        return true;
    }

    // Supprime le fichier CBZ de la librairie et remet l'issue à MISSING : efface aussi les résultats
    // d'analyse (ils décrivent un fichier disparu) et les lignes IssueDownload de l'issue (le torrent
    // qBittorrent n'est PAS touché). Opération courte → pas un job (cf. DeleteDownloadAsync).
    public async Task<(bool Success, string? Error)> DeleteIssueFileAsync(Guid issueId, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var issue = await ctx.Issues.FindAsync([issueId], ct);
        if (issue is null) return (false, "Issue not found.");
        if (string.IsNullOrEmpty(issue.CbzFilename)) return (false, "This issue has no CBZ file.");

        var volume = await ctx.Volumes.FindAsync([issue.VolumeId], ct);
        if (volume is null) return (false, "Volume not found.");
        var library = await ctx.Libraries.FindAsync([volume.LibraryId], ct);
        if (library is null) return (false, "Library not found.");

        // Le fichier sur disque peut porter le nom calculé depuis les métadonnées actuelles OU le nom
        // enregistré à l'import (dérive possible si les métadonnées ont changé sans regen). On sonde
        // les deux ; un fichier déjà absent n'est pas une erreur.
        var candidates = new[]
        {
            ArchiveService.GetPath(issue, volume, library),
            Path.Combine(ArchiveService.GetPath(volume, library), issue.CbzFilename)
        };
        foreach (var path in candidates.Distinct())
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (false, $"Could not delete the file: {ex.Message}");
            }
        }

        ClearIssueDownloadState(issue);
        ctx.IssueDownloads.RemoveRange(ctx.IssueDownloads.Where(d => d.IssueId == issueId));

        await ctx.SaveChangesAsync(ct);
        await RecalculateVolumeStatisticsAsync(ctx, volume.Id, ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issue.Id));

        await TriggerKavitaScanAsync(volume.Id);
        return (true, null);
    }

    // Remet une issue à l'état "non téléchargée" : statut MISSING + purge fichier/taille/date +
    // tous les champs d'analyse CBZ (ils décrivent un fichier disparu). Ne sauve PAS, ne supprime
    // NI le fichier disque NI les lignes IssueDownload — à la charge de l'appelant selon le
    // contexte (DeleteIssueFileAsync : fichier + IssueDownload ; CheckVolumeFilesAsync : IssueDownload).
    private static void ClearIssueDownloadState(Issue issue)
    {
        issue.CbzFilename = null;
        issue.FileSizeBytes = 0;
        issue.Status = IssueStatus.MISSING;
        issue.DownloadedAt = default;
        issue.AnalysisScore = null;
        issue.AnalysisScoreBand = null;
        issue.AnalysisDominantImageFormat = null;
        issue.AnalysisDominantResolutionWidth = null;
        issue.AnalysisDominantResolutionHeight = null;
        issue.AnalysisPageCount = null;
        issue.AnalysisHasComicInfo = null;
        issue.AnalysisZipCompressionPercent = null;
        issue.AnalysisFileSizeBytes = null;
        issue.AnalysisAveragePageSizeBytes = null;
        issue.AnalysisFileHash = null;
        issue.AnalyzedAt = null;
    }

    // Liste les fichiers d'archive d'un dossier avec le numéro d'issue déduit de leur nom — alimente
    // la popup de revue fichiers ↔ issues avant l'import. Lecture rapide, pas un job.
    public async Task<List<ImportScanFile>> ScanImportDirectoryAsync(Guid volumeId, string directory, CancellationToken ct = default)
    {
        var ctx = GetDb();
        _ = await ctx.Volumes.FindAsync([volumeId], ct)
            ?? throw new KeyNotFoundException($"Volume {volumeId} not found.");

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        return _archiveExtensions
            .SelectMany(ext => Directory.GetFiles(directory, ext))
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.Name)
            .Select(f => new ImportScanFile(f.Name, f.Length, SourceAnalyzer.ParseIssueNumber(f.Name)))
            .ToList();
    }

    // Import des archives d'un dossier vers un volume. parameters.FileIssueMap (nom de fichier →
    // IssueId) = appariement explicite issu de la popup de revue ; quand null, appariement
    // automatique par numéro de tome. Retourne le JobContext pour que le controller renvoie le jobId.
    public JobContext LaunchJobImportDirectory(ImportDirectoryJobParameters parameters)
    {
        var ctx = GetDb();
        var volume = ctx.Volumes.Find(parameters.VolumeId);
        var jobTitle = volume is not null ? $"Import — {volume.Title}" : $"Import — {parameters.Directory}";

        var job = StartJob(jobTitle, parameters);
        if (job.State != JobState.ERROR)
        {
            job.SetState(JobState.RUNNING);
            _ = RunImportDirectoryJobAsync(job, parameters);
        }
        return job;
    }

    private async Task RunImportDirectoryJobAsync(JobContext job, ImportDirectoryJobParameters parameters)
    {
        DirectoryInfo? tempDir = null;
        try
        {
            var ctx = GetDb();
            var volume = await ctx.Volumes.FindAsync(parameters.VolumeId)
                ?? throw new KeyNotFoundException($"Volume {parameters.VolumeId} not found.");
            var library = await ctx.Libraries.FindAsync(volume.LibraryId)
                ?? throw new KeyNotFoundException($"Library {volume.LibraryId} not found.");
            var issues = await ctx.Issues.Where(i => i.VolumeId == parameters.VolumeId).ToListAsync();

            var archiveService = GetService<ArchiveService, ArchiveOption>();
            if (archiveService.CurrentState.State != EState.OK)
                throw new InvalidOperationException("ArchiveService is not available");

            tempDir = archiveService.GenerateTempDirectory();

            var files = _archiveExtensions
                .SelectMany(ext => Directory.GetFiles(parameters.Directory, ext))
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.Name)
                .ToList();

            job.AddTotal(files.Count);
            JobSendTrace($"[Import] {files.Count} archive file(s) in {parameters.Directory}");

            var anyImported = false;
            foreach (var file in files)
            {
                Issue? issue;
                if (parameters.FileIssueMap is { } map)
                {
                    if (!map.TryGetValue(file.Name, out var issueId)) continue; // non retenu dans la revue
                    issue = issues.FirstOrDefault(i => i.Id == issueId);
                    if (issue is null)
                    {
                        JobSendTrace($"[Import] {file.Name}: issue {issueId} not found — skipped", ETraceLevel.WARNING);
                        job.Progress.Increment(false);
                        job.CallbackHandler.Callback(job.Progress);
                        continue;
                    }
                    // Fichier explicitement mappé → importé même si l'issue est déjà DOWNLOADED (ré-import).
                }
                else
                {
                    // Auto-appariement par numéro : réservé aux issues Standard (un "#3" dans un nom de
                    // fichier désigne le tome 3, jamais un hors-série / omnibus portant le même numéro).
                    var number = SourceAnalyzer.ParseIssueNumber(file.Name);
                    if (number is null) continue;
                    issue = issues.FirstOrDefault(i => i.Category == IssueCategory.Standard && i.IssueNumber == number);
                    if (issue is null) continue;
                    if (issue.Status == IssueStatus.DOWNLOADED && !parameters.OverrideExisting)
                    {
                        JobSendTrace($"[Import] Skipping {file.Name} (issue #{issue.IssueNumber} already downloaded)");
                        job.Progress.Increment(true);
                        job.CallbackHandler.Callback(job.Progress);
                        continue;
                    }
                }

                var ok = await ImportArchiveFileForIssueAsync(
                    ctx, archiveService, file, issue, volume, library, tempDir.FullName, job.CallbackHandler);
                if (ok) anyImported = true;
                job.Progress.Increment(ok);
                job.CallbackHandler.Callback(job.Progress);
            }

            if (anyImported && !string.IsNullOrEmpty(library.KavitaPath))
            {
                var kavita = GetService<KavitaService, KavitaOptions>();
                var kavitaFolderPath = library.KavitaPath.TrimEnd('/', '\\') + "/" + ArchiveService.GetPath(volume);
                await JobRunTimedAsync(
                    $"[Import] Triggering Kavita folder scan: {kavitaFolderPath}",
                    () => kavita.ScanFolderAsync(kavitaFolderPath));
            }

            EndJob(job.Progress.Error == 0);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Import] Unhandled error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
        finally
        {
            if (tempDir?.Exists == true)
                tempDir.Delete(recursive: true);
        }
    }

    #endregion

    #region Issue Actions
    // Point d'entrée autonome (démarre son propre job). À ne PAS appeler depuis une méthode qui gère
    // déjà un job en cours : StartJob/EndJob repose sur un AsyncLocal partagé, donc un appel imbriqué
    // remplace le job "courant" pendant l'exécution puis le remet à null à la fin (EndJob), ce qui fait
    // disparaître silencieusement toutes les traces émises ensuite par l'appelant (JobId vide).
    // Depuis une boucle déjà pilotée par un job, appeler ImportArchiveAsync(...) directement à la place.
    public async Task<FileInfo?> LaunchJobImportArchive(ArchiveConverterPdfJobParameters parameters)
    {
        var sourcefile = File.Exists(parameters.SourceFile) ? new FileInfo(parameters.SourceFile) : null;
        if (sourcefile == null)
        {
            return null;
        }

        var job = StartJob($"Transform {sourcefile.Name} to archive", parameters);
        job.SetState(JobState.RUNNING);

        try
        {
            var archive = await ImportArchiveAsync(parameters, job.CallbackHandler);
            EndJob(archive != null && job.Progress.Error < job.Progress.Total);
            return archive;
        }
        catch (Exception ex)
        {
            JobSendTrace($"Unhandled error during import: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
            return null;
        }
    }

    // Logique de conversion pure, sans gestion de job — pour être appelée en toute sécurité depuis
    // une boucle déjà pilotée par un job appelant (ImportArchiveFromDirectoryAsync, LaunchJobProcessDownloads).
    // Les traces émises ici restent attachées au job de l'appelant (AsyncLocal non modifié).
    private async Task<FileInfo?> ImportArchiveAsync(ArchiveConverterPdfJobParameters parameters, ProgressionCallback? progression = null)
    {
        var archiveService = GetService<ArchiveService, ArchiveOption>();

        if (archiveService.CurrentState.State != EState.OK)
        {
            throw new InvalidOperationException("ArchiveService is not available");
        }

        var sourcefile = File.Exists(parameters.SourceFile) ? new FileInfo(parameters.SourceFile) : null;
        if (sourcefile == null)
        {
            return null;
        }

        // STEP 1 : Get pages from file
        var pageFiles = await archiveService.ConvertToImages(sourcefile, parameters.WorkingPath, progression);
        if (pageFiles == null)
        {
            return null;
        }

        // STEP 2 : Add ComicsInfo
        var comicsInfo = await archiveService.CreateComicInfo(parameters.Volume, parameters.Issue, parameters.WorkingPath, progression);

        // STEP 3 : Generage CBZ
        var archive = await archiveService.CreateCbzFile(parameters.WorkingPath, parameters.Volume, parameters.Issue, comicsInfo, pageFiles, progression);

        // STEP 4 : Delete files
        comicsInfo.Delete();
        pageFiles.ForEach(c => c.Delete());

        return archive;
    }

    // Convertit sourceFile en CBZ normalisé pour l'issue, le copie dans le dossier du volume et met
    // l'issue à jour (DOWNLOADED / CbzFilename / FileSizeBytes / DownloadedAt) + recalcul des stats.
    // workingRoot = dossier temporaire du job appelant ; false si la conversion échoue.
    // L'appelant garde sa propre progression et le scan Kavita de fin de job.
    private async Task<bool> ImportArchiveFileForIssueAsync(
        DbStorageContext ctx, ArchiveService archiveService,
        FileInfo sourceFile, Issue issue, Volume volume, Library library,
        string workingRoot, ProgressionCallback? progression)
    {
        JobSendTrace($"[Import] {sourceFile.Name} → issue #{issue.IssueNumber}");

        // L'issue peut être en cours de téléchargement : l'import la satisfait, le suivi du download
        // n'a plus lieu d'être (voir la purge après conversion réussie, plus bas).
        var wasDownloading = issue.Status == IssueStatus.DOWNLOADING;

        var archive = await ImportArchiveAsync(new ArchiveConverterPdfJobParameters
        {
            SourceFile = sourceFile.FullName,
            WorkingPath = Path.Combine(workingRoot, issue.Id.ToString("N")),
            Library = library,
            Volume = volume,
            Issue = issue
        }, progression);

        if (archive is null)
        {
            JobSendTrace($"[Import] Conversion failed for {sourceFile.Name}", ETraceLevel.ERROR);
            return false;
        }

        var volumeDir = archiveService.CreateVolumeDirectory(volume, library);
        var dest = Path.Combine(volumeDir.FullName, archive.Name);
        File.Copy(archive.FullName, dest, overwrite: true);
        archiveService.EnsurePermissiveFileMode(dest);

        issue.CbzFilename = archive.Name;
        issue.FileSizeBytes = (int)archive.Length;
        issue.Status = IssueStatus.DOWNLOADED;
        issue.DownloadedAt = DateTime.UtcNow;

        // Issue servie par un import alors qu'un téléchargement était en cours → on abandonne le suivi
        // de CE couple (issue, download) uniquement : le torrent reste dans qBittorrent avec ses
        // fichiers, les lignes jumelles des autres issues du même torrent (PACK) sont conservées et
        // aucun ban n'est créé (le torrent n'a pas démérité, on n'en a simplement plus besoin ici).
        if (wasDownloading)
        {
            var tracked = await ctx.IssueDownloads.Where(d => d.IssueId == issue.Id).ToListAsync();
            if (tracked.Count > 0)
            {
                ctx.IssueDownloads.RemoveRange(tracked);
                JobSendTrace($"[Import] Issue #{issue.IssueNumber} was downloading — {tracked.Count} download tracking row(s) dropped (torrent and files kept)");
            }
        }
        // Persiste d'abord le statut de l'issue : le recalcul du compteur ci-dessous requête la base
        // et ne verrait pas cette issue si elle n'était pas encore sauvegardée.
        await ctx.SaveChangesAsync();
        await RecalculateVolumeStatisticsAsync(ctx, volume.Id);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issue.Id));
        return true;
    }

    // Import d'un fichier local unique comme CBZ d'une issue précise (bouton "Import" de la page
    // Issue). Retourne le JobContext pour que le controller renvoie le jobId.
    public async Task<JobContext> LaunchJobImportIssueFile(ImportIssueFileJobParameters parameters)
    {
        var ctx = GetDb();
        var issue = await ctx.Issues.FindAsync(parameters.IssueId);
        var jobTitle = issue is not null
            ? $"Import — Issue #{issue.IssueNumber}"
            : $"Import — {parameters.IssueId}";

        var job = StartJob(jobTitle, parameters);
        if (job.State != JobState.ERROR)
        {
            job.SetState(JobState.RUNNING);
            _ = RunImportIssueFileJobAsync(job, parameters);
        }
        return job;
    }

    private async Task RunImportIssueFileJobAsync(JobContext job, ImportIssueFileJobParameters parameters)
    {
        DirectoryInfo? tempDir = null;
        try
        {
            var ctx = GetDb();
            var issue = await ctx.Issues.FindAsync(parameters.IssueId);
            if (issue is null) { JobSendTrace($"[Import] Issue {parameters.IssueId} not found", ETraceLevel.ERROR); EndJob(false); return; }
            var volume = await ctx.Volumes.FindAsync(issue.VolumeId);
            if (volume is null) { JobSendTrace($"[Import] Volume {issue.VolumeId} not found", ETraceLevel.ERROR); EndJob(false); return; }
            var library = await ctx.Libraries.FindAsync(volume.LibraryId);
            if (library is null) { JobSendTrace($"[Import] Library {volume.LibraryId} not found", ETraceLevel.ERROR); EndJob(false); return; }

            if (!File.Exists(parameters.FilePath))
            {
                JobSendTrace($"[Import] File not found: {parameters.FilePath}", ETraceLevel.ERROR);
                EndJob(false);
                return;
            }
            if (!IsArchiveFile(parameters.FilePath))
            {
                JobSendTrace($"[Import] Unsupported format (.cbz/.cbr/.pdf only): {parameters.FilePath}", ETraceLevel.ERROR);
                EndJob(false);
                return;
            }

            var archiveService = GetService<ArchiveService, ArchiveOption>();
            if (archiveService.CurrentState.State != EState.OK)
                throw new InvalidOperationException("ArchiveService is not available");

            tempDir = archiveService.GenerateTempDirectory();
            job.AddTotal(1);

            var ok = await ImportArchiveFileForIssueAsync(
                ctx, archiveService, new FileInfo(parameters.FilePath), issue, volume, library, tempDir.FullName, job.CallbackHandler);
            job.Progress.Increment(ok);
            job.CallbackHandler.Callback(job.Progress);

            if (ok && !string.IsNullOrEmpty(library.KavitaPath))
            {
                var kavita = GetService<KavitaService, KavitaOptions>();
                var kavitaFolderPath = library.KavitaPath.TrimEnd('/', '\\') + "/" + ArchiveService.GetPath(volume);
                await JobRunTimedAsync(
                    $"[Import] Triggering Kavita folder scan: {kavitaFolderPath}",
                    () => kavita.ScanFolderAsync(kavitaFolderPath));
            }

            EndJob(ok);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Import] Unhandled error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
        finally
        {
            if (tempDir?.Exists == true)
                tempDir.Delete(recursive: true);
        }
    }

    private static readonly string[] _archiveExtensions = ["*.cbz", "*.cbr", "*.pdf"];

    #endregion

    #region Prowlarr

    public async Task<List<ProwlarrIndexer>> GetProwlarrIndexersAsync(CancellationToken ct = default)
    {
        var prowlarr = GetService<ProwlarrService, ProwlarrOptions>();
        if (prowlarr.CurrentState.State != EState.OK) return [];
        return await prowlarr.GetIndexersAsync(ct);
    }

    public async Task<List<SelectedIndexer>> GetSelectedIndexersAsync(Guid libraryId)
    {
        return await GetDb().SelectedIndexers.Where(si => si.LibraryId == libraryId).ToListAsync();
    }

    public async Task SetSelectedIndexersAsync(
        Guid libraryId, List<(ProwlarrIndexer Indexer, List<int> CategoryIds)> items)
    {
        var ctx = GetDb();
        ctx.SelectedIndexers.RemoveRange(ctx.SelectedIndexers.Where(si => si.LibraryId == libraryId));
        foreach (var (indexer, categoryIds) in items)
        {
            ctx.SelectedIndexers.Add(new SelectedIndexer
            {
                LibraryId      = libraryId,
                IndexerId      = indexer.Id,
                Name           = indexer.Name,
                Protocol       = indexer.Protocol,
                AddedAt        = DateTime.UtcNow,
                CategoriesJson = JsonSerializer.Serialize(categoryIds)
            });
        }
        await ctx.SaveChangesAsync();
    }

    // Résultats de recherche Prowlarr en attente de récupération par le contrôleur, indexés par
    // JobId — même raison que _searchResults (recherche multi-source) : un Job ne peut pas
    // porter de valeur de retour vers l'appelant HTTP d'origine (fire-and-forget).
    // Indexers à interroger : paramètre explicite s'il est fourni, sinon la sélection persistée pour
    // la library du volume (null = tous les indexers). Retourne aussi la sélection persistée, dont
    // les catégories servent à ComputeCategories.
    private static async Task<(int[]? IndexerIds, List<SelectedIndexer> Saved)> ResolveIndexersAsync(
        DbStorageContext ctx, Volume volume, int[]? explicitIds)
    {
        var saved = await ctx.SelectedIndexers.Where(si => si.LibraryId == volume.LibraryId).ToListAsync();
        var indexerIds = explicitIds;
        if (indexerIds is null or { Length: 0 })
            indexerIds = saved.Count > 0 ? [.. saved.Select(s => s.IndexerId)] : null;
        return (indexerIds, saved);
    }

    // Exécute une cascade de requêtes Prowlarr et fusionne les résultats. Toutes les requêtes sont
    // tentées, sans condition d'arrêt anticipé : les requêtes précises ne garantissent pas des
    // résultats pertinents (matching plein texte assez large côté indexers), donc même si une requête
    // ramène déjà des résultats, les niveaux suivants (jusqu'au titre du volume seul, qui remonte le
    // mieux les candidats PACK/omnibus) sont toujours tentés. Un même indexer peut renvoyer plusieurs
    // fois le même torrent d'une requête à l'autre — le Guid Prowlarr n'étant pas garanti stable d'un
    // appel à l'autre pour une même release, la déduplication se fait sur une empreinte de contenu
    // (indexeur + titre + taille) plutôt que sur le Guid.
    // queryCache (optionnel) : mémoire par job des réponses brutes par requête — évite de réinterroger
    // Prowlarr quand plusieurs cascades du même job partagent une requête (ex. "{titre}" seul,
    // commun à la cascade volume et à chaque cascade issue de l'auto search).
    // Chaque requête incrémente la progression du job (le total doit avoir été déclaré par l'appelant).
    private async Task<List<ProwlarrSearchResult>> SearchProwlarrCascadeAsync(
        JobContext job,
        ProwlarrService prowlarr,
        List<string> queries,
        int[]? indexerIds,
        List<SelectedIndexer> saved,
        Dictionary<string, List<ProwlarrSearchResult>>? queryCache = null)
    {
        var seen = new HashSet<(int IndexerId, string TitleKey, long Size)>();
        List<ProwlarrSearchResult> merged = [];

        foreach (var query in queries)
        {
            List<ProwlarrSearchResult> raw;
            if (queryCache is not null && queryCache.TryGetValue(query, out var cached))
            {
                JobSendTrace($"[Prowlarr] Reusing results of: {query}");
                raw = cached;
            }
            else
            {
                JobSendTrace($"[Prowlarr] Searching: {query}");
                raw = await prowlarr.SearchAsync(query, indexerIds, ComputeCategories(saved, indexerIds), default);
                queryCache?.Add(query, raw);
            }

            job.Progress.Increment(true);
            job.CallbackHandler.Callback(job.Progress);

            var added = 0;
            foreach (var r in raw)
            {
                var key = (r.IndexerId, r.Title.Trim().ToLowerInvariant(), r.Size);
                if (seen.Add(key))
                {
                    merged.Add(r);
                    added++;
                }
            }

            JobSendTrace(added > 0
                ? $"[Prowlarr] {added} new result(s) with \"{query}\" ({merged.Count} total)"
                : $"[Prowlarr] No new results with \"{query}\"");
        }

        return merged;
    }

    // Les plus lourds des trois caches de résultats : des centaines de résultats d'indexer par
    // entrée. Bornés comme _searchResults — voir le commentaire là-bas.
    private readonly ExpiringCache<Guid, List<ScoredSearchResultTorrent>> _prowlarrResults =
        new("Prowlarr issue results", JobResultRetention, JobResultMaxEntries);

    public List<ScoredSearchResultTorrent>? GetProwlarrSearchJobResult(Guid jobId)
        => _prowlarrResults.TryGet(jobId, out var result) ? result : null;

    // Lance la recherche Prowlarr en tâche de fond et retourne immédiatement le JobContext (donc
    // son JobId) pour que le frontend puisse s'abonner à sa progression/ses traces via SignalR
    // sans attendre la fin de la recherche — miroir de LaunchJobSearchVolumes.
    public async Task<JobContext> LaunchJobSearchMissingIssue(ProwlarrSearchJobParameters parameters)
    {
        var ctx = GetDb();
        var issue = await ctx.Issues.FindAsync(parameters.IssueId);
        var volume = issue is not null ? await ctx.Volumes.FindAsync(issue.VolumeId) : null;
        var jobTitle = issue is not null
            ? $"Recherche Prowlarr — {volume?.Title ?? "?"} #{issue.IssueNumber}"
            : $"Recherche Prowlarr — {parameters.IssueId}";

        var job = StartJob(jobTitle, parameters);
        if (job.State != JobState.ERROR)
        {
            job.SetState(JobState.RUNNING);
            _ = RunSearchMissingIssueJobAsync(job, parameters);
        }
        return job;
    }

    private async Task RunSearchMissingIssueJobAsync(JobContext job, ProwlarrSearchJobParameters parameters)
    {
        var ctx = GetDb();
        try
        {
            var issue = await ctx.Issues.FindAsync(parameters.IssueId);
            if (issue is null)
            {
                EndJob(false);
                return;
            }

            var volume = await ctx.Volumes.FindAsync(issue.VolumeId);
            if (volume is null)
            {
                EndJob(false);
                return;
            }

            var prowlarr = GetService<ProwlarrService, ProwlarrOptions>();
            if (prowlarr.CurrentState.State != EState.OK)
            {
                JobSendTrace("[Prowlarr] Service unavailable", ETraceLevel.ERROR);
                EndJob(false);
                return;
            }

            var (indexerIds, saved) = await ResolveIndexersAsync(ctx, volume, parameters.IndexerIds);

            var queries = BuildSearchQueries(volume, issue);
            job.CallbackHandler.UpdateTotal(queries.Count);

            JobSendTrace($"[Prowlarr] {queries.Count} search quer{(queries.Count == 1 ? "y" : "ies")} planned for \"{volume.Title} #{issue.IssueNumber}\": {string.Join(" | ", queries)}");

            var merged = await SearchProwlarrCascadeAsync(job, prowlarr, queries, indexerIds, saved);

            var bans = await LoadBanIndexForIssueAsync(issue.Id);
            var results = ScoringTorrent.ScoreAndSort(volume, issue, merged, bans);

            if (results.Count == 0)
                JobSendTrace("[Prowlarr] No results across all queries", ETraceLevel.WARNING);
            else
                JobSendTrace($"[Prowlarr] {results.Count} result(s) scored and sorted across {queries.Count} quer{(queries.Count == 1 ? "y" : "ies")} attempted");

            var bannedCount = results.Count(r => r.Banned);
            if (bannedCount > 0)
                JobSendTrace($"[Prowlarr] {bannedCount} result(s) banned for this issue — scored 0", ETraceLevel.WARNING);

            _prowlarrResults.Set(job.JobId, results);
            EndJob(true);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Prowlarr] Unexpected error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
    }

    // Résultats de recherche Prowlarr au niveau Volume — même raison que _prowlarrResults (fire-and-forget).
    private readonly ExpiringCache<Guid, List<ScoredSearchResultVolumePack>> _prowlarrVolumeResults =
        new("Prowlarr volume results", JobResultRetention, JobResultMaxEntries);

    public List<ScoredSearchResultVolumePack>? GetProwlarrVolumeSearchJobResult(Guid jobId)
        => _prowlarrVolumeResults.TryGet(jobId, out var result) ? result : null;

    // Lance la recherche Prowlarr au niveau d'un Volume entier (toutes ses issues MISSING, pas une
    // issue précise) — miroir de LaunchJobSearchMissingIssue.
    public async Task<JobContext> LaunchJobSearchMissingVolume(ProwlarrVolumeSearchJobParameters parameters)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync(parameters.VolumeId);
        var jobTitle = volume is not null
            ? $"Recherche Prowlarr — {volume.Title} (volume complet)"
            : $"Recherche Prowlarr — {parameters.VolumeId}";

        var job = StartJob(jobTitle, parameters);
        if (job.State != JobState.ERROR)
        {
            job.SetState(JobState.RUNNING);
            _ = RunSearchMissingVolumeJobAsync(job, parameters);
        }
        return job;
    }

    private async Task RunSearchMissingVolumeJobAsync(JobContext job, ProwlarrVolumeSearchJobParameters parameters)
    {
        var ctx = GetDb();
        try
        {
            var volume = await ctx.Volumes.FindAsync(parameters.VolumeId);
            if (volume is null)
            {
                EndJob(false);
                return;
            }

            var missingIssues = await ctx.Issues
                .Where(i => i.VolumeId == volume.Id && i.Status == IssueStatus.MISSING)
                .ToListAsync();

            if (missingIssues.Count == 0)
            {
                JobSendTrace("[Prowlarr] No missing issues for this volume — nothing to search for", ETraceLevel.WARNING);
                _prowlarrVolumeResults.Set(job.JobId, []);
                EndJob(true);
                return;
            }

            var prowlarr = GetService<ProwlarrService, ProwlarrOptions>();
            if (prowlarr.CurrentState.State != EState.OK)
            {
                JobSendTrace("[Prowlarr] Service unavailable", ETraceLevel.ERROR);
                EndJob(false);
                return;
            }

            var (indexerIds, saved) = await ResolveIndexersAsync(ctx, volume, parameters.IndexerIds);

            var queries = BuildSearchQueries(volume);
            job.CallbackHandler.UpdateTotal(queries.Count);

            JobSendTrace($"[Prowlarr] {queries.Count} search quer{(queries.Count == 1 ? "y" : "ies")} planned for \"{volume.Title}\" ({missingIssues.Count} missing issue(s)): {string.Join(" | ", queries)}");

            var merged = await SearchProwlarrCascadeAsync(job, prowlarr, queries, indexerIds, saved);

            var bans = await LoadBanIndexForVolumeAsync(volume.Id);
            var results = ScoringVolumePack.ScoreAndSort(volume, missingIssues, merged, bans);

            if (results.Count == 0)
                JobSendTrace("[Prowlarr] No results across all queries", ETraceLevel.WARNING);
            else
                JobSendTrace($"[Prowlarr] {results.Count} result(s) scored and sorted across {queries.Count} quer{(queries.Count == 1 ? "y" : "ies")} attempted");

            var bannedCount = results.Count(r => r.Banned);
            if (bannedCount > 0)
                JobSendTrace($"[Prowlarr] {bannedCount} result(s) banned for an issue of this volume — scored 0", ETraceLevel.WARNING);

            _prowlarrVolumeResults.Set(job.JobId, results);
            EndJob(true);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Prowlarr] Unexpected error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
    }

    // Lance l'analyse CBZ en tâche de fond et retourne immédiatement le JobContext (donc son
    // JobId) — miroir de LaunchJobSearchVolumes/LaunchJobSearchMissingIssue. Pas de cache de
    // résultat séparé : les champs d'analyse sont persistés sur l'Issue et OnDataUpdated est
    // émis, donc le frontend récupère le résultat via son abonnement existant aux mises à jour
    // de données plutôt que par un endpoint dédié.
    public async Task<JobContext> LaunchJobAnalyzeIssue(AnalyzeIssueJobParameters parameters)
    {
        var ctx = GetDb();
        var issue = await ctx.Issues.FindAsync(parameters.IssueId);
        var jobTitle = issue is not null
            ? $"Analyse CBZ — Issue #{issue.IssueNumber}"
            : $"Analyse CBZ — {parameters.IssueId}";

        var job = StartJob(jobTitle, parameters);
        if (job.State != JobState.ERROR)
        {
            job.SetState(JobState.RUNNING);
            _ = RunAnalyzeIssueJobAsync(job, parameters);
        }
        return job;
    }

    private async Task RunAnalyzeIssueJobAsync(JobContext job, AnalyzeIssueJobParameters parameters)
    {
        var ctx = GetDb();
        try
        {
            var issue = await ctx.Issues.FindAsync(parameters.IssueId);
            if (issue is null || string.IsNullOrEmpty(issue.CbzFilename))
            {
                JobSendTrace("[Analyze] Issue not found or missing CBZ file", ETraceLevel.ERROR);
                EndJob(false);
                return;
            }

            var volume = await ctx.Volumes.FindAsync(issue.VolumeId);
            if (volume is null)
            {
                JobSendTrace("[Analyze] Volume not found", ETraceLevel.ERROR);
                EndJob(false);
                return;
            }

            var library = await ctx.Libraries.FindAsync(volume.LibraryId);
            if (library is null)
            {
                JobSendTrace("[Analyze] Library not found", ETraceLevel.ERROR);
                EndJob(false);
                return;
            }

            var archiveService = GetService<ArchiveService, ArchiveOption>();
            var kavitaService = GetService<KavitaService, KavitaOptions>();
            var scoringSettings = kavitaService.BuildScoringSettings();

            var cbzPath = ArchiveService.GetPath(issue, volume, library);
            if (!File.Exists(cbzPath))
            {
                JobSendTrace($"[Analyze] CBZ file not found: {cbzPath}", ETraceLevel.ERROR);
                EndJob(false);
                return;
            }

            JobSendTrace($"[Analyze] Computing SHA-256 hash of {Path.GetFileName(cbzPath)}");
            var hash = await ArchiveService.ComputeFileHashAsync(cbzPath);

            var progress = new Progress<CbzAnalysisProgress>(p =>
            {
                job.CallbackHandler.UpdateTotal(p.TotalEntries);
                job.CallbackHandler.Callback(new Progression { Total = p.TotalEntries, Completed = p.EntriesProcessed });
            });

            JobSendTrace($"[Analyze] Analyzing {Path.GetFileName(cbzPath)}");
            var report = await AnalyzeIssueFileAsync(issue, cbzPath, archiveService, scoringSettings, hash, progress);

            await ctx.SaveChangesAsync();
            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issue.Id));

            JobSendTrace($"[Analyze] Score {report.Score}/100 ({report.ScoreBand}) — {issue.AnalysisPageCount} page(s)");
            EndJob(true);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Analyze] Unexpected error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
    }

    // Analyse un CBZ existant (score de compatibilité Kavita) et écrit tous les champs Analysis* +
    // AnalyzedAt sur l'issue. Ne SAUVE PAS — l'appelant groupe les SaveChanges. Retourne le rapport
    // (pour la trace). hash : passé par l'appelant s'il l'a déjà calculé (CheckVolumeFilesAsync le
    // calcule pour la comparaison à AnalysisFileHash), sinon calculé ici. progress : null hors du
    // job d'analyse dédié (CheckVolumeFilesAsync suit la progression au niveau issue).
    private static async Task<KavitaCompatibilityReport> AnalyzeIssueFileAsync(
        Issue issue, string cbzPath, ArchiveService archiveService, ScoringSettings scoringSettings,
        string? hash = null, IProgress<CbzAnalysisProgress>? progress = null, CancellationToken ct = default)
    {
        hash ??= await ArchiveService.ComputeFileHashAsync(cbzPath, ct);
        var analysis = await archiveService.AnalyzeCbzAsync(cbzPath, scoringSettings, progress, ct);
        var report = ArchiveService.ScoreCbz(analysis, scoringSettings);

        var dominant = analysis.Entries
            .Where(e => e.IsImage && e.Image is { DecodeSucceeded: true })
            .GroupBy(e => (e.Image!.WidthPx, e.Image!.HeightPx))
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        issue.AnalysisScore = report.Score;
        issue.AnalysisScoreBand = report.ScoreBand;
        issue.AnalysisDominantImageFormat = analysis.FormatBreakdown.FirstOrDefault()?.Format;
        issue.AnalysisDominantResolutionWidth = dominant?.Key.WidthPx;
        issue.AnalysisDominantResolutionHeight = dominant?.Key.HeightPx;
        issue.AnalysisPageCount = analysis.ImageEntryCount;
        issue.AnalysisHasComicInfo = analysis.HasComicInfoXml;
        issue.AnalysisZipCompressionPercent = Math.Round((1 - analysis.ZipCompressionRatio) * 100, 1);
        issue.AnalysisFileSizeBytes = analysis.FileSizeBytes;
        issue.AnalysisAveragePageSizeBytes = analysis.AverageImageBytes;
        issue.AnalysisFileHash = hash;
        issue.AnalyzedAt = DateTime.UtcNow;
        return report;
    }

    public async Task<bool> GrabSearchResultAsync(
        string guid,
        int indexerId,
        Guid issueId,
        int? downloadClientId = null,
        CancellationToken ct = default)
    {
        var prowlarr = GetService<ProwlarrService, ProwlarrOptions>();
        if (prowlarr.CurrentState.State != EState.OK) return false;

        var success = await prowlarr.GrabReleaseAsync(guid, indexerId, downloadClientId, ct);
        if (!success) return false;

        var ctx = GetDb();
        var issue = await ctx.Issues.FindAsync([issueId], ct);
        if (issue is not null)
        {
            issue.Status = IssueStatus.DOWNLOADING;
            await ctx.SaveChangesAsync(ct);
            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issueId));
        }

        return true;
    }

    public async Task<List<ProwlarrHistoryItem>> GetProwlarrHistoryAsync(
        int limit = 20,
        CancellationToken ct = default)
    {
        var prowlarr = GetService<ProwlarrService, ProwlarrOptions>();
        if (prowlarr.CurrentState.State != EState.OK) return [];
        return await prowlarr.GetHistoryAsync(limit, ct);
    }

    private static int[] ComputeCategories(List<SelectedIndexer> saved, int[]? activeIndexerIds)
    {
        var relevant = activeIndexerIds is { Length: > 0 }
            ? saved.Where(s => activeIndexerIds.Contains(s.IndexerId)).ToList()
            : saved;

        var cats = relevant
            .Where(s => !string.IsNullOrEmpty(s.CategoriesJson))
            .SelectMany(s => JsonSerializer.Deserialize<List<int>>(s.CategoriesJson) ?? [])
            .Distinct()
            .ToArray();

        return cats.Length > 0 ? cats : [7030];
    }

    #region QBittorrent

    public async Task<List<QBittorrentCategory>> GetQBittorrentCategoriesAsync(CancellationToken ct = default)
    {
        var qb = GetService<QBittorrentService, QBittorrentOptions>();
        if (qb.CurrentState.State != EState.OK) return [];
        return await qb.GetCategoriesAsync(ct);
    }

    public async Task<(bool Success, string? TorrentHash)> GrabToQBittorrentAsync(
        string downloadUrl,
        string title,
        string? trackerName,
        Guid issueId,
        CancellationToken ct = default)
    {
        var qb = GetService<QBittorrentService, QBittorrentOptions>();
        if (qb.CurrentState.State != EState.OK) return (false, null);

        var grabParams = qb.GetGrabParameters();

        var hash = await qb.AddTorrentAsync(downloadUrl, grabParams.Category, grabParams.SavePath, grabParams.AddPaused, ct);
        if (hash is null) return (false, null);

        var ctx = GetDb();
        var download = new IssueDownload
        {
            Id = Guid.NewGuid(),
            IssueId = issueId,
            TorrentHash = hash,
            TorrentTitle = title,
            DownloadUrl = downloadUrl,
            TrackerName = trackerName,
            Status = DownloadStatus.Unknown,
            AddedAt = DateTime.UtcNow
        };
        ctx.IssueDownloads.Add(download);

        var issue = await ctx.Issues.FindAsync([issueId], ct);
        if (issue is not null)
            issue.Status = IssueStatus.DOWNLOADING;

        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issueId));

        return (true, hash);
    }

    public async Task<(bool Success, string? TorrentHash, List<QBittorrentTorrentFile>? Files)> GrabPackSelectiveAsync(
        string downloadUrl,
        CancellationToken ct = default)
    {
        var qb = GetService<QBittorrentService, QBittorrentOptions>();
        if (qb.CurrentState.State != EState.OK) return (false, null, null);

        var grabParams = qb.GetGrabParameters();

        // On ajoute le torrent normalement (sans paused=true) pour un état stable,
        // puis on le met immédiatement en pause via l'API dédiée.
        // Passer paused=true à l'ajout est instable selon la version QBittorrent
        // et peut provoquer un état ERROR impossible à reprendre.
        var hash = await qb.AddTorrentAsync(downloadUrl, grabParams.Category, grabParams.SavePath, paused: false, ct);
        if (hash is null) return (false, null, null);

        var paused = await qb.PauseTorrentAsync(hash, ct);
        if (!paused)
            JobSendTrace($"GrabPackSelectiveAsync: could not pause torrent {hash} — it may keep downloading during review", ETraceLevel.WARNING);
        await Task.Delay(500, ct); // Laisse QBittorrent traiter la mise en pause

        // Attente que QBittorrent charge les métadonnées du torrent (jusqu'à 10 s). À chaque tour, on
        // vérifie que le torrent est bien à l'arrêt : si QBittorrent l'a (re)mis en téléchargement
        // (pause ratée / ignorée en course), on ré-émet l'ordre — la revue des fichiers doit se faire
        // torrent arrêté, aucune donnée ne doit être téléchargée avant validation de la sélection.
        List<QBittorrentTorrentFile> files = [];
        for (var attempt = 0; attempt < 10 && files.Count == 0; attempt++)
        {
            await Task.Delay(1000, ct);
            files = await qb.GetTorrentFilesAsync(hash, ct);

            var torrent = (await qb.GetTorrentsAsync([hash], ct)).FirstOrDefault();
            if (torrent is not null && IsActivelyDownloading(torrent.State))
                await qb.PauseTorrentAsync(hash, ct);
        }

        return (true, hash, files.Count > 0 ? files : null);
    }

    // État live du torrent d'un PACK pendant la revue des fichiers (polling frontend) : sert à
    // détecter une source indisponible (« stalled ») et à récupérer la liste de fichiers si les
    // métadonnées n'étaient pas encore prêtes au moment du grab.
    public async Task<PackFetchStatus?> GetPackFetchStatusAsync(string hash, CancellationToken ct = default)
    {
        var qb = GetService<QBittorrentService, QBittorrentOptions>();
        if (qb.CurrentState.State != EState.OK) return null;

        var torrent = (await qb.GetTorrentsAsync([hash], ct)).FirstOrDefault();
        var files = await qb.GetTorrentFilesAsync(hash, ct);

        return new PackFetchStatus(
            Found: torrent is not null,
            State: torrent?.State ?? "unknown",
            Progress: torrent?.Progress ?? 0,
            NumComplete: torrent?.NumComplete ?? -1,
            NumSeeds: torrent?.NumSeeds ?? -1,
            Dlspeed: torrent?.Dlspeed ?? 0,
            Eta: torrent?.Eta ?? 0,
            MetadataReady: files.Count > 0,
            Files: files);
    }

    // Annulation de la revue des fichiers d'un PACK : supprime le torrent (et ses fichiers déjà
    // téléchargés) de QBittorrent. Refusé si un IssueDownload référence déjà ce hash — dans ce cas
    // la sélection a été validée et la suppression passe par la page Downloads.
    public async Task<(bool Success, string? Error)> AbortPackSelectionAsync(string hash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash)) return (false, "Torrent hash is required.");

        var ctx = GetDb();
        var alreadyTracked = await ctx.IssueDownloads.AnyAsync(d => d.TorrentHash == hash, ct);
        if (alreadyTracked)
            return (false, "This torrent is already tracked as a download — remove it from the Downloads page.");

        var qb = GetService<QBittorrentService, QBittorrentOptions>();
        if (qb.CurrentState.State != EState.OK) return (false, "QBittorrent service unavailable.");

        var deleted = await qb.DeleteTorrentAsync(hash, deleteFiles: true, ct);
        return deleted ? (true, null) : (false, "Failed to remove the torrent from QBittorrent.");
    }

    // issueId (flux page Issue) ou volumeId (flux page Volume) — au moins l'un des deux doit être
    // fourni. fileIssueOverrides permet une assignation manuelle (index de fichier → IssueId) pour
    // les fichiers dont le numéro de tome n'a pas pu être extrait automatiquement.
    public async Task<bool> ApplyPackSelectionAsync(
        string torrentHash,
        string downloadUrl,
        string title,
        string? trackerName,
        Guid? issueId,
        Guid? volumeId,
        int[] selectedFileIndices,
        Dictionary<int, Guid>? fileIssueOverrides,
        CancellationToken ct = default)
    {
        var qb = GetService<QBittorrentService, QBittorrentOptions>();
        if (qb.CurrentState.State != EState.OK) return false;

        var allFiles = await qb.GetTorrentFilesAsync(torrentHash, ct);
        var allIndices = allFiles.Select(f => f.Index).ToHashSet();
        var unselectedIndices = allIndices.Except(selectedFileIndices).ToArray();

        if (unselectedIndices.Length > 0)
            await qb.SetFilePrioritiesAsync(torrentHash, unselectedIndices, 0, ct);

        if (selectedFileIndices.Length > 0)
            await qb.SetFilePrioritiesAsync(torrentHash, selectedFileIndices, 1, ct);

        await qb.ResumeTorrentAsync(torrentHash, ct);

        var ctx = GetDb();

        var triggerIssue = issueId is { } id ? await ctx.Issues.FindAsync([id], ct) : null;
        var resolvedVolumeId = volumeId ?? triggerIssue?.VolumeId;

        // Tenter de matcher les fichiers sélectionnés aux issues du volume (override manuel ou
        // numéro de tome détecté).
        var selectedFiles = allFiles.Where(f => selectedFileIndices.Contains(f.Index)).ToList();
        var matchedIds    = new HashSet<Guid>();

        if (resolvedVolumeId is { } targetVolumeId)
        {
            // Toutes les issues du volume : un override manuel peut cibler une issue déjà DOWNLOADED
            // (re-acquisition demandée). L'auto-appariement par numéro de tome, lui, reste réservé
            // aux issues MISSING de catégorie Standard.
            var volumeIssues = await ctx.Issues
                .Where(i => i.VolumeId == targetVolumeId)
                .ToListAsync(ct);

            foreach (var file in selectedFiles)
            {
                Issue? matched;

                if (fileIssueOverrides is not null && fileIssueOverrides.TryGetValue(file.Index, out var overrideId))
                {
                    // Override explicite : accepté sur MISSING et DOWNLOADED, refusé sur DOWNLOADING
                    // (déjà en cours d'acquisition) — garde défensif, le frontend l'empêche déjà.
                    matched = volumeIssues.FirstOrDefault(i =>
                        i.Id == overrideId && i.Status != IssueStatus.DOWNLOADING && !matchedIds.Contains(i.Id));
                }
                else
                {
                    matched = TorrentTypeAnalyzer.ExtractIssueNumber(file.Name) is { } number
                        ? volumeIssues.FirstOrDefault(i =>
                            i.Category == IssueCategory.Standard && i.IssueNumber == number
                            && i.Status == IssueStatus.MISSING && !matchedIds.Contains(i.Id))
                        : null;
                }

                if (matched is null) continue; // ni override valide ni numéro reconnu — fichier ignoré

                matched.Status = IssueStatus.DOWNLOADING;
                ctx.IssueDownloads.Add(new IssueDownload
                {
                    Id = Guid.NewGuid(), IssueId = matched.Id,
                    TorrentHash = torrentHash, TorrentTitle = title, DownloadUrl = downloadUrl, TrackerName = trackerName,
                    FileName = file.Name,
                    Status = DownloadStatus.Unknown, AddedAt = DateTime.UtcNow
                });
                matchedIds.Add(matched.Id);
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(matched.Id));
            }
        }

        // Fallback : si aucun fichier n'a pu être associé et qu'une issue déclencheuse existe
        // (flux page Issue), comportement standard — rattacher le tout à cette issue. Pas de
        // fallback équivalent pour le flux page Volume (aucune issue "probable" à privilégier).
        if (matchedIds.Count == 0 && issueId is { } fallbackIssueId)
        {
            var fallback = triggerIssue ?? await ctx.Issues.FindAsync([fallbackIssueId], ct);
            if (fallback is not null) fallback.Status = IssueStatus.DOWNLOADING;
            ctx.IssueDownloads.Add(new IssueDownload
            {
                Id = Guid.NewGuid(), IssueId = fallbackIssueId,
                TorrentHash = torrentHash, TorrentTitle = title, DownloadUrl = downloadUrl, TrackerName = trackerName,
                Status = DownloadStatus.Unknown, AddedAt = DateTime.UtcNow
            });
            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(fallbackIssueId));
        }

        await ctx.SaveChangesAsync(ct);

        return true;
    }

    public async Task<List<DownloadItemData>> GetDownloadsAsync(
        DownloadStatus? statusFilter = null,
        CancellationToken ct = default)
    {
        var ctx = GetDb();

        var query = ctx.IssueDownloads.AsQueryable();
        if (statusFilter.HasValue)
            query = query.Where(d => d.Status == statusFilter.Value);

        var downloads = await query.OrderByDescending(d => d.AddedAt).ToListAsync(ct);
        return await EnrichDownloadsAsync(ctx, downloads, ct);
    }

    // Pagination réelle (Skip/Take + CountAsync) pour l'affichage UI — contrairement à
    // GetDownloadsAsync (utilisée en interne par LaunchJobProcessDownloads sur tout
    // l'historique), on n'enrichit (appels QBittorrent live + persistance du statut) que les
    // items de la page demandée.
    public async Task<Page<DownloadItemData>> GetDownloadsPageAsync(
        List<DownloadStatus>? statusFilter, int page, int pageSize, CancellationToken ct = default)
    {
        var ctx = GetDb();

        var query = ctx.IssueDownloads.AsQueryable();
        if (statusFilter is { Count: > 0 })
            query = query.Where(d => statusFilter.Contains(d.Status));

        var totalItems = await query.CountAsync(ct);
        var pageItems = await query
            .OrderByDescending(d => d.AddedAt).ThenByDescending(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var enriched = await EnrichDownloadsAsync(ctx, pageItems, ct);
        return new Page<DownloadItemData>
        {
            Items = enriched,
            PageNumber = page,
            PageSize = pageSize,
            TotalItems = totalItems,
        };
    }

    // Statuts d'un download encore "en vie" : ceux dont l'état peut encore évoluer côté QBittorrent
    // (par opposition à Done, terminé et importé). Sert aux vues ciblées ci-dessous.
    private static readonly DownloadStatus[] ActiveDownloadStatuses =
    [
        DownloadStatus.Downloading, DownloadStatus.Stalled, DownloadStatus.Paused,
        DownloadStatus.Finished, DownloadStatus.Syncing, DownloadStatus.Error,
        DownloadStatus.Unknown, DownloadStatus.NotFound
    ];

    /// <summary>
    /// Downloads d'une issue (carte « Download » de sa page détail), enrichis — donc avec un statut
    /// à jour. Plusieurs lignes possibles : un PACK peut avoir été relancé, ou plusieurs torrents
    /// visés successivement.
    /// </summary>
    public async Task<List<DownloadItemData>> GetIssueDownloadsAsync(Guid issueId, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var downloads = await ctx.IssueDownloads
            .Where(d => d.IssueId == issueId)
            .OrderByDescending(d => d.AddedAt)
            .ToListAsync(ct);
        return await EnrichDownloadsAsync(ctx, downloads, ct);
    }

    /// <summary>
    /// Downloads de toutes les issues d'un volume (badges de la liste des issues) — une seule
    /// requête QBittorrent groupée, jamais une par carte. <c>IssueDownload</c> n'a pas de
    /// <c>VolumeId</c> : la portée passe par une jointure sur <c>Issues</c>.
    /// </summary>
    public async Task<List<DownloadItemData>> GetVolumeDownloadsAsync(Guid volumeId, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var downloads = await (from d in ctx.IssueDownloads
                               join i in ctx.Issues on d.IssueId equals i.Id
                               where i.VolumeId == volumeId
                               orderby d.AddedAt descending
                               select d).ToListAsync(ct);
        return await EnrichDownloadsAsync(ctx, downloads, ct);
    }

    /// <summary>
    /// Downloads actuellement bloqués (section d'alerte du Dashboard). On charge les lignes encore
    /// actives, on les enrichit, PUIS on filtre sur <see cref="DownloadStatus.Stalled"/> : filtrer
    /// en SQL renverrait l'état du dernier enrichissement (le statut n'est recalculé qu'ici), donc
    /// raterait un torrent qui vient de perdre ses seeders.
    /// </summary>
    public async Task<List<DownloadItemData>> GetStalledDownloadsAsync(int limit, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var downloads = await ctx.IssueDownloads
            .Where(d => ActiveDownloadStatuses.Contains(d.Status))
            .OrderByDescending(d => d.AddedAt)
            .ToListAsync(ct);

        var enriched = await EnrichDownloadsAsync(ctx, downloads, ct);
        return enriched
            .Where(d => d.Download.Status == DownloadStatus.Stalled)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    // Complète chaque IssueDownload avec son Issue/Volume et son état QBittorrent live, en
    // persistant le statut mappé si besoin. Partagé entre GetDownloadsAsync (liste complète,
    // usage interne), GetDownloadsPageAsync (page affichée à l'utilisateur), les vues ciblées
    // ci-dessus (issue / volume / stalled) et UpdateDownloadHashAsync. Prend le DbStorageContext du GetDb() de la méthode APPELANTE — chaque
    // appel à GetDb() retourne une instance différente (voir son commentaire), donc appeler GetDb()
    // ici forcerait un SaveChangesAsync() sur un contexte qui n'a jamais chargé/suivi les entités
    // mutées ci-dessous, et la persistance échouerait silencieusement (les valeurs restent correctes
    // dans la réponse HTTP de la requête en cours, mais ne sont jamais écrites en base).
    private async Task<List<DownloadItemData>> EnrichDownloadsAsync(DbStorageContext ctx, List<IssueDownload> downloads, CancellationToken ct)
    {
        if (downloads.Count == 0) return [];

        var issueIds = downloads.Select(d => d.IssueId).Distinct().ToList();
        var issues = await ctx.Issues
            .Where(i => issueIds.Contains(i.Id))
            .ToListAsync(ct);

        var volumeIds = issues.Select(i => i.VolumeId).Distinct().ToList();
        var volumes = await ctx.Volumes
            .Where(v => volumeIds.Contains(v.Id))
            .ToListAsync(ct);

        // Récupère les infos live depuis QBittorrent pour tous les hashes connus
        var qb = GetService<QBittorrentService, QBittorrentOptions>();
        var hashes = downloads.Where(d => !string.IsNullOrEmpty(d.TorrentHash)).Select(d => d.TorrentHash).ToList();
        var torrents = qb.CurrentState.State == EState.OK && hashes.Count > 0
            ? await qb.GetTorrentsAsync(hashes, ct)
            : [];

        // Nombre total de downloads par hash (toute la table, pas seulement la page) — pour prévenir
        // qu'une suppression de torrent embarque les downloads jumeaux d'un PACK.
        var hashCounts = hashes.Count > 0
            ? await ctx.IssueDownloads
                .Where(d => hashes.Contains(d.TorrentHash))
                .GroupBy(d => d.TorrentHash)
                .Select(g => new { Hash = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Hash, x => x.Count, ct)
            : [];

        var torrentByHash = torrents.ToDictionary(t => t.Hash, StringComparer.OrdinalIgnoreCase);
        var issueById = issues.ToDictionary(i => i.Id);
        var volumeById = volumes.ToDictionary(v => v.Id);

        var result = new List<DownloadItemData>();
        foreach (var dl in downloads)
        {
            issueById.TryGetValue(dl.IssueId, out var issue);
            var volume = issue is not null && volumeById.TryGetValue(issue.VolumeId, out var v) ? v : null;
            torrentByHash.TryGetValue(dl.TorrentHash, out var torrent);

            // Mappe l'état QBittorrent → DownloadStatus et persiste si changé.
            // Une fois qu'Inkhound a pris la main sur le statut (Finished/Syncing/Done), on ne
            // laisse plus le polling QBittorrent l'écraser (QBittorrent reste "terminé" indéfiniment).
            var alreadyOwnedByInkhound = dl.Status is DownloadStatus.Finished or DownloadStatus.Syncing or DownloadStatus.Done;
            if (torrent is not null)
            {
                var newStatus = MapQBittorrentState(torrent.State);
                if (!alreadyOwnedByInkhound && newStatus != dl.Status)
                {
                    dl.Status = newStatus;
                    dl.UpdatedAt = DateTime.UtcNow;
                }

                // Rattrapage : le torrent est bien retrouvé mais le titre n'a jamais été renseigné
                // (lignes créées avant l'ajout de TorrentTitle, ou tout cas où l'info manquait) — on le
                // récupère depuis QBittorrent à cet instant. Seul le titre est récupérable ainsi :
                // QBittorrent ne conserve ni l'URL de retéléchargement d'origine ni le nom du tracker
                // Prowlarr (ce dernier n'existe que côté Inkhound, jamais transmis à QBittorrent).
                if (string.IsNullOrEmpty(dl.TorrentTitle) && !string.IsNullOrEmpty(torrent.Name))
                {
                    dl.TorrentTitle = torrent.Name;
                    dl.UpdatedAt = DateTime.UtcNow;
                }
            }
            else if (!alreadyOwnedByInkhound && dl.Status != DownloadStatus.NotFound)
            {
                dl.Status = DownloadStatus.NotFound;
                dl.UpdatedAt = DateTime.UtcNow;
            }

            var sharedWith = !string.IsNullOrEmpty(dl.TorrentHash) && hashCounts.TryGetValue(dl.TorrentHash, out var hc)
                ? Math.Max(0, hc - 1)
                : 0;

            result.Add(new DownloadItemData(dl, issue, volume, torrent, sharedWith));
        }

        await SaveEnrichedStatusesAsync(ctx, ct);
        return result;
    }

    // La persistance du statut enrichi est du best-effort : une ligne peut disparaître entre son
    // chargement et l'écriture (suppression d'un download depuis une autre requête pendant le
    // polling 10 s des pages qui les affichent — page Downloads, page Issue). EF lève alors une
    // DbUpdateConcurrencyException pour un UPDATE qui n'affecte aucune ligne, ce qui ferait échouer
    // un GET par ailleurs parfaitement valide : la réponse en cours reste correcte, seule
    // l'écriture des lignes disparues n'a plus de sens.
    // SaveChanges étant transactionnel, on détache les entités en conflit puis on retente une fois
    // pour ne pas perdre au passage le statut des lignes toujours présentes.
    private static async Task SaveEnrichedStatusesAsync(DbStorageContext ctx, CancellationToken ct)
    {
        if (!ctx.ChangeTracker.HasChanges()) return;

        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
                entry.State = EntityState.Detached;

            if (ex.Entries.Count == 0 || !ctx.ChangeTracker.HasChanges()) return;

            try { await ctx.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { /* une autre ligne a disparu entre-temps */ }
        }
    }

    private static DownloadStatus MapQBittorrentState(string state) => state.ToLowerInvariant() switch
    {
        "downloading" or "checkingdl" or "metadl" => DownloadStatus.Downloading,
        // Téléchargement en cours mais aucune connexion/seed — ne progresse pas faute de sources.
        "stalleddl" => DownloadStatus.Stalled,
        // "pauseddl" (QBittorrent < 5) / "stoppeddl" (QBittorrent 5.x — renommage)
        "pauseddl" or "stoppeddl" => DownloadStatus.Paused,
        // "stalledup" = téléchargement terminé, en seed sans peer → pour Inkhound c'est "prêt".
        "uploading" or "stalledup" or "pausedup" or "stoppedup" or "checkingup" or "queuedup" => DownloadStatus.Finished,
        "error" or "missingfiles" => DownloadStatus.Error,
        _ => DownloadStatus.Unknown
    };

    // Le torrent télécharge activement (ou tente de) — par opposition à un état arrêté/en pause/terminé.
    private static bool IsActivelyDownloading(string state) => state.ToLowerInvariant() switch
    {
        "downloading" or "stalleddl" or "metadl" or "checkingdl" or "queueddl" or "forceddl" => true,
        _ => false
    };

    // Relit et revérifie TOUTE la table IssueDownloads contre l'état réel de QBittorrent (via
    // EnrichDownloadsAsync, qui détecte désormais aussi les hash orphelins — voir NotFound).
    public async Task LaunchJobRefreshDownloads(RefreshDownloadsJobParameters parameters)
    {
        var job = StartJob("Refresh downloads", parameters);
        job.SetState(JobState.RUNNING);

        var qb = GetService<QBittorrentService, QBittorrentOptions>();
        if (qb.CurrentState.State != EState.OK)
        {
            JobSendTrace("[Refresh] QBittorrent service unavailable", ETraceLevel.ERROR);
            EndJob(false);
            return;
        }

        var downloads = await GetDownloadsAsync(null);
        var byStatus = downloads.GroupBy(d => d.Download.Status).ToDictionary(g => g.Key, g => g.Count());

        JobSendTrace($"[Refresh] {downloads.Count} download(s) checked");
        foreach (var (status, count) in byStatus)
            JobSendTrace($"[Refresh] {count} {status}");

        if (byStatus.GetValueOrDefault(DownloadStatus.NotFound) is > 0 and var notFound)
            JobSendTrace($"[Refresh] {notFound} download(s) could not be matched to a QBittorrent torrent — edit them from the Downloads page", ETraceLevel.WARNING);

        EndJob(true);
    }

    // Corrige manuellement le hash d'un download (ex. après un hash orphelin détecté par le
    // refresh) : vérifié auprès de QBittorrent avant d'être persisté, puis le statut est recalculé
    // normalement via EnrichDownloadsAsync (qui rattrape aussi le titre si absent, voir § A.3).
    public async Task<(bool Success, string? Error, DownloadItemData? Item)> UpdateDownloadHashAsync(
        Guid downloadId, string newHash, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var download = await ctx.IssueDownloads.FindAsync([downloadId], ct);
        if (download is null) return (false, "Download not found.", null);

        var trimmedHash = newHash.Trim();
        if (string.IsNullOrEmpty(trimmedHash)) return (false, "Hash is required.", null);

        var qb = GetService<QBittorrentService, QBittorrentOptions>();
        if (qb.CurrentState.State != EState.OK) return (false, "QBittorrent service unavailable.", null);

        var torrents = await qb.GetTorrentsAsync([trimmedHash], ct);
        if (torrents.Count == 0) return (false, "No torrent found in QBittorrent with this hash.", null);

        download.TorrentHash = trimmedHash;
        download.Status = DownloadStatus.Unknown; // laisse EnrichDownloadsAsync recalculer normalement
        await ctx.SaveChangesAsync(ct);

        var enriched = await EnrichDownloadsAsync(ctx, [download], ct);
        return (true, null, enriched.FirstOrDefault());
    }

    // Supprime le suivi d'un download, quel que soit son état.
    // removeTorrent : supprime aussi le torrent (et ses fichiers déjà téléchargés) de QBittorrent ;
    // dans ce cas, tous les downloads jumeaux (même hash, PACK multi-issues) sont supprimés eux aussi
    // — le frontend prévient l'utilisateur au moment de la confirmation.
    // ban : mémorise le couple (Issue, Torrent) dans IssueTorrentBans pour que les recherches
    // Prowlarr suivantes le scorent 0 (voir TorrentBanIndex) — un ban par ligne supprimée, donc
    // aussi pour les jumelles d'un PACK.
    // Chaque Issue impactée redevient MISSING si elle était DOWNLOADING et que plus aucune ligne
    // restante ne la télécharge.
    // DeletedCount : nombre de lignes IssueDownload effectivement supprimées ; BanCount : nombre de
    // bans réellement créés (les doublons d'un ban existant ne sont pas recréés).
    public async Task<(bool Success, string? Error, bool TorrentRemoved, int DeletedCount, int BanCount)> DeleteDownloadAsync(
        Guid downloadId, bool removeTorrent, bool ban = true, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var download = await ctx.IssueDownloads.FindAsync([downloadId], ct);
        if (download is null) return (false, "Download not found.", false, 0, 0);

        var hash = download.TorrentHash;

        // Par défaut on ne supprime que la ligne demandée. Si le retrait du torrent est demandé ET
        // qu'il réussit, on supprime aussi les lignes jumelles (elles pointeraient un torrent absent).
        var toRemove = new List<IssueDownload> { download };
        var torrentRemoved = false;

        if (removeTorrent && !string.IsNullOrEmpty(hash))
        {
            var qb = GetService<QBittorrentService, QBittorrentOptions>();
            if (qb.CurrentState.State == EState.OK)
            {
                torrentRemoved = await qb.DeleteTorrentAsync(hash, deleteFiles: true, ct);
                if (torrentRemoved)
                    toRemove = await ctx.IssueDownloads.Where(d => d.TorrentHash == hash).ToListAsync(ct);
            }
        }

        var removedIds = toRemove.Select(d => d.Id).ToList();
        var affectedIssueIds = toRemove.Select(d => d.IssueId).Distinct().ToList();

        var issues = await ctx.Issues.Where(i => affectedIssueIds.Contains(i.Id)).ToListAsync(ct);
        foreach (var issue in issues.Where(i => i.Status == IssueStatus.DOWNLOADING))
        {
            // Les lignes de toRemove sont encore en base à cet instant (SaveChanges plus bas) — on
            // les exclut explicitement.
            var stillDownloadedElsewhere = await ctx.IssueDownloads
                .AnyAsync(d => !removedIds.Contains(d.Id) && d.IssueId == issue.Id, ct);
            if (!stillDownloadedElsewhere)
            {
                issue.Status = IssueStatus.MISSING;
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issue.Id));
            }
        }

        var banCount = ban ? await AddTorrentBansAsync(ctx, toRemove, "Download deleted", ct) : 0;

        ctx.IssueDownloads.RemoveRange(toRemove);
        await ctx.SaveChangesAsync(ct);
        return (true, null, torrentRemoved, toRemove.Count, banCount);
    }

    // Crée un ban par download supprimé, en ignorant ceux déjà couverts par un ban existant de la
    // même issue (même URL, ou à défaut même hash, ou à défaut même titre — mêmes clés que
    // TorrentBanIndex). Ne sauvegarde pas : l'appelant commit avec le reste de son unité de travail.
    private static async Task<int> AddTorrentBansAsync(
        DbStorageContext ctx, List<IssueDownload> downloads, string reason, CancellationToken ct)
    {
        var issueIds = downloads.Select(d => d.IssueId).Distinct().ToList();
        var existing = await ctx.IssueTorrentBans.Where(b => issueIds.Contains(b.IssueId)).ToListAsync(ct);

        var created = 0;
        foreach (var download in downloads)
        {
            var alreadyBanned = existing.Any(b => b.IssueId == download.IssueId && SameTorrent(b, download));
            if (alreadyBanned) continue;

            var newBan = new IssueTorrentBan
            {
                Id = Guid.NewGuid(),
                IssueId = download.IssueId,
                TorrentHash = download.TorrentHash,
                TorrentTitle = download.TorrentTitle,
                DownloadUrl = download.DownloadUrl,
                TrackerName = download.TrackerName,
                CreatedAt = DateTime.UtcNow,
                Reason = reason
            };
            ctx.IssueTorrentBans.Add(newBan);
            existing.Add(newBan);   // évite un doublon entre deux lignes jumelles du même lot
            created++;
        }
        return created;
    }

    private static bool SameTorrent(IssueTorrentBan ban, IssueDownload download)
    {
        if (!string.IsNullOrEmpty(download.DownloadUrl) && !string.IsNullOrEmpty(ban.DownloadUrl))
            return string.Equals(ban.DownloadUrl, download.DownloadUrl, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(download.TorrentHash) && !string.IsNullOrEmpty(ban.TorrentHash))
            return string.Equals(ban.TorrentHash, download.TorrentHash, StringComparison.OrdinalIgnoreCase);
        return !string.IsNullOrEmpty(download.TorrentTitle)
            && string.Equals(ban.TorrentTitle, download.TorrentTitle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Bans de torrents d'une issue, du plus récent au plus ancien (page détail de l'issue).</summary>
    public async Task<List<IssueTorrentBan>> GetIssueBansAsync(Guid issueId, CancellationToken ct = default)
        => await GetDb().IssueTorrentBans
            .Where(b => b.IssueId == issueId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

    /// <summary>Lève un ban ; <c>false</c> si l'identifiant est inconnu.</summary>
    public async Task<bool> DeleteIssueBanAsync(Guid banId, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var ban = await ctx.IssueTorrentBans.FindAsync([banId], ct);
        if (ban is null) return false;

        ctx.IssueTorrentBans.Remove(ban);
        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(ban.IssueId));
        return true;
    }

    /// <summary>Index des torrents bannis pour une issue — périmètre d'une recherche par issue.</summary>
    public async Task<TorrentBanIndex> LoadBanIndexForIssueAsync(Guid issueId, CancellationToken ct = default)
        => TorrentBanIndex.From(await GetDb().IssueTorrentBans.Where(b => b.IssueId == issueId).ToListAsync(ct));

    /// <summary>
    /// Index des torrents bannis pour TOUTES les issues d'un volume — périmètre d'une recherche par
    /// volume : un torrent banni sur n'importe quelle issue du volume est écarté du pack.
    /// </summary>
    public async Task<TorrentBanIndex> LoadBanIndexForVolumeAsync(Guid volumeId, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var bans = await (from b in ctx.IssueTorrentBans
                          join i in ctx.Issues on b.IssueId equals i.Id
                          where i.VolumeId == volumeId
                          select b).ToListAsync(ct);
        return TorrentBanIndex.From(bans);
    }

    public async Task LaunchJobProcessDownloads(ProcessDownloadsJobParameters parameters)
    {
        var archiveService = GetService<ArchiveService, ArchiveOption>();
        var qb = GetService<QBittorrentService, QBittorrentOptions>();

        var job = StartJob(parameters.IssueDownloadId.HasValue
            ? $"Process download {parameters.IssueDownloadId}"
            : "Process downloads", parameters);
        job.SetState(JobState.RUNNING);

        if (archiveService.CurrentState.State != EState.OK)
        {
            JobSendTrace("[Downloads] ArchiveService is not available", ETraceLevel.ERROR);
            EndJob(false);
            return;
        }

        var ctx = GetDb();

        // Rafraîchit les statuts depuis QBittorrent avant de sélectionner les éligibles
        await GetDownloadsAsync(null);

        var query = ctx.IssueDownloads.Where(d => d.Status == DownloadStatus.Finished || d.Status == DownloadStatus.Syncing);
        if (parameters.IssueDownloadId.HasValue)
            query = query.Where(d => d.Id == parameters.IssueDownloadId.Value);

        var eligible = await query.ToListAsync();
        JobSendTrace($"[Downloads] {eligible.Count} download(s) eligible for processing");
        job.CallbackHandler.UpdateTotal(eligible.Count);

        if (eligible.Count == 0)
        {
            EndJob(true);
            return;
        }

        var tempDir = archiveService.GenerateTempDirectory();
        var touchedVolumeIds = new HashSet<Guid>();

        try
        {
            foreach (var group in eligible.GroupBy(d => d.TorrentHash))
            {
                var torrentFiles = await qb.GetTorrentFilesAsync(group.Key);
                var archiveFiles = torrentFiles.Where(f => IsArchiveFile(f.Name)).ToList();

                foreach (var download in group)
                {
                    JobSendTrace($"[Downloads] Processing download {download.Id} (hash {download.TorrentHash})");

                    var issue = await ctx.Issues.FindAsync(download.IssueId);
                    if (issue is null)
                    {
                        JobSendTrace($"[Downloads] Issue {download.IssueId} not found, skipping", ETraceLevel.WARNING);
                        job.Progress.Increment(false);
                        job.CallbackHandler.Callback(job.Progress);
                        continue;
                    }

                    // 1. Fichier explicitement retenu lors de la revue du PACK (fiable, y compris pour
                    //    les numéros atypiques : #0, hors-série…).
                    // 2. À défaut : torrent mono-fichier → ce fichier.
                    // 3. À défaut : appariement par numéro extrait du nom (lignes créées avant FileName,
                    //    grab direct sur un PACK…).
                    var matchedFile =
                        (!string.IsNullOrEmpty(download.FileName)
                            ? archiveFiles.FirstOrDefault(f => f.Name == download.FileName)
                            : null)
                        ?? (archiveFiles.Count == 1 ? archiveFiles[0] : null)
                        ?? archiveFiles.FirstOrDefault(f => TorrentTypeAnalyzer.ExtractIssueNumber(f.Name) == issue.IssueNumber);

                    if (matchedFile is null)
                    {
                        JobSendTrace($"[Downloads] Could not match a file for issue #{issue.IssueNumber} in torrent {download.TorrentHash}", ETraceLevel.WARNING);
                        job.Progress.Increment(false);
                        job.CallbackHandler.Callback(job.Progress);
                        continue;
                    }

                    var downloadedFilePath = Path.Combine(archiveService.DownloadsPath, matchedFile.Name);
                    JobSendTrace($"[Downloads] Looking for downloaded file at '{downloadedFilePath}'", ETraceLevel.DEBUG);
                    if (!File.Exists(downloadedFilePath))
                    {
                        JobSendTrace($"[Downloads] File '{matchedFile.Name}' not yet present in downloads folder for issue #{issue.IssueNumber} — marking Syncing");
                        download.Status = DownloadStatus.Syncing;
                        download.UpdatedAt = DateTime.UtcNow;
                        await ctx.SaveChangesAsync();
                        job.Progress.Increment(true);
                        job.CallbackHandler.Callback(job.Progress);
                        continue;
                    }

                    var volume = await ctx.Volumes.FindAsync(issue.VolumeId);
                    var library = volume is not null ? await ctx.Libraries.FindAsync(volume.LibraryId) : null;
                    if (volume is null || library is null)
                    {
                        JobSendTrace($"[Downloads] Volume/Library not found for issue {issue.Id}", ETraceLevel.ERROR);
                        job.Progress.Increment(false);
                        job.CallbackHandler.Callback(job.Progress);
                        continue;
                    }

                    var importSubDir = Directory.CreateDirectory(Path.Combine(archiveService.ImportPath, download.Id.ToString("N")));
                    var importedFilePath = Path.Combine(importSubDir.FullName, Path.GetFileName(matchedFile.Name));
                    var downloadedFileSizeMb = new FileInfo(downloadedFilePath).Length / 1024.0 / 1024.0;
                    JobRunTimed(
                        $"[Downloads] Copying '{matchedFile.Name}' ({downloadedFileSizeMb:F1} MB) from downloads to import folder",
                        () => File.Copy(downloadedFilePath, importedFilePath, overwrite: true));

                    var workingSubPath = Path.Combine(tempDir.FullName, download.Id.ToString("N"));
                    var importParams = new ArchiveConverterPdfJobParameters
                    {
                        SourceFile = importedFilePath,
                        WorkingPath = workingSubPath,
                        Library = library,
                        Volume = volume,
                        Issue = issue
                    };

                    JobSendTrace($"[Downloads] Importing '{matchedFile.Name}' for issue #{issue.IssueNumber} — {volume.Title}");
                    var archive = await ImportArchiveAsync(importParams, job.CallbackHandler);

                    Directory.Delete(importSubDir.FullName, recursive: true);

                    if (archive is null)
                    {
                        JobSendTrace($"[Downloads] Import failed for issue #{issue.IssueNumber} — {volume.Title}", ETraceLevel.ERROR);
                        job.Progress.Increment(false);
                        job.CallbackHandler.Callback(job.Progress);
                        continue;
                    }

                    var volumeDir = archiveService.CreateVolumeDirectory(volume, library);
                    var dest = Path.Combine(volumeDir.FullName, archive.Name);
                    var archiveSizeMb = archive.Length / 1024.0 / 1024.0;
                    JobRunTimed(
                        $"[Downloads] Copying '{archive.Name}' ({archiveSizeMb:F1} MB) to library folder",
                        () => File.Copy(archive.FullName, dest, overwrite: true));
                    archiveService.EnsurePermissiveFileMode(dest);

                    issue.CbzFilename = archive.Name;
                    issue.FileSizeBytes = (int)archive.Length;
                    issue.Status = IssueStatus.DOWNLOADED;
                    issue.DownloadedAt = DateTime.UtcNow;

                    download.Status = DownloadStatus.Done;
                    download.UpdatedAt = DateTime.UtcNow;

                    // Persiste d'abord le changement de statut de l'issue : le recalcul du compteur
                    // ci-dessous interroge la base via CountAsync, qui ne verrait pas cette issue si
                    // elle n'était pas encore sauvegardée (bug historique : le dernier item traité
                    // n'était jamais comptabilisé).
                    await ctx.SaveChangesAsync();
                    await RecalculateVolumeStatisticsAsync(ctx, volume.Id);
                    OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issue.Id));
                    touchedVolumeIds.Add(volume.Id);

                    JobSendTrace($"[Downloads] Issue #{issue.IssueNumber} — {volume.Title} imported successfully");
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                }
            }

            foreach (var volumeId in touchedVolumeIds)
            {
                var volume = await ctx.Volumes.FindAsync(volumeId);
                var library = volume is not null ? await ctx.Libraries.FindAsync(volume.LibraryId) : null;
                if (volume is null || library is null || string.IsNullOrEmpty(library.KavitaPath)) continue;

                var kavita = GetService<KavitaService, KavitaOptions>();
                var kavitaFolderPath = library.KavitaPath.TrimEnd('/', '\\') + "/" + ArchiveService.GetPath(volume);
                await JobRunTimedAsync(
                    $"[Downloads] Triggering Kavita folder scan: {kavitaFolderPath}",
                    () => kavita.ScanFolderAsync(kavitaFolderPath));
            }

            EndJob(job.Progress.Error == 0);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Downloads] Unhandled error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
        finally
        {
            if (tempDir.Exists)
                tempDir.Delete(recursive: true);
        }
    }

    internal static bool IsArchiveFile(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".cbz" or ".cbr" or ".pdf";
    }

    #endregion

    // Construit les requêtes du plus précis au plus général en croisant Volume + Issue.
    // Le numéro est inséré sans préfixe "#" : c'est du texte libre transmis tel quel à l'indexer
    // (recherche full-text), et un "#" littéral ne matche pas les releases nommées "T15".
    // internal (+ InternalsVisibleTo, voir AssemblyInfo.cs) pour être testable indépendamment de la DB/Prowlarr.
    internal static List<string> BuildSearchQueries(Volume volume, Issue issue)
    {
        var title = volume.Title;
        var num = issue.IssueNumber;
        var year = issue.Year ?? volume.Year;
        var publisher = string.IsNullOrWhiteSpace(volume.Publisher) ? null : volume.Publisher;
        var issueTitle = string.IsNullOrWhiteSpace(issue.Title) ? null : issue.Title;

        List<string> candidates = [];

        // Niveau 1 : max — titre + numéro + éditeur + année + titre issue
        if (publisher is not null && year is not null && issueTitle is not null)
            candidates.Add($"{title} {num} {publisher} ({year}) {issueTitle}");

        // Niveau 2 : titre + numéro + éditeur + année
        if (publisher is not null && year is not null)
            candidates.Add($"{title} {num} {publisher} ({year})");

        // Niveau 3 : titre + numéro + année
        if (year is not null)
            candidates.Add($"{title} {num} ({year})");

        // Niveau 4 : titre + numéro
        candidates.Add($"{title} {num}");

        // Niveau 5 (fallback) : titre seul
        candidates.Add(title);

        // Déduplication en conservant l'ordre
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return candidates.Where(seen.Add).ToList();
    }

    // Construit les requêtes du plus précis au plus général à partir des seules informations du
    // Volume (pas de numéro de tome ciblé) — utilisée pour la recherche "volume complet", dont le
    // but est de trouver un PACK/une compilation couvrant le plus grand nombre d'issues manquantes.
    // Inclut des variantes "intégrale"/"pack" pour maximiser les chances de trouver une compilation.
    internal static List<string> BuildSearchQueries(Volume volume)
    {
        var title = volume.Title;
        var year = volume.Year;
        var publisher = string.IsNullOrWhiteSpace(volume.Publisher) ? null : volume.Publisher;

        List<string> candidates = [];

        if (publisher is not null && year is not null)
            candidates.Add($"{title} {publisher} ({year})");

        if (year is not null)
            candidates.Add($"{title} ({year})");

        if (publisher is not null)
            candidates.Add($"{title} {publisher}");

        candidates.Add($"{title} intégrale");
        candidates.Add($"{title} pack");
        candidates.Add(title);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return candidates.Where(seen.Add).ToList();
    }

    #endregion
}
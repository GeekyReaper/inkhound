using Inkhound.Core.ApiTokens;
using Inkhound.Core.Bedetheque;
using Inkhound.Core.ComicVine;
using Inkhound.Core.Models;
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

public class InkhoundManager : BaseServiceManager
{
    private readonly string _dbPath;



    //public InkhoundDbContext? Database { get; private set; }

    public InkhoundManager(string dbPath = "data/inkhound.db")
    {
        _dbPath = dbPath;

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

    public async Task AutomaticLoadServices()
    {
        // Instantiate services
        // WebshareProxy first so it's already registered as the active proxy provider before the
        // other services build their first HttpClient.
        GetService<WebshareProxyService, WebshareProxyOptions>();
        var databaseService = GetService<DbStorageService, DbStorageOption>();
        var comicVine = GetService<ComicVineSourceService, ComicVineOptions>();
        GetService<BedethequeSourceService, BedethequeOptions>();
        var archiveService = GetService<ArchiveService, ArchiveOption>();
        var kavitaService = GetService<KavitaService, KavitaOptions>();
        GetService<ProwlarrService, ProwlarrOptions>();
        GetService<QBittorrentService, QBittorrentOptions>();
        GetService<ApiTokenService, ApiTokenOptions>();

        // Load database options and initialize database
        var databaseoption = new DbStorageOption { Path = _dbPath, UseInMemory = false };

        if (await databaseService.LoadOptions(databaseoption.GetOptions()))
        {
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

        }


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

    public async Task<bool> DeleteLibraryAsync(Guid id)
    {
        var ctx = GetDb();
        var library = await ctx.Libraries.FindAsync(id);
        if (library is null) return false;

        ctx.Libraries.Remove(library);
        await ctx.SaveChangesAsync();
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Library>(id));
        return true;
    }

    public async Task<bool> DeleteVolumeAsync(Guid id)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync(id);
        if (volume is null) return false;

        ctx.Issues.RemoveRange(ctx.Issues.Where(i => i.VolumeId == id));
        ctx.Volumes.Remove(volume);
        await ctx.SaveChangesAsync();
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(id));
        return true;
    }
    #endregion

    #region Volume CRUD
    public async Task<Volume?> GetVolumeAsync(Guid id)
    {
        return await GetDb().Volumes.FindAsync(id);
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
    private readonly ConcurrentDictionary<Guid, SearchVolumesJobResult> _searchResults = new();

    public SearchVolumesJobResult? GetSearchJobResult(Guid jobId)
        => _searchResults.TryGetValue(jobId, out var result) ? result : null;

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
            _searchResults[job.JobId] = result;
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
                stats.Add(new SourceSearchStats(src.SourceKey, 0, sw.ElapsedMilliseconds, false, ex.Message));
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
        // pertinence réelle.
        var scoredItems = results
            .SelectMany(p => p.Items)
            .Select(v => v with { Score = SearchScoring.ScoreTitleMatch(name, v.Name, v.CountOfIssues, v.Language) })
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

                    volume = Mapper.Map(cvVolume);
                    volume.Id = Guid.NewGuid();
                    volume.LibraryId = parameters.LibraryId;
                    volume.Status = VolumeStatus.PAUSED;
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
            volume.CountOfIssues = localIssues.Count;
            volume.CountOfDownloadedIssues = localIssues.Count(i => i.Status == IssueStatus.DOWNLOADED);


            volume.UpdatedAt = DateTime.UtcNow;
            volume.CountOfIssues = await ctx.Issues.CountAsync(i => i.VolumeId == volume.Id);
            volume.CountOfDownloadedIssues = await ctx.Issues.CountAsync(i => i.VolumeId == volume.Id && i.Status == IssueStatus.DOWNLOADED);
            volume.Status = volume.CountOfIssues == volume.CountOfDownloadedIssues ? VolumeStatus.COMPLETED : VolumeStatus.MONITORED;

            await ctx.SaveChangesAsync();
            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));


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




    #endregion

    #region Volume Actions
    public async Task<Volume> AddVolumeFromComicVineAsync(
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
        volume.CountOfIssues = 0;
        ctx.Volumes.Add(volume);
        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));

        var cvIssues = await comicVine.GetAllIssuesForVolumeAsync(comicVineVolumeId, ELevelDetail.FULL, ct);

        List<VolumeAuthor> allIssueAuthors = [];

        foreach (var cvIssue in cvIssues)
        {
            var issue = Mapper.Map(cvIssue);
            issue.Id = Guid.NewGuid();
            issue.Status = IssueStatus.MISSING;
            issue.VolumeId = volume.Id;
            allIssueAuthors.AddRange(issue.Authors);
            ctx.Issues.Add(issue);
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
            await ctx.SaveChangesAsync(ct);
            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));
        }

        return volume;
    }

    public async Task<bool> RematchVolumeFromComicVineAsync(
        Guid volumeId, int comicVineVolumeId, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync([volumeId], ct);
        if (volume is null) return false;

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

        var cvIssues = await comicVine.GetAllIssuesForVolumeAsync(comicVineVolumeId, ELevelDetail.FULL, ct);
        var existingIssues = await ctx.Issues.Where(i => i.VolumeId == volumeId).ToListAsync(ct);

        var matchedExistingIds = new HashSet<Guid>();

        foreach (var cvIssue in cvIssues)
        {
            var cvId = cvIssue.Id.ToString();
            int.TryParse(cvIssue.IssueNumber, out var issueNum);

            // Correspondance par SourceId en priorité, puis par numéro d'issue
            var existing =
                existingIssues.FirstOrDefault(i => i.SourceId == cvId) ??
                existingIssues.FirstOrDefault(i => string.IsNullOrEmpty(i.SourceId) && i.IssueNumber == issueNum && !matchedExistingIds.Contains(i.Id));

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
            }
            else
            {
                var newIssue = mappedIssue;
                newIssue.Id      = Guid.NewGuid();
                newIssue.VolumeId = volumeId;
                newIssue.Status  = IssueStatus.MISSING;
                ctx.Issues.Add(newIssue);
            }
        }

        // Supprimer les issues MISSING non appariées
        foreach (var orphan in existingIssues.Where(i => !matchedExistingIds.Contains(i.Id)))
        {
            if (orphan.Status == IssueStatus.MISSING)
                ctx.Issues.Remove(orphan);
            // DOWNLOADED / DOWNLOADING → conservé
        }

        await ctx.SaveChangesAsync(ct);

        volume.CountOfIssues           = await ctx.Issues.CountAsync(i => i.VolumeId == volumeId, ct);
        volume.CountOfDownloadedIssues = await ctx.Issues.CountAsync(i => i.VolumeId == volumeId && i.Status == IssueStatus.DOWNLOADED, ct);
        await ctx.SaveChangesAsync(ct);

        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volumeId));
        return true;
    }

    // Miroir de AddVolumeFromComicVineAsync pour la source Bedetheque : utilise les modèles natifs
    // riches de BedethequeSourceService (BdSerie/BdAlbum, avec auteurs) plutôt que le DTO mince
    // SourceVolume/SourceIssue utilisé pour l'agrégation de recherche.
    public async Task<Volume> AddVolumeFromBedethequeAsync(
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
        volume.CountOfIssues = 0;
        ctx.Volumes.Add(volume);
        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));

        var bdAlbums = await bedetheque.GetAllAlbumsForSerieAsync(bdSerieId, ct);

        List<VolumeAuthor> allIssueAuthors = [];

        foreach (var bdAlbum in bdAlbums)
        {
            var issue = Mapper.Map(bdAlbum);
            issue.Id = Guid.NewGuid();
            issue.Status = IssueStatus.MISSING;
            issue.VolumeId = volume.Id;
            allIssueAuthors.AddRange(issue.Authors);
            ctx.Issues.Add(issue);
        }
        if (bdAlbums.Count > 0)
        {
            var roleByName = allIssueAuthors
                        .GroupBy(a => a.Name)
                        .ToDictionary(g => g.Key, g => g.First().Role);

            volume.Authors = volume.Authors
                .Select(a => string.IsNullOrEmpty(a.Role) && roleByName.TryGetValue(a.Name, out var role)
                    ? new VolumeAuthor(a.Name, role ?? string.Empty)
                    : a)
                .ToList();

            volume.CountOfIssues = bdAlbums.Count;
            volume.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync(ct);
            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));
        }

        return volume;
    }

    // Miroir de RematchVolumeFromComicVineAsync pour la source Bedetheque.
    public async Task<bool> RematchVolumeFromBedethequeAsync(
        Guid volumeId, int bdSerieId, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync([volumeId], ct);
        if (volume is null) return false;

        var bedetheque = GetService<BedethequeSourceService, BedethequeOptions>();
        if (bedetheque.CurrentState.State != EState.OK)
            throw new InvalidOperationException("Bedetheque service is not available");

        var bdSerie = await bedetheque.GetSerieAsync(bdSerieId, ct)
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
        volume.UpdatedAt    = DateTime.UtcNow;

        var bdAlbums = await bedetheque.GetAllAlbumsForSerieAsync(bdSerieId, ct);
        var existingIssues = await ctx.Issues.Where(i => i.VolumeId == volumeId).ToListAsync(ct);

        var matchedExistingIds = new HashSet<Guid>();

        foreach (var bdAlbum in bdAlbums)
        {
            var bdId = bdAlbum.Id.ToString();
            int.TryParse(bdAlbum.NumeroAlbum, out var issueNum);

            // Correspondance par SourceId en priorité, puis par numéro d'issue
            var existing =
                existingIssues.FirstOrDefault(i => i.SourceId == bdId) ??
                existingIssues.FirstOrDefault(i => string.IsNullOrEmpty(i.SourceId) && i.IssueNumber == issueNum && !matchedExistingIds.Contains(i.Id));

            var mappedIssue = Mapper.Map(bdAlbum);

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
            }
            else
            {
                var newIssue = mappedIssue;
                newIssue.Id      = Guid.NewGuid();
                newIssue.VolumeId = volumeId;
                newIssue.Status  = IssueStatus.MISSING;
                ctx.Issues.Add(newIssue);
            }
        }

        // Supprimer les issues MISSING non appariées
        foreach (var orphan in existingIssues.Where(i => !matchedExistingIds.Contains(i.Id)))
        {
            if (orphan.Status == IssueStatus.MISSING)
                ctx.Issues.Remove(orphan);
            // DOWNLOADED / DOWNLOADING → conservé
        }

        await ctx.SaveChangesAsync(ct);

        volume.CountOfIssues           = await ctx.Issues.CountAsync(i => i.VolumeId == volumeId, ct);
        volume.CountOfDownloadedIssues = await ctx.Issues.CountAsync(i => i.VolumeId == volumeId && i.Status == IssueStatus.DOWNLOADED, ct);
        await ctx.SaveChangesAsync(ct);

        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volumeId));
        return true;
    }

    // Dispatchers génériques utilisés par les contrôleurs Web — routent vers l'implémentation
    // dédiée à la source choisie par l'utilisateur dans les résultats de recherche.
    public Task<Volume> AddVolumeFromSourceAsync(Guid libraryId, string source, string sourceId, CancellationToken ct = default) =>
        source switch
        {
            "comicvine" => AddVolumeFromComicVineAsync(libraryId, int.Parse(sourceId), ct),
            "bedetheque" => AddVolumeFromBedethequeAsync(libraryId, int.Parse(sourceId), ct),
            _ => throw new InvalidOperationException($"Unknown source '{source}'"),
        };

    public Task<bool> RematchVolumeFromSourceAsync(Guid volumeId, string source, string sourceId, CancellationToken ct = default) =>
        source switch
        {
            "comicvine" => RematchVolumeFromComicVineAsync(volumeId, int.Parse(sourceId), ct),
            "bedetheque" => RematchVolumeFromBedethequeAsync(volumeId, int.Parse(sourceId), ct),
            _ => throw new InvalidOperationException($"Unknown source '{source}'"),
        };

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
                SourceId = Guid.NewGuid().ToString(),
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
                SourceId     = Guid.NewGuid().ToString(),
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

        volume.CountOfIssues = await ctx.Issues.CountAsync(i => i.VolumeId == volumeId, ct);
        await ctx.SaveChangesAsync(ct);

        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volumeId));
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
            if (volume is null) { EndJob(false); return; }

            var library = await ctx.Libraries.FindAsync(volume.LibraryId);
            if (library is null) { EndJob(false); return; }

            var archiveService = GetService<ArchiveService, ArchiveOption>();

            var downloadedIssues = await ctx.Issues
                .Where(i => i.VolumeId == parameters.VolumeId && i.Status == IssueStatus.DOWNLOADED)
                .ToListAsync();

            JobSendTrace($"[Regen] {downloadedIssues.Count} downloaded issues found for {volume.Title}");
            job.CallbackHandler.UpdateTotal(downloadedIssues.Count);

            foreach (var issue in downloadedIssues)
            {
                var cbzPath = ArchiveService.GetPath(issue, volume, library);
                JobSendTrace($"[Regen] Injecting ComicInfo into {issue.CbzFilename}");
                await archiveService.InjectComicInfoIntoCbzAsync(volume, issue, cbzPath);
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);
            }

            if (!string.IsNullOrEmpty(library.KavitaPath))
            {
                var kavita = GetService<KavitaService, KavitaOptions>();
                var kavitaFolderPath = library.KavitaPath.TrimEnd('/', '\\') + "/" + ArchiveService.GetPath(volume);
                JobSendTrace($"[Regen] Triggering Kavita folder scan: {kavitaFolderPath}");
                await kavita.ScanFolderAsync(kavitaFolderPath);
            }
            else
            {
                JobSendTrace("[Regen] No KavitaPath configured — falling back to full library scan");
                await ScanKavitaLibraryAsync(library.KavitaLibraryId);
            }

            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(parameters.VolumeId));
            EndJob(true);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Regen] Unhandled error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
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

    public async Task ImportArchiveFromDirectoryAsync(Guid volumeId, string importDirectory, bool overrideExisting = false, CancellationToken ct = default)
    {
        var ctx = GetDb();

        var volume = await ctx.Volumes.FindAsync([volumeId], ct)
            ?? throw new KeyNotFoundException($"Volume {volumeId} not found.");
        var library = await ctx.Libraries.FindAsync([volume.LibraryId], ct)
            ?? throw new KeyNotFoundException($"Library {volume.LibraryId} not found.");
        var issues = await ctx.Issues.Where(i => i.VolumeId == volumeId).ToListAsync(ct);

        var archiveService = GetService<ArchiveService, ArchiveOption>();
        if (archiveService.CurrentState.State != EState.OK)
            throw new InvalidOperationException("ArchiveService is not available");

        var job = StartJob($"Import {importDirectory} to {volume.Title}");
        job.SetState(JobState.RUNNING);

        var tempDir = archiveService.GenerateTempDirectory();
        JobSendTrace($"Created temporary directory {tempDir.FullName} for import processing", ETraceLevel.INFO);
        try
        {
            var files = _archiveExtensions
                .SelectMany(ext => Directory.GetFiles(importDirectory, ext))
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.Name)
                .ToList();

            job.AddTotal(files.Count);
            JobSendTrace($"Found {files.Count} archive files in import directory {importDirectory}", ETraceLevel.INFO);

            foreach (var file in files)
            {
                JobSendTrace($"Processing file {file.Name}", ETraceLevel.INFO);
                var issueNumber = SourceAnalyzer.ParseIssueNumber(file.Name);
                if (issueNumber is null) continue;

                var issue = issues.FirstOrDefault(i => i.IssueNumber == issueNumber);
                if (issue is null) continue;

                if (issue.Status == IssueStatus.DOWNLOADED && !overrideExisting)
                {
                    JobSendTrace($"Skipping {file.Name} (already downloaded)", ETraceLevel.INFO);
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    continue; // Skip already downloaded issues if not overriding
                }
                var subPath = Path.Combine(tempDir.FullName, issueNumber.Value.ToString("D3"));
                var parameters = new ArchiveConverterPdfJobParameters
                {
                    SourceFile = file.FullName,
                    WorkingPath = subPath,
                    Library = library,
                    Volume = volume,
                    Issue = issue
                };
                JobSendTrace($"Launching import job for file {file.Name} to issue {issue.Title} (issue number {issue.IssueNumber})", ETraceLevel.INFO);
                var archive = await ImportArchiveAsync(parameters, job.CallbackHandler);
                JobSendTrace($"Import job completed for file {file.Name} with result: {(archive != null ? "Success" : "Failure")}", ETraceLevel.INFO);
                if (archive is null) continue;

                var volumeDir = archiveService.CreateVolumeDirectory(volume, library);

                var dest = Path.Combine(volumeDir.FullName, archive.Name);
                JobSendTrace($"Moving archive file {archive.FullName} to final destination {dest}", ETraceLevel.INFO);
                File.Copy(archive.FullName, dest, overwrite: true);
                archiveService.EnsurePermissiveFileMode(dest);
                JobSendTrace($"Moving Done", ETraceLevel.INFO);

                issue.CbzFilename = archive.Name;
                issue.FileSizeBytes = (int)archive.Length;
                issue.Status = IssueStatus.DOWNLOADED;
                volume.CountOfDownloadedIssues = await ctx.Issues.CountAsync(i => i.VolumeId == volume.Id && i.Status == IssueStatus.DOWNLOADED, ct);
                volume.UpdatedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync(ct);
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issue.Id));
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));
                JobSendTrace($"Successfully imported file {file.Name} as issue {issue.Title} (issue number {issue.IssueNumber})", ETraceLevel.INFO);
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);
            }
        }
        catch (Exception ex)
        {
            JobSendTrace($"Erreur non gérée lors de l'import: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
            return;
        }

        if (tempDir.Exists)
            tempDir.Delete(recursive: true);
        JobSendTrace($"Import process completed for directory {importDirectory}", ETraceLevel.INFO);
        EndJob(true);


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
            JobSendTrace($"Erreur non gérée lors de l'import: {ex.Message}", ETraceLevel.ERROR);
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

    private static readonly string[] _archiveExtensions = ["*.cbz", "*.cbr", "*.pdf"];

    #endregion

    #region Prowlarr

    public async Task<List<ProwlarrIndexer>> GetProwlarrIndexersAsync(CancellationToken ct = default)
    {
        var prowlarr = GetService<ProwlarrService, ProwlarrOptions>();
        if (prowlarr.CurrentState.State != EState.OK) return [];
        return await prowlarr.GetIndexersAsync(ct);
    }

    public async Task<List<SelectedIndexer>> GetSelectedIndexersAsync()
    {
        return await GetDb().SelectedIndexers.ToListAsync();
    }

    public async Task SetSelectedIndexersAsync(
        List<(ProwlarrIndexer Indexer, List<int> CategoryIds)> items)
    {
        var ctx = GetDb();
        ctx.SelectedIndexers.RemoveRange(ctx.SelectedIndexers);
        foreach (var (indexer, categoryIds) in items)
        {
            ctx.SelectedIndexers.Add(new SelectedIndexer
            {
                IndexerId      = indexer.Id,
                Name           = indexer.Name,
                Protocol       = indexer.Protocol,
                AddedAt        = DateTime.UtcNow,
                CategoriesJson = JsonSerializer.Serialize(categoryIds)
            });
        }
        await ctx.SaveChangesAsync();
    }

    public async Task<List<ScoredSearchResultTorrent>> LaunchJobSearchMissingIssue(ProwlarrSearchJobParameters parameters)
    {
        var ctx = GetDb();
        var issue = await ctx.Issues.FindAsync(parameters.IssueId);
        var jobTitle = issue is not null
            ? $"Recherche Prowlarr — Issue #{issue.IssueNumber}"
            : $"Recherche Prowlarr — {parameters.IssueId}";

        var job = StartJob(jobTitle, parameters);
        job.SetState(JobState.RUNNING);

        try
        {
            if (issue is null)
            {
                EndJob(false);
                return [];
            }

            var volume = await ctx.Volumes.FindAsync(issue.VolumeId);
            if (volume is null)
            {
                EndJob(false);
                return [];
            }

            var prowlarr = GetService<ProwlarrService, ProwlarrOptions>();
            if (prowlarr.CurrentState.State != EState.OK)
            {
                JobSendTrace("[Prowlarr] Service non disponible", ETraceLevel.ERROR);
                EndJob(false);
                return [];
            }

            // Indexers : paramètre explicite ou sélection persistée
            int[]? indexerIds = parameters.IndexerIds;
            var saved = await ctx.SelectedIndexers.ToListAsync();
            if (indexerIds is null or { Length: 0 })
                indexerIds = saved.Count > 0 ? [.. saved.Select(s => s.IndexerId)] : null;

            var queries = BuildSearchQueries(volume, issue);
            job.CallbackHandler.UpdateTotal(queries.Count);

            JobSendTrace($"[Prowlarr] {queries.Count} requête(s) à tenter pour \"{volume.Title} #{issue.IssueNumber}\"");

            List<ScoredSearchResultTorrent> results = [];

            foreach (var query in queries)
            {
                JobSendTrace($"[Prowlarr] Recherche : {query}");
                var raw = await prowlarr.SearchAsync(query, indexerIds, ComputeCategories(saved, indexerIds), default);
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);

                if (raw.Count > 0)
                {
                    JobSendTrace($"[Prowlarr] {raw.Count} résultat(s) trouvé(s) avec \"{query}\"");
                    results = ScoringTorrent.ScoreAndSort(volume, issue, raw);
                    break;
                }

                JobSendTrace($"[Prowlarr] Aucun résultat pour \"{query}\", tentative suivante");
            }

            if (results.Count == 0)
                JobSendTrace("[Prowlarr] Aucun résultat sur aucune tentative", ETraceLevel.WARNING);

            EndJob(true);
            return results;
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Prowlarr] Erreur inattendue : {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
            return [];
        }
    }

    public async Task<Issue?> LaunchJobAnalyzeIssue(AnalyzeIssueJobParameters parameters)
    {
        var ctx = GetDb();
        var issue = await ctx.Issues.FindAsync(parameters.IssueId);
        var jobTitle = issue is not null
            ? $"Analyse CBZ — Issue #{issue.IssueNumber}"
            : $"Analyse CBZ — {parameters.IssueId}";

        var job = StartJob(jobTitle, parameters);
        job.SetState(JobState.RUNNING);

        try
        {
            if (issue is null || string.IsNullOrEmpty(issue.CbzFilename))
            {
                JobSendTrace("[Analyze] Issue introuvable ou sans fichier CBZ", ETraceLevel.ERROR);
                EndJob(false);
                return null;
            }

            var volume = await ctx.Volumes.FindAsync(issue.VolumeId);
            if (volume is null)
            {
                JobSendTrace("[Analyze] Volume introuvable", ETraceLevel.ERROR);
                EndJob(false);
                return null;
            }

            var library = await ctx.Libraries.FindAsync(volume.LibraryId);
            if (library is null)
            {
                JobSendTrace("[Analyze] Library introuvable", ETraceLevel.ERROR);
                EndJob(false);
                return null;
            }

            var archiveService = GetService<ArchiveService, ArchiveOption>();
            var kavitaService = GetService<KavitaService, KavitaOptions>();
            var scoringSettings = kavitaService.BuildScoringSettings();

            var cbzPath = ArchiveService.GetPath(issue, volume, library);
            if (!File.Exists(cbzPath))
            {
                JobSendTrace($"[Analyze] Fichier CBZ introuvable : {cbzPath}", ETraceLevel.ERROR);
                EndJob(false);
                return null;
            }

            JobSendTrace($"[Analyze] Calcul du hash SHA-256 de {Path.GetFileName(cbzPath)}");
            var hash = await ArchiveService.ComputeFileHashAsync(cbzPath);

            var progress = new Progress<CbzAnalysisProgress>(p =>
            {
                job.CallbackHandler.UpdateTotal(p.TotalEntries);
                job.CallbackHandler.Callback(new Progression { Total = p.TotalEntries, Completed = p.EntriesProcessed });
            });

            JobSendTrace($"[Analyze] Analyse de {Path.GetFileName(cbzPath)}");
            var analysis = await archiveService.AnalyzeCbzAsync(cbzPath, scoringSettings, progress);
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

            await ctx.SaveChangesAsync();
            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issue.Id));

            JobSendTrace($"[Analyze] Score {report.Score}/100 ({report.ScoreBand}) — {analysis.ImageEntryCount} page(s)");
            EndJob(true);
            return issue;
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Analyze] Erreur inattendue : {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
            return null;
        }
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
        Guid issueId,
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

        await qb.PauseTorrentAsync(hash, ct);
        await Task.Delay(500, ct); // Laisse QBittorrent traiter la mise en pause

        // Attente que QBittorrent charge les métadonnées du torrent (jusqu'à 10 s)
        List<QBittorrentTorrentFile> files = [];
        for (var attempt = 0; attempt < 10 && files.Count == 0; attempt++)
        {
            await Task.Delay(1000, ct);
            files = await qb.GetTorrentFilesAsync(hash, ct);
        }

        return (true, hash, files.Count > 0 ? files : null);
    }

    public async Task<bool> ApplyPackSelectionAsync(
        string torrentHash,
        Guid issueId,
        int[] selectedFileIndices,
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

        // Tenter de matcher les fichiers sélectionnés aux issues MISSING du volume
        var selectedFiles = allFiles.Where(f => selectedFileIndices.Contains(f.Index)).ToList();
        var triggerIssue  = await ctx.Issues.FindAsync([issueId], ct);
        var matchedIds    = new HashSet<Guid>();

        if (triggerIssue is not null)
        {
            var volumeIssues = await ctx.Issues
                .Where(i => i.VolumeId == triggerIssue.VolumeId && i.Status == IssueStatus.MISSING)
                .ToListAsync(ct);

            foreach (var file in selectedFiles)
            {
                var number  = TorrentTypeAnalyzer.ExtractIssueNumber(file.Name);
                var matched = number.HasValue
                    ? volumeIssues.FirstOrDefault(i => i.IssueNumber == number.Value && !matchedIds.Contains(i.Id))
                    : null;

                if (matched is null) continue;

                matched.Status = IssueStatus.DOWNLOADING;
                ctx.IssueDownloads.Add(new IssueDownload
                {
                    Id = Guid.NewGuid(), IssueId = matched.Id,
                    TorrentHash = torrentHash, Status = DownloadStatus.Unknown, AddedAt = DateTime.UtcNow
                });
                matchedIds.Add(matched.Id);
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(matched.Id));
            }
        }

        // Fallback : si aucun fichier n'a pu être associé, comportement standard (issue déclencheur)
        if (matchedIds.Count == 0)
        {
            var fallback = triggerIssue ?? await ctx.Issues.FindAsync([issueId], ct);
            if (fallback is not null) fallback.Status = IssueStatus.DOWNLOADING;
            ctx.IssueDownloads.Add(new IssueDownload
            {
                Id = Guid.NewGuid(), IssueId = issueId,
                TorrentHash = torrentHash, Status = DownloadStatus.Unknown, AddedAt = DateTime.UtcNow
            });
            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issueId));
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
            if (torrent is not null)
            {
                var alreadyOwnedByInkhound = dl.Status is DownloadStatus.Finished or DownloadStatus.Syncing or DownloadStatus.Done;
                var newStatus = MapQBittorrentState(torrent.State);
                if (!alreadyOwnedByInkhound && newStatus != dl.Status)
                {
                    dl.Status = newStatus;
                    dl.UpdatedAt = DateTime.UtcNow;
                }
            }

            result.Add(new DownloadItemData(dl, issue, volume, torrent));
        }

        await ctx.SaveChangesAsync(ct);
        return result;
    }

    private static DownloadStatus MapQBittorrentState(string state) => state.ToLowerInvariant() switch
    {
        "downloading" or "stalleddl" or "checkingdl" or "metadl" => DownloadStatus.Downloading,
        "pauseddl" => DownloadStatus.Paused,
        "uploading" or "stalledup" or "pausedup" or "checkingup" or "queuedup" => DownloadStatus.Finished,
        "error" or "missingfiles" => DownloadStatus.Error,
        _ => DownloadStatus.Unknown
    };

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

                    var matchedFile = archiveFiles.Count == 1
                        ? archiveFiles[0]
                        : archiveFiles.FirstOrDefault(f => TorrentTypeAnalyzer.ExtractIssueNumber(f.Name) == issue.IssueNumber);

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
                    volume.CountOfDownloadedIssues = await ctx.Issues.CountAsync(i => i.VolumeId == volume.Id && i.Status == IssueStatus.DOWNLOADED);
                    volume.UpdatedAt = DateTime.UtcNow;

                    download.Status = DownloadStatus.Done;
                    download.UpdatedAt = DateTime.UtcNow;

                    await ctx.SaveChangesAsync();
                    OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issue.Id));
                    OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));
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

    private static bool IsArchiveFile(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".cbz" or ".cbr" or ".pdf";
    }

    #endregion

    // Construit les requêtes du plus précis au plus général en croisant Volume + Issue
    private static List<string> BuildSearchQueries(Volume volume, Issue issue)
    {
        var title = volume.Title;
        var num = issue.IssueNumber;
        var year = issue.Year ?? volume.Year;
        var publisher = string.IsNullOrWhiteSpace(volume.Publisher) ? null : volume.Publisher;
        var issueTitle = string.IsNullOrWhiteSpace(issue.Title) ? null : issue.Title;

        List<string> candidates = [];

        // Niveau 1 : max — titre + numéro + éditeur + année + titre issue
        if (publisher is not null && year is not null && issueTitle is not null)
            candidates.Add($"{title} #{num} {publisher} ({year}) {issueTitle}");

        // Niveau 2 : titre + numéro + éditeur + année
        if (publisher is not null && year is not null)
            candidates.Add($"{title} #{num} {publisher} ({year})");

        // Niveau 3 : titre + numéro + année
        if (year is not null)
            candidates.Add($"{title} #{num} ({year})");

        // Niveau 4 : titre + numéro
        candidates.Add($"{title} #{num}");

        // Niveau 5 (fallback) : titre seul
        candidates.Add(title);

        // Déduplication en conservant l'ordre
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return candidates.Where(seen.Add).ToList();
    }

    #endregion
}
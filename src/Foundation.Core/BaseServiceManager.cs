
using System.Collections.Concurrent;
using System.Diagnostics;
using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Foundation.Core;

public abstract class BaseServiceManager
{
    protected readonly ConcurrentDictionary<Type, IService> Services = new();
    private CancellationTokenSource? _monitoringCts;
    private IProxyProviderService? _proxyProvider;

    public Action<StateServiceManager>? OnHealthcheck { get; set; }
    public Action<JobContext>? OnJobUpdated { get; set; }
    public Action<TraceDefinition>? OnTrace { get; set; }

    public Action<UpdatedData>? OnDataUpdated { get; set; }

    protected StateServiceManager CurrentState { get; set; } = new StateServiceManager();
    protected TimeSpan RefreshState = new TimeSpan(0, 0, 30);

    private static readonly AsyncLocal<JobContext> _currentJob = new();

    // Cache des jobs récents (actifs + terminés depuis peu), indexé par JobId — permet à un
    // client HTTP de rattraper un ManagerJobChanged manqué (déconnexion SignalR pendant
    // l'exécution du job, ex: app mobile mise en arrière-plan). Ne remplace pas SignalR : c'est
    // un filet de secours consulté uniquement à la resynchronisation (voir JobsController).
    // JobContext étant une classe, la référence stockée ici reflète automatiquement toute
    // mutation ultérieure (SetState, progression) sans hook supplémentaire.
    private readonly ConcurrentDictionary<Guid, JobContext> _recentJobs = new();

    // Caches mémoire détenus par le manager lui-même (les caches des services sont découverts via
    // IPurgeableCacheProvider). Purgés de leurs entrées expirées à chaque tick de monitoring.
    private readonly List<IPurgeableCache> _managedCaches = [];

    // Durée de conservation d'un job APRÈS complétion (SUCCESS/ERROR) avant purge. Volontairement
    // courte — contrairement à InkhoundManager._searchResults qui persiste les résultats sans
    // limite, ce cache ne vise qu'à couvrir une brève fenêtre de reconnexion, pas un historique.
    protected virtual TimeSpan JobRetention => TimeSpan.FromMinutes(15);

    // Plafond absolu, quel que soit l'état du job. Sans lui, un job qui meurt sans passer par
    // EndJob (exception avalée en amont, boucle interrompue) n'a ni état terminal ni EndDate : il
    // échappe à la purge ci-dessus et retient son JobContext — et tout ce que celui-ci référence —
    // pour la durée de vie du process.
    protected virtual TimeSpan JobHardRetention => TimeSpan.FromHours(6);

    public BaseServiceManager()
    {
        StartGlobalMonitoring();
    }

    public T GetService<T, K>() where K : IOptionList, new() where T : BaseService<K>, new()
    {

        if (Services.TryGetValue(typeof(T), out var service) && service is T typedService)
        {
            return typedService;
        }
        else
        {
            var newService = new T();
            newService.InitializeAction(GlobalTraceHandler, GlobalStateServiceHandler, GetActiveProxy, RequestProxyRotation);
            Services[typeof(T)] = newService;

            if (newService is IProxyProviderService proxyProvider)
                _proxyProvider = proxyProvider;

            return newService;
        }

    }

    private ProxyEndpoint? GetActiveProxy() => _proxyProvider?.CurrentProxy;

    private ProxyEndpoint? RequestProxyRotation()
    {
        JobSendTrace("Proxy rotation requested (possible IP ban)", ETraceLevel.WARNING);
        return _proxyProvider?.RotateToNext();
    }

    public List<OptionDefinition> GetServiceOptions(Type typeOfService)
    {
        IService? r;
        return Services.TryGetValue(typeOfService, out r) ? r.GetOptions() : new List<OptionDefinition>();
    }


    public void StartGlobalMonitoring()
    {
        // Annuler l'ancien avant d'en créer un neuf : sinon un second appel laisse la première
        // boucle PeriodicTimer tourner à vie (double healthcheck, double purge, et une boucle qui
        // survit à l'arrêt de l'host puisque StopMonitoring n'a alors plus de prise sur elle).
        var previous = _monitoringCts;
        _monitoringCts = new CancellationTokenSource();
        previous?.Cancel();
        previous?.Dispose();

        // On lance la tâche sur un thread de pool pour ne pas bloquer l'appelant
        Task.Run(async () => await MonitoringLoopAsync(RefreshState, _monitoringCts.Token));
    }

    /// <summary>
    /// Enregistre un cache détenu par le manager pour qu'il soit purgé de ses entrées expirées à
    /// chaque tick de monitoring, et vidé par <see cref="PurgeAllCaches"/>.
    /// </summary>
    protected void RegisterCache(IPurgeableCache cache)
    {
        lock (_managedCaches)
        {
            if (!_managedCaches.Contains(cache))
                _managedCaches.Add(cache);
        }
    }

    /// <summary>
    /// Tous les caches connus : ceux du manager et ceux exposés par les services.
    /// </summary>
    public IReadOnlyList<IPurgeableCache> GetAllCaches()
    {
        List<IPurgeableCache> all;
        lock (_managedCaches)
        {
            all = [.. _managedCaches];
        }

        foreach (var provider in Services.Values.OfType<IPurgeableCacheProvider>())
            all.AddRange(provider.GetPurgeableCaches());

        return all;
    }

    /// <summary>
    /// Vide intégralement tous les caches connus. Retourne le nombre total d'entrées retirées.
    /// </summary>
    public int PurgeAllCaches()
    {
        var removed = 0;
        foreach (var cache in GetAllCaches())
        {
            try
            {
                removed += cache.PurgeCache();
            }
            catch (Exception ex)
            {
                JobSendTrace($"Purge du cache {cache.CacheName} échouée : {ex.Message}", ETraceLevel.WARNING);
            }
        }
        return removed;
    }

    private void PurgeExpiredCacheEntries()
    {
        foreach (var cache in GetAllCaches())
        {
            try
            {
                cache.PurgeExpiredEntries();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de la purge du cache {cache.CacheName}: {ex.Message}");
            }
        }
    }

    private async Task MonitoringLoopAsync(TimeSpan delay, CancellationToken ct)
    {
        using PeriodicTimer timer = new(delay);

        try
        {
            do
            {
                CurrentState.Init();

                foreach (var key in Services.Keys)
                {
                    try
                    {
                        CurrentState.stateServices.Add(await Services[key].GetState());
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erreur lors du check de {key}: {ex.Message}");
                    }
                }

                CalculateGlobalState();
                OnHealthcheck?.Invoke(CurrentState);
                PurgeExpiredJobs();
                PurgeExpiredCacheEntries();
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // Arrêt propre
        }
    }

    protected virtual void CalculateGlobalState()
    {
        if (CurrentState.stateServices.Count == 0)
            CurrentState.GlobalState = EState.NOTINIT;
        else
            CurrentState.GlobalState = EState.OK;
    }

    public void StopMonitoring()
    {
        _monitoringCts?.Cancel();
    }

    public JobContext StartJob<T>(string title, T parameters) where T : IJobParameters
    {
        var job = new JobContext() { Title = title, OnUpdated = this.OnJobUpdated };
        _recentJobs[job.JobId] = job;
        OnJobUpdated?.Invoke(job);
        _currentJob.Value = job;

        if (!parameters.IsValid(out var errors))
        {
            var trace = new TraceDefinition() { JobId = job.JobId, ServiceName = "InkHound", Level = ETraceLevel.WARNING };
            trace.Message.AddRange(errors);
            GlobalTraceHandler(trace);
            EndJob(false);
        }

        return job;
    }

    public JobContext StartJob(string title)
    {
        var job = new JobContext() { Title = title, OnUpdated = this.OnJobUpdated };
        _recentJobs[job.JobId] = job;
        OnJobUpdated?.Invoke(job);
        _currentJob.Value = job;
        return job;
    }

    // Filet de rattrapage HTTP (voir JobsController) — état courant d'un job connu, y compris
    // juste après sa complétion (fenêtre JobRetention). Ne distingue pas un job jamais lancé d'un
    // job expiré : les deux renvoient null, pour éviter un registre permanent.
    public JobContext? TryGetJob(Guid jobId) => _recentJobs.TryGetValue(jobId, out var job) ? job : null;

    private void PurgeExpiredJobs()
    {
        var now = DateTime.UtcNow;
        foreach (var (id, job) in _recentJobs)
        {
            var isTerminal = job.State is JobState.SUCCESS or JobState.ERROR;
            if (isTerminal && job.EndDate.HasValue && now - job.EndDate.Value > JobRetention)
            {
                _recentJobs.TryRemove(id, out _);
                continue;
            }

            // Filet contre les jobs zombies — voir JobHardRetention.
            if (now - job.StartDate > JobHardRetention)
                _recentJobs.TryRemove(id, out _);
        }
    }

    public void JobSendTrace(string message, ETraceLevel level = ETraceLevel.INFO)
    {
        var trace = new TraceDefinition() { JobId = _currentJob.Value?.JobId ?? Guid.Empty, ServiceName = "InkHound", Level = level };
        trace.Message.Add(message);
        GlobalTraceHandler(trace);
    }

    // Trace de début/fin (avec durée) autour d'une étape longue d'un job (I/O, appel réseau, etc.),
    // pour que l'utilisateur voie que le job avance au lieu de sembler bloqué sans retour.
    public async Task<T> JobRunTimedAsync<T>(string operationName, Func<Task<T>> action)
    {
        JobSendTrace($"{operationName}…");
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action();
            JobSendTrace($"{operationName} — done in {TimeFormat.Elapsed(sw.Elapsed)}");
            return result;
        }
        catch (Exception ex)
        {
            JobSendTrace($"{operationName} — failed after {TimeFormat.Elapsed(sw.Elapsed)}: {ex.Message}", ETraceLevel.ERROR);
            throw;
        }
    }

    public Task JobRunTimedAsync(string operationName, Func<Task> action) =>
        JobRunTimedAsync(operationName, async () => { await action(); return true; });

    public T JobRunTimed<T>(string operationName, Func<T> action)
    {
        JobSendTrace($"{operationName}…");
        var sw = Stopwatch.StartNew();
        try
        {
            var result = action();
            JobSendTrace($"{operationName} — done in {TimeFormat.Elapsed(sw.Elapsed)}");
            return result;
        }
        catch (Exception ex)
        {
            JobSendTrace($"{operationName} — failed after {TimeFormat.Elapsed(sw.Elapsed)}: {ex.Message}", ETraceLevel.ERROR);
            throw;
        }
    }

    public void JobRunTimed(string operationName, Action action) =>
        JobRunTimed(operationName, () => { action(); return true; });

    public void EndJob(bool success = true)
    {
        if (_currentJob.Value != null)
        {
            _currentJob.Value.SetState(success ? JobState.SUCCESS : JobState.ERROR);
            OnJobUpdated?.Invoke(_currentJob.Value);
            _currentJob.Value = null;
        }
    }

    protected void GlobalTraceHandler(TraceDefinition trace)
    {
        // On récupère le job associé au contexte actuel pour enrichir le trace si besoin
        var job = _currentJob.Value;
        if (job != null)
        {
            trace.JobId = job.JobId;
        }
        OnTrace?.Invoke(trace);
    }
    protected void GlobalStateServiceHandler(StateService stateService)
    {
        CurrentState.stateServices.RemoveAll(s => s.ServiceName == stateService.ServiceName);
        CurrentState.stateServices.Add(stateService);
        CalculateGlobalState();
        OnHealthcheck?.Invoke(CurrentState);
    }
}


using System.Collections.Concurrent;
using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Foundation.Core;

public abstract class BaseServiceManager
{
    protected readonly ConcurrentDictionary<Type, IService> Services = new();
    private CancellationTokenSource? _monitoringCts;

    public Action<StateServiceManager>? OnHealthcheck { get; set; }
    public Action<JobContext>? OnJobUpdated { get; set; }
    public Action<TraceDefinition>? OnTrace { get; set; }

    protected StateServiceManager CurrentState { get; set; } = new StateServiceManager();
    protected TimeSpan RefreshState = new TimeSpan(0, 0, 30);

    private static readonly AsyncLocal<JobContext> _currentJob = new();

    public BaseServiceManager()
    {

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
            newService.InitializeAction(GlobalTraceHandler, GlobalStateServiceHandler);
            Services[typeof(T)] = newService;
            return newService;
        }

    }

    public List<OptionDefinition> GetServiceOptions(Type typeOfService)
    {
        IService? r;
        return Services.TryGetValue(typeOfService, out r) ? r.GetOptions() : new List<OptionDefinition>();
    }


    public void StartGlobalMonitoring()
    {
        _monitoringCts = new CancellationTokenSource();
        // On lance la tâche sur un thread de pool pour ne pas bloquer l'appelant
        Task.Run(async () => await MonitoringLoopAsync(RefreshState, _monitoringCts.Token));
    }

    private async Task MonitoringLoopAsync(TimeSpan delay, CancellationToken ct)
    {
        // Création d'un timer de 30 secondes
        using PeriodicTimer timer = new(delay);

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                // On boucle sur une copie des valeurs pour éviter les erreurs 
                // si le dictionnaire est modifié pendant l'itération

                CurrentState.Init();

                foreach (var key in Services.Keys)
                {
                    try
                    {
                        // Récupération de l'état (votre méthode d'interface)
                        CurrentState.stateServices.Add(await Services[key].GetState());
                    }
                    catch (Exception ex)
                    {
                        // On log l'erreur mais on continue pour les autres services
                        Console.WriteLine($"Erreur lors du check de {key}: {ex.Message}");
                    }
                }

            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt propre
        }

        CalculateGlobalState();
        OnHealthcheck?.Invoke(CurrentState);
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

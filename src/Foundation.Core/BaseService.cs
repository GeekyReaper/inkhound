using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Foundation.Core;

public abstract class BaseService<T> : IService where T : IOptionList, new()
{
    private Action<TraceDefinition>? _onTrace;
    private Action<StateService>? _onServiceStateUpdated;
    protected TimeSpan StateRefreshDelay { get; set; } = new TimeSpan(0);

    public StateService CurrentState { get; protected set; } = new StateService();

    protected readonly T Options = new();

    public virtual string GetServiceName() => "unknow";
    public virtual void InitializeAction(Action<TraceDefinition> onTraceAction, Action<StateService> onServiceStateUpdated)
    {
        _onTrace = onTraceAction;
        _onServiceStateUpdated = onServiceStateUpdated;
        SendTrace("Service initialized - State: " + CurrentState.State);
    }

    protected virtual void SendTrace(string title, Exception ex, TraceDefinition? trace = null)
    {
        var tracemessage = new List<string>();
        tracemessage.Add($"{title} -  {ex.GetType().Name}: {ex.Message}");
        if (ex.InnerException != null)
            tracemessage.Add($"Inner: {ex.InnerException.Message}");
        SendTrace(tracemessage, new TraceDefinition() { Level = ETraceLevel.ERROR });
    }
    protected virtual void SendTrace(string message, TraceDefinition? trace = null)
    {
        SendTrace(new List<string> { message }, trace);
    }

    protected virtual void SendTrace(string message, ETraceLevel level)
    {
        SendTrace(new List<string> { message }, new TraceDefinition() { Level = level });
    }

    protected virtual void SendTrace(List<string> messages, TraceDefinition? trace = null)
    {
        if (_onTrace == null)
        {
            return;
        }
        if (trace == null)
        {
            trace = new TraceDefinition() { Level = ETraceLevel.INFO };
        }
        trace.ServiceName = GetServiceName();
        trace.Message.AddRange(messages);
        _onTrace.Invoke(trace);
    }

    // Trace de début/fin (avec durée) autour d'une étape longue (I/O, appel réseau, etc.),
    // pour que le job appelant montre une vraie progression au lieu de sembler bloqué.
    protected async Task<TResult> RunTimedAsync<TResult>(string operationName, Func<Task<TResult>> action)
    {
        SendTrace($"{operationName}…");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await action();
            SendTrace($"{operationName} — done in {TimeFormat.Elapsed(sw.Elapsed)}");
            return result;
        }
        catch (Exception ex)
        {
            SendTrace($"{operationName} — failed after {TimeFormat.Elapsed(sw.Elapsed)}", ex);
            throw;
        }
    }

    protected Task RunTimedAsync(string operationName, Func<Task> action) =>
        RunTimedAsync(operationName, async () => { await action(); return true; });

    protected TResult RunTimed<TResult>(string operationName, Func<TResult> action)
    {
        SendTrace($"{operationName}…");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = action();
            SendTrace($"{operationName} — done in {TimeFormat.Elapsed(sw.Elapsed)}");
            return result;
        }
        catch (Exception ex)
        {
            SendTrace($"{operationName} — failed after {TimeFormat.Elapsed(sw.Elapsed)}", ex);
            throw;
        }
    }

    protected void RunTimed(string operationName, Action action) =>
        RunTimed(operationName, () => { action(); return true; });


    public virtual async Task<bool> LoadOptions(List<OptionDefinition> optionList)
    {
        Options.LoadOptions(optionList, out var optionErrors);

        CurrentState = await CalculateState();
        _onServiceStateUpdated?.Invoke(CurrentState);
        SendTrace("Options Loaded - State: " + CurrentState.State);
        return CurrentState.State == EState.OK;
    }

    public virtual async Task<StateService> GetState(bool force = false)
    {
        if (force || (CurrentState == null) || StateRefreshDelay.TotalMilliseconds == 0 || (DateTime.UtcNow - CurrentState.LastRefresh > StateRefreshDelay))
        {
            CurrentState = await CalculateState();
        }
        if (force)
        {
            _onServiceStateUpdated?.Invoke(CurrentState);
        }
        return CurrentState;
    }

    protected virtual async Task<StateService> CalculateState()
    {

        var s = new StateService() { ServiceName = GetServiceName() };
        if (!Options.IsValid(out _))
        {
            s.State = EState.INVALID;
        }
        else
        {
            s.State = await CheckInternalState();
        }

        return s;


    }

    /// <summary>
    /// Permet de faire des vérifications plus poussées sur l'état du service, comme par exemple vérifier la connectivité à une API distante, etc...
    /// </summary>
    /// <returns></returns>
    protected virtual async Task<EState> CheckInternalState()
    {
        return EState.OK;
    }

    public virtual List<OptionDefinition> GetOptions()
    {
        var result = Options.GetOptions();
        result.ForEach(o => o.ServiceName = GetServiceName());
        return result;
    }

}

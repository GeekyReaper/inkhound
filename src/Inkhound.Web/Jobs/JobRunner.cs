using Inkhound.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Inkhound.Web.Jobs;

/// <summary>
/// Singleton — enforces single-job exclusivity via SemaphoreSlim,
/// broadcasts job state and traces over SignalR.
/// </summary>
public class JobRunner(IHubContext<AppHub> hub, ILogger<JobRunner> logger)
{
    private readonly SemaphoreSlim      _sem  = new(1, 1);
    private CancellationTokenSource?    _cts;

    public JobContext? Current    { get; private set; }
    public bool        IsRunning  => Current?.Status == JobStatus.Running;

    // ── Start ───────────────────────────────────────────────────────────────────

    public async Task<bool> StartAsync(
        string jobType,
        string title,
        string requestedBy,
        Dictionary<string, string> parameters,
        Func<JobContext, CancellationToken, Task> work)
    {
        if (!await _sem.WaitAsync(0)) return false;    // already running

        _cts = new CancellationTokenSource();
        Current = new JobContext
        {
            JobType     = jobType,
            Title       = title,
            RequestedBy = requestedBy,
            Parameters  = parameters,
            Status      = JobStatus.Running
        };

        _ = Task.Run(() => ExecuteAsync(work, _cts.Token));
        return true;
    }

    // ── Cancel ──────────────────────────────────────────────────────────────────

    public async Task CancelAsync()
    {
        if (_cts is null || Current is null) return;
        await _cts.CancelAsync();
        await TraceAsync(TraceLevel.Cancel, "Cancellation requested.");
        await BroadcastJobAsync();
    }

    // ── Helpers for handlers ────────────────────────────────────────────────────

    public async Task TraceAsync(TraceLevel level, string message)
    {
        if (Current is null) return;
        var trace = new JobTrace(DateTime.UtcNow, level, message);
        Current.Traces.Add(trace);
        logger.LogInformation("[{Level}] {Message}", level, message);
        await hub.Clients.All.SendAsync("JobTrace", trace);
    }

    public async Task BroadcastJobAsync()
    {
        if (Current is null) return;
        await hub.Clients.All.SendAsync("JobChanged", Current);
    }

    // ── Execution ───────────────────────────────────────────────────────────────

    private async Task ExecuteAsync(Func<JobContext, CancellationToken, Task> work, CancellationToken ct)
    {
        try
        {
            await work(Current!, ct);
            Current!.Status  = JobStatus.Succeeded;
            Current.EndedAt  = DateTime.UtcNow;
            Current.Progress = 100;
            await TraceAsync(TraceLevel.Success, "Job completed successfully.");
        }
        catch (OperationCanceledException)
        {
            Current!.Status = JobStatus.Canceled;
            Current.EndedAt = DateTime.UtcNow;
            await TraceAsync(TraceLevel.Cancel, "Job was canceled.");
        }
        catch (Exception ex)
        {
            Current!.Status = JobStatus.Failed;
            Current.EndedAt = DateTime.UtcNow;
            logger.LogError(ex, "Job '{JobType}' failed: {Message}", Current.JobType, ex.Message);
            await TraceAsync(TraceLevel.Error, $"Job failed: {ex.Message}");
        }
        finally
        {
            await BroadcastJobAsync();
            _sem.Release();
        }
    }
}

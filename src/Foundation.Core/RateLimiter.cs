using System.Diagnostics;
using System.Threading.Channels;

namespace Foundation.Core;

/// <summary>
/// FIFO rate limiter: serializes async work items and enforces a minimum
/// interval between the START of consecutive executions.
/// Response/execution time does not count toward the interval — if an item
/// takes longer than <c>rateLimitMs</c>, the next one starts immediately.
/// </summary>
public sealed class RateLimiter : IDisposable
{
    private interface IWorkItem { Task ExecuteAsync(CancellationToken ct); }

    private sealed class WorkItem<T>(Func<CancellationToken, Task<T?>> work) : IWorkItem
    {
        private readonly TaskCompletionSource<T?> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T?> Result => _tcs.Task;

        public async Task ExecuteAsync(CancellationToken ct)
        {
            try { _tcs.SetResult(await work(ct)); }
            catch (Exception ex) { _tcs.SetException(ex); }
        }
    }

    private readonly Channel<IWorkItem> _channel = Channel.CreateUnbounded<IWorkItem>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly int _rateLimitMs;
    private readonly CancellationTokenSource _cts = new();

    public RateLimiter(int rateLimitMs)
    {
        _rateLimitMs = rateLimitMs;
        _ = Task.Run(ConsumeAsync);
    }

    private async Task ConsumeAsync()
    {
        var sw = new Stopwatch();
        var ct = _cts.Token;
        await foreach (var item in _channel.Reader.ReadAllAsync(ct))
        {
            if (sw.IsRunning)
            {
                var elapsed = sw.ElapsedMilliseconds;
                if (elapsed < _rateLimitMs)
                    await Task.Delay((int)(_rateLimitMs - elapsed), ct);
            }
            sw.Restart();
            await item.ExecuteAsync(ct);
        }
    }

    /// <summary>
    /// Enqueues <paramref name="work"/> and returns a task that completes
    /// when the work has been executed by the internal consumer.
    /// </summary>
    public Task<T?> EnqueueAsync<T>(Func<CancellationToken, Task<T?>> work)
    {
        var item = new WorkItem<T>(work);
        _channel.Writer.TryWrite(item);
        return item.Result;
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        _cts.Dispose();
    }
}

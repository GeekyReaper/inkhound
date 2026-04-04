using System.Reflection;
using Inkhound.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Inkhound.Web.State;

/// <summary>
/// Singleton — single source of truth for the platform state.
/// Handles full snapshots (on client connect) and partial patches (on mutations).
/// </summary>
public class PlatformStateService(IHubContext<AppHub> hub)
{
    private readonly PlatformState _state = new();

    // ── Read ────────────────────────────────────────────────────────────────────

    public PlatformState GetState() => _state;

    public StatePatch GetFullPatch() => BuildPatch("full", GetAllPropertyNames());

    // ── Sync — send a partial patch for specific properties ─────────────────────

    public async Task SyncAsync(params string[] propertyNames)
    {
        var patch = BuildPatch("partial", propertyNames);
        await hub.Clients.All.SendAsync("StateChanged", patch);
    }

    // ── Update + Sync — mutate and broadcast in one call ────────────────────────

    /// <summary>
    /// Updates state then broadcasts a partial patch.
    /// Usage: await state.UpdateAndSyncAsync(s => s.Status.Set("busy"), nameof(PlatformState.Status));
    /// </summary>
    public async Task UpdateAndSyncAsync(Action<PlatformState> mutate, params string[] propertyNames)
    {
        mutate(_state);
        await SyncAsync(propertyNames);
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private StatePatch BuildPatch(string patchType, IEnumerable<string> propertyNames)
        => new(patchType, BuildProps(propertyNames), DateTime.UtcNow);

    private Dictionary<string, TrackedValue> BuildProps(IEnumerable<string> propertyNames)
    {
        var dict = new Dictionary<string, TrackedValue>();
        foreach (var name in propertyNames)
        {
            var prop = typeof(PlatformState).GetProperty(name);
            if (prop?.GetValue(_state) is { } tracked)
            {
                var toDto = tracked.GetType().GetMethod("ToDto");
                if (toDto?.Invoke(tracked, null) is TrackedValue dto)
                    dict[name] = dto;
            }
        }
        return dict;
    }

    private static IEnumerable<string> GetAllPropertyNames()
        => typeof(PlatformState)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);
}

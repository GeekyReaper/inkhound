namespace Inkhound.Web.State;

/// <summary>Envelope for the SignalR "StateChanged" message.</summary>
public record StatePatch(
    string PatchType,                        // "full" | "partial"
    Dictionary<string, TrackedValue> Props,
    DateTime ServerTime
);

namespace Inkhound.Web.State;

/// <summary>
/// Global platform state. Add tracked properties here as features are implemented.
/// Each property is individually timestamped for efficient partial patching.
/// </summary>
public class PlatformState
{
    /// <summary>Overall application status.</summary>
    public TrackedProperty<string> Status { get; } = new("ready");

    /// <summary>Number of currently connected SignalR clients.</summary>
    public TrackedProperty<int> ConnectedUsers { get; } = new(0);
}

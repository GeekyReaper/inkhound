namespace Foundation.Core.Chatbot.Matrix.Models;

public sealed class SyncResponse
{
    public required string NextBatch { get; init; }

    /// <summary>Événements de timeline de l'unique room écoutée par le bot, si le sync en a rapporté.</summary>
    public IReadOnlyList<RoomEvent> RoomEvents { get; init; } = [];
}

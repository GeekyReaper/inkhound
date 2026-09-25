using System.Text.Json;

namespace Foundation.Core.Chatbot.Matrix.Models;

public sealed class RoomEvent
{
    public required string EventId { get; init; }
    public required string Sender { get; init; }
    public required string Type { get; init; }
    public long OriginServerTs { get; init; }
    public JsonElement Content { get; init; }

    public MessageEventContent AsMessageContent() => new(Content);
}

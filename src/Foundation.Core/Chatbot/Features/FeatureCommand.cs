using Foundation.Core.Chatbot.Matrix.Models;

namespace Foundation.Core.Chatbot.Features;

/// <summary>Une commande « !nom arg1 arg2 » analysée depuis un message de la room.</summary>
public sealed record FeatureCommand(
    string Name,
    IReadOnlyList<string> Args,
    string RawText,
    RoomEvent TriggerEvent,
    string? ReplyToEventId);

using Foundation.Core.Chatbot.Matrix.Models;

namespace Foundation.Core.Chatbot.Features;

public static class CommandParser
{
    /// <summary>Retourne null quand l'événement n'est pas un message texte de la forme « !commande ... ».</summary>
    public static FeatureCommand? TryParse(RoomEvent roomEvent)
    {
        if (roomEvent.Type != "m.room.message") return null;

        var content = roomEvent.AsMessageContent();
        // m.image autorise une légende (« !commande ») sur le même événement que l'image elle-même,
        // ce dont se servent les commandes d'analyse d'image pour router directement un traitement.
        if (content.MsgType != "m.text" && content.MsgType != "m.image") return null;

        var body = content.Body;
        if (string.IsNullOrWhiteSpace(body) || !body.StartsWith('!')) return null;

        var tokens = body[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;

        return new FeatureCommand(
            Name: tokens[0],
            Args: tokens[1..],
            RawText: body,
            TriggerEvent: roomEvent,
            ReplyToEventId: content.InReplyToEventId);
    }
}

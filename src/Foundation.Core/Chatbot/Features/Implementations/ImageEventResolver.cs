using Foundation.Core.Chatbot.Matrix;
using Foundation.Core.Chatbot.Matrix.Models;

namespace Foundation.Core.Chatbot.Features.Implementations;

/// <summary>
/// Résout l'événement image sur lequel une commande doit opérer, partagé par toutes les commandes
/// prenant une image en entrée : soit l'événement déclencheur est lui-même l'image (motif de la
/// légende, par exemple une image postée avec « !bd-scan » comme corps), soit l'image est
/// référencée par une réponse (m.relates_to.m.in_reply_to, via FeatureCommand.ReplyToEventId) ou
/// par son event id passé explicitement en premier argument — ces deux derniers cas nécessitant un
/// aller-retour par GetEventAsync.
/// </summary>
public static class ImageEventResolver
{
    public static async Task<(RoomEvent? ImageEvent, string? Error)> ResolveAsync(
        IMatrixClient matrixClient, FeatureCommand command, string roomId, CancellationToken ct)
    {
        if (command.TriggerEvent.AsMessageContent().MsgType == "m.image")
        {
            return (command.TriggerEvent, null);
        }

        var eventId = command.ReplyToEventId ?? command.Args.FirstOrDefault();
        if (eventId is null)
        {
            return (null, "doit être utilisé comme légende d'une image, ou en réponse (reply) à un message image.");
        }

        var imageEvent = await matrixClient.GetEventAsync(roomId, eventId, ct);
        var content = imageEvent.AsMessageContent();
        if (content.MsgType != "m.image" || content.Url is null)
        {
            return (null, $"L'event référencé ({eventId}) n'est pas une image.");
        }

        return (imageEvent, null);
    }
}

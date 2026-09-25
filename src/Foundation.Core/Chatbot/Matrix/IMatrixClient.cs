using Foundation.Core.Chatbot.Matrix.Models;

namespace Foundation.Core.Chatbot.Matrix;

public interface IMatrixClient
{
    /// <summary>Retourne l'identifiant Matrix du bot lui-même, et valide au passage le token configuré.</summary>
    Task<string> WhoAmIAsync(CancellationToken ct);

    /// <summary>Long-poll sur /sync ; ne retourne que les événements de la room configurée.</summary>
    Task<SyncResponse> SyncAsync(string? since, int timeoutMs, CancellationToken ct);

    Task<RoomEvent> GetEventAsync(string roomId, string eventId, CancellationToken ct);

    /// <summary>Envoie un m.room.message texte ; ajoute le formatage HTML quand htmlBody est fourni.</summary>
    Task<string> SendTextMessageAsync(string roomId, string plainText, string? htmlBody, CancellationToken ct);

    /// <summary>Envoie un m.room.message référençant un média déjà uploadé (msgtype "m.image" ou "m.file").</summary>
    Task<string> SendMediaMessageAsync(string roomId, string msgType, string body, string mxcUri, string mimeType, long size, CancellationToken ct);

    Task<MediaDownloadResult> DownloadMediaAsync(string mxcUri, CancellationToken ct);

    /// <summary>Uploade des octets bruts vers la media repository et retourne l'URI mxc:// obtenue.</summary>
    Task<string> UploadMediaAsync(Stream content, string contentType, string fileName, CancellationToken ct);
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundation.Core.Chatbot.Matrix.Models;

namespace Foundation.Core.Chatbot.Matrix;

/// <summary>
/// Wrapper HTTP mince sur la Client-Server API de Matrix. Il n'existe pas de SDK Matrix .NET mature
/// et digne de confiance : on parle donc directement à Synapse.
/// </summary>
/// <remarks>
/// Le <see cref="HttpClient"/> est fourni construit (BaseAddress = homeserver, en-tête Authorization
/// déjà positionné) par <c>BaseChatbotService</c> — pas de IHttpClientFactory ici, Foundation.Core
/// n'ayant aucune dépendance NuGet.
/// </remarks>
public sealed class MatrixClient(HttpClient http, string roomId, IChatTrace trace) : IMatrixClient
{
    public async Task<string> WhoAmIAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync("/_matrix/client/v3/account/whoami", ct);
        await EnsureSuccessAsync(response, ct);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("user_id").GetString()
            ?? throw new MatrixApiException(response.StatusCode, null, "La réponse whoami ne contient pas de user_id.");
    }

    public async Task<SyncResponse> SyncAsync(string? since, int timeoutMs, CancellationToken ct)
    {
        // On restreint le sync à l'unique room configurée pour garder une charge utile minimale.
        var filter = new JsonObject
        {
            ["room"] = new JsonObject
            {
                ["rooms"] = new JsonArray(roomId),
                ["timeline"] = new JsonObject { ["limit"] = 20 },
            },
        };

        var query = new StringBuilder($"/_matrix/client/v3/sync?timeout={timeoutMs}&filter={Uri.EscapeDataString(filter.ToJsonString())}");
        if (!string.IsNullOrEmpty(since))
        {
            query.Append("&since=").Append(Uri.EscapeDataString(since));
        }

        using var response = await http.GetAsync(query.ToString(), ct);
        await EnsureSuccessAsync(response, ct);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        var nextBatch = root.GetProperty("next_batch").GetString()
            ?? throw new MatrixApiException(response.StatusCode, null, "La réponse sync ne contient pas de next_batch.");

        var events = new List<RoomEvent>();
        if (root.TryGetProperty("rooms", out var rooms) &&
            rooms.TryGetProperty("join", out var joined) &&
            joined.TryGetProperty(roomId, out var room) &&
            room.TryGetProperty("timeline", out var timeline) &&
            timeline.TryGetProperty("events", out var timelineEvents))
        {
            foreach (var e in timelineEvents.EnumerateArray())
            {
                events.Add(ParseRoomEvent(e));
            }
        }

        return new SyncResponse { NextBatch = nextBatch, RoomEvents = events };
    }

    public async Task<RoomEvent> GetEventAsync(string targetRoomId, string eventId, CancellationToken ct)
    {
        var path = $"/_matrix/client/v3/rooms/{Uri.EscapeDataString(targetRoomId)}/event/{Uri.EscapeDataString(eventId)}";
        using var response = await http.GetAsync(path, ct);
        await EnsureSuccessAsync(response, ct);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return ParseRoomEvent(doc.RootElement);
    }

    public async Task<string> SendTextMessageAsync(string targetRoomId, string plainText, string? htmlBody, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["msgtype"] = "m.text",
            ["body"] = plainText,
        };

        if (htmlBody is not null)
        {
            body["format"] = "org.matrix.custom.html";
            body["formatted_body"] = htmlBody;
        }

        return await SendEventAsync(targetRoomId, body, ct);
    }

    public async Task<string> SendMediaMessageAsync(string targetRoomId, string msgType, string body, string mxcUri, string mimeType, long size, CancellationToken ct)
    {
        var content = new JsonObject
        {
            ["msgtype"] = msgType,
            ["body"] = body,
            ["url"] = mxcUri,
            ["info"] = new JsonObject
            {
                ["mimetype"] = mimeType,
                ["size"] = size,
            },
        };

        return await SendEventAsync(targetRoomId, content, ct);
    }

    public async Task<MediaDownloadResult> DownloadMediaAsync(string mxcUri, CancellationToken ct)
    {
        var mxc = MxcUri.Parse(mxcUri);
        // Depuis MSC3916 / Matrix v1.11, le téléchargement de média passe par l'endpoint client-v1 authentifié.
        var path = $"/_matrix/client/v1/media/download/{Uri.EscapeDataString(mxc.ServerName)}/{Uri.EscapeDataString(mxc.MediaId)}";

        using var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return new MediaDownloadResult
        {
            Bytes = bytes,
            ContentType = response.Content.Headers.ContentType?.MediaType,
            FileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName,
        };
    }

    public async Task<string> UploadMediaAsync(Stream content, string contentType, string fileName, CancellationToken ct)
    {
        var path = $"/_matrix/media/v3/upload?filename={Uri.EscapeDataString(fileName)}";

        using var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var response = await http.PostAsync(path, streamContent, ct);
        await EnsureSuccessAsync(response, ct);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("content_uri").GetString()
            ?? throw new MatrixApiException(response.StatusCode, null, "La réponse d'upload ne contient pas de content_uri.");
    }

    // L'identifiant de transaction rend l'envoi idempotent côté homeserver en cas de retransmission.
    private async Task<string> SendEventAsync(string targetRoomId, JsonObject content, CancellationToken ct)
    {
        var txnId = Guid.NewGuid().ToString("N");
        var path = $"/_matrix/client/v3/rooms/{Uri.EscapeDataString(targetRoomId)}/send/m.room.message/{txnId}";

        using var response = await http.PutAsJsonAsync(path, content, ct);
        await EnsureSuccessAsync(response, ct);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("event_id").GetString()
            ?? throw new MatrixApiException(response.StatusCode, null, "La réponse d'envoi ne contient pas d'event_id.");
    }

    private static RoomEvent ParseRoomEvent(JsonElement e) => new()
    {
        EventId = e.GetProperty("event_id").GetString() ?? "",
        Sender = e.GetProperty("sender").GetString() ?? "",
        Type = e.GetProperty("type").GetString() ?? "",
        OriginServerTs = e.TryGetProperty("origin_server_ts", out var ts) ? ts.GetInt64() : 0,
        Content = e.TryGetProperty("content", out var content) ? content.Clone() : default,
    };

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        string? errcode = null;
        string message = $"Appel API Matrix en échec, statut {(int)response.StatusCode} {response.StatusCode}.";
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            errcode = doc.RootElement.TryGetProperty("errcode", out var ec) ? ec.GetString() : null;
            var errmsg = doc.RootElement.TryGetProperty("error", out var em) ? em.GetString() : null;
            if (errmsg is not null)
            {
                message = $"{message} {errcode}: {errmsg}";
            }
        }
        catch (JsonException)
        {
            // Corps non-JSON (page d'erreur d'un reverse-proxy, par exemple) : on garde le message générique.
        }

        trace.Warn($"Erreur API Matrix : {message}");

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new MatrixApiException(response.StatusCode, errcode ?? "M_LIMIT_EXCEEDED", message);
        }

        throw new MatrixApiException(response.StatusCode, errcode, message);
    }
}

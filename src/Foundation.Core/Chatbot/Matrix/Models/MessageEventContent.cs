using System.Text.Json;

namespace Foundation.Core.Chatbot.Matrix.Models;

/// <summary>Accesseur en lecture seule sur le JSON brut du contenu d'un événement m.room.message.</summary>
public readonly struct MessageEventContent(JsonElement content)
{
    public string? MsgType => GetString("msgtype");
    public string? Body => GetString("body");
    public string? Url => GetString("url");

    public string? MimeType => TryGetProperty("info", out var info) && info.TryGetProperty("mimetype", out var v)
        ? v.GetString()
        : null;

    public long? Size => TryGetProperty("info", out var info) && info.TryGetProperty("size", out var v) && v.TryGetInt64(out var size)
        ? size
        : null;

    public int? Width => TryGetProperty("info", out var info) && info.TryGetProperty("w", out var v) && v.TryGetInt32(out var w)
        ? w
        : null;

    public int? Height => TryGetProperty("info", out var info) && info.TryGetProperty("h", out var v) && v.TryGetInt32(out var h)
        ? h
        : null;

    /// <summary>Identifiant de l'événement auquel ce message répond, le cas échéant (m.relates_to.m.in_reply_to.event_id).</summary>
    public string? InReplyToEventId
    {
        get
        {
            if (!TryGetProperty("m.relates_to", out var relatesTo)) return null;
            if (!relatesTo.TryGetProperty("m.in_reply_to", out var inReplyTo)) return null;
            return inReplyTo.TryGetProperty("event_id", out var eventId) ? eventId.GetString() : null;
        }
    }

    private bool TryGetProperty(string name, out JsonElement value)
    {
        if (content.ValueKind == JsonValueKind.Object && content.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private string? GetString(string name) => TryGetProperty(name, out var v) ? v.GetString() : null;
}

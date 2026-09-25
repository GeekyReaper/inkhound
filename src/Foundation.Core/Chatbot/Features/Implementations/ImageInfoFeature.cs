using System.Net;
using System.Text;

namespace Foundation.Core.Chatbot.Features.Implementations;

/// <summary>
/// Commande prenant une image en entrée. Un événement Matrix ne peut porter qu'un seul msgtype :
/// l'image est donc référencée soit en répondant à un message image par « !image-info », soit en
/// passant son event id explicitement : « !image-info $eventId:serveur ».
/// </summary>
public sealed class ImageInfoFeature(ChatbotContext context) : IChatFeature
{
    public string Name => "image-info";

    public string Description =>
        "Donne les informations de base d'une image. Répondez à un message image par !image-info, " +
        "ou passez son event id : !image-info $eventId";

    public async Task<FeatureExecutionResult> ExecuteAsync(FeatureCommand command, CancellationToken ct)
    {
        var (imageEvent, error) = await ImageEventResolver.ResolveAsync(context.Matrix, command, context.RoomId, ct);
        if (imageEvent is null)
        {
            return FeatureExecutionResult.Fail(error!);
        }

        var content = imageEvent.AsMessageContent();
        var media = await context.Matrix.DownloadMediaAsync(content.Url!, ct);
        var detectedFormat = DetectFormat(media.Bytes);

        var fileName = content.Body ?? "(inconnu)";
        var declaredMime = content.MimeType ?? "(inconnu)";
        var declaredSize = content.Size?.ToString() ?? "(inconnue)";
        var actualSize = media.Bytes.Length.ToString();
        var dimensions = content.Width is not null && content.Height is not null
            ? $"{content.Width}x{content.Height}"
            : "(inconnues)";

        var plain = $"""
            Nom du fichier : {fileName}
            Mimetype déclaré : {declaredMime}
            Format détecté : {detectedFormat}
            Dimensions : {dimensions}
            Taille déclarée : {declaredSize} octets
            Taille réelle : {actualSize} octets
            """;

        var html = new StringBuilder("<ul>")
            .Append($"<li>Nom du fichier : {Enc(fileName)}</li>")
            .Append($"<li>Mimetype déclaré : {Enc(declaredMime)}</li>")
            .Append($"<li>Format détecté : {Enc(detectedFormat)}</li>")
            .Append($"<li>Dimensions : {Enc(dimensions)}</li>")
            .Append($"<li>Taille déclarée : {Enc(declaredSize)} octets</li>")
            .Append($"<li>Taille réelle : {Enc(actualSize)} octets</li>")
            .Append("</ul>")
            .ToString();

        return FeatureExecutionResult.Ok(plain, html);
    }

    // Détection par nombre magique : le mimetype déclaré par le client est purement indicatif.
    private static string DetectFormat(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return "PNG";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "JPEG";
        }

        if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
        {
            return "GIF";
        }

        if (bytes.Length >= 12 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return "WEBP";
        }

        return "inconnu";
    }

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
}

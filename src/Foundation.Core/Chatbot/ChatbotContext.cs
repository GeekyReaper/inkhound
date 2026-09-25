using Foundation.Core.Chatbot.Features;
using Foundation.Core.Chatbot.Matrix;
using Foundation.Core.Chatbot.Vision;

namespace Foundation.Core.Chatbot;

/// <summary>
/// Composition root passée aux commandes. Remplace le conteneur d'injection de dépendances du
/// projet d'origine : Inkhound n'en utilise pas pour ses services métier, et les commandes sont
/// construites explicitement par <c>BaseChatbotService.CreateFeatures</c>.
/// </summary>
public sealed class ChatbotContext
{
    public required IMatrixClient Matrix { get; init; }
    public required VisionAnalysisService Vision { get; init; }
    public required PendingDisambiguationStore Disambiguation { get; init; }
    public required IChatTrace Trace { get; init; }

    /// <summary>Room unique écoutée par le bot.</summary>
    public required string RoomId { get; init; }

    /// <summary>Durée de vie d'un menu numéroté en attente.</summary>
    public TimeSpan PendingMenuTtl { get; init; } = PendingDisambiguationStore.DefaultTtl;

    /// <summary>Identifiant Matrix du bot, renseigné par /whoami au démarrage de la boucle.</summary>
    public string? BotUserId { get; internal set; }
}

using Foundation.Core.Chatbot.Vision;

namespace Foundation.Core.Chatbot;

/// <summary>
/// État d'exécution du bot, exposé à la couche Web pour la page dédiée. Purement en mémoire :
/// rien de tout cela n'est persisté.
/// </summary>
public sealed record ChatbotRuntimeStatus
{
    /// <summary>Le bot est-il configuré pour démarrer automatiquement au lancement de l'application.</summary>
    public required bool StartAtStartup { get; init; }
    public required bool Running { get; init; }
    public required string ServiceName { get; init; }
    public string? BotUserId { get; init; }
    public string? RoomId { get; init; }
    public string? HomeServerUrl { get; init; }
    public DateTime? LastSyncUtc { get; init; }
    public DateTime? StartedUtc { get; init; }

    /// <summary>Nombre d'événements de room traités depuis le démarrage de la boucle.</summary>
    public int HandledEventCount { get; init; }

    /// <summary>Nombre d'événements chiffrés reçus, qui ne peuvent pas être déchiffrés (pas de support E2EE).</summary>
    public int EncryptedEventCount { get; init; }

    /// <summary>Menus numérotés actuellement en attente d'une réponse.</summary>
    public int PendingMenuCount { get; init; }

    public string? VisionProvider { get; init; }
    public bool VisionConfigured { get; init; }
    public IReadOnlyList<VisionProviderInfo> VisionProviders { get; init; } = [];
    public UsageStatisticsSnapshot? VisionUsage { get; init; }
    public int VisionCacheEntries { get; init; }

    public IReadOnlyList<string> Commands { get; init; } = [];
}

namespace Foundation.Core.Chatbot.Features;

/// <summary>
/// Une capacité du chatbot, déclenchée par « !{Name} arg1 arg2 ... » dans la room.
/// Les implémentations sont exposées en les retournant depuis <c>BaseChatbotService.CreateFeatures</c>.
/// </summary>
public interface IChatFeature
{
    /// <summary>Nom de la commande sans le « ! » initial, par exemple « ping ».</summary>
    string Name { get; }

    string Description { get; }

    /// <summary>
    /// Avis facultatif envoyé immédiatement dans la room, avant l'exécution, pour les commandes dont
    /// le traitement peut durer (appel à une API externe lente). Retourner null pour les commandes
    /// rapides : aucun avis n'est alors envoyé.
    /// </summary>
    string? DescribeInProgress(FeatureCommand command) => null;

    Task<FeatureExecutionResult> ExecuteAsync(FeatureCommand command, CancellationToken ct);
}

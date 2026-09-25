namespace Foundation.Core.Chatbot.Features;

/// <summary>
/// Un menu en attente d'une réponse numérique d'un utilisateur donné dans une room donnée.
/// Générique : il ne sait rien de la commande qui l'a créé ni de ce qu'il résout — l'état de
/// l'assistant est porté par la closure <see cref="ResolveAsync"/>.
/// </summary>
public sealed record PendingDisambiguation(
    int CandidateCount,
    Func<int, CancellationToken, Task<FeatureExecutionResult>> ResolveAsync,
    DateTimeOffset ExpiresAt);

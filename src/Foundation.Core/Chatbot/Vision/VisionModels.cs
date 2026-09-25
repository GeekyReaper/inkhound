namespace Foundation.Core.Chatbot.Vision;

/// <summary>Image à analyser, accompagnée du prompt qui pilote entièrement la réponse du modèle.</summary>
public sealed record VisionRequest(byte[] Content, string MediaType, string Prompt);

/// <summary>
/// Résultat d'une analyse. Attention : <see cref="Success"/> reflète le succès de l'appel HTTP au
/// provider, pas la présence de JSON — <see cref="ExtractedJson"/> peut être null avec Success == true.
/// </summary>
public sealed record VisionResult(
    bool Success,
    string ProviderName,
    string? RawResponseText,
    string? ExtractedJson,
    string? ErrorMessage,
    TokenUsage? Usage = null,
    RateLimitSnapshot? RateLimit = null,
    bool FromCache = false);

public sealed record TokenUsage(int InputTokens, int OutputTokens)
{
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// Champs individuellement nullable : seuls certains providers (Anthropic) renvoient ces quotas
/// via des en-têtes HTTP ; Google ne les expose pas.
/// </summary>
public sealed record RateLimitSnapshot(
    int? RequestsLimit,
    int? RequestsRemaining,
    DateTimeOffset? RequestsReset,
    int? TokensLimit,
    int? TokensRemaining,
    DateTimeOffset? TokensReset);

/// <summary>
/// Limites déclarées par configuration, utilisées par <see cref="UsageStatisticsTracker"/> pour
/// estimer un quota restant quand le provider ne le renvoie pas lui-même (cas de Google).
/// </summary>
public sealed class UsageLimits
{
    public int? RequestsPerMinute { get; init; }
    public int? TokensPerMinute { get; init; }
    public int? RequestsPerDay { get; init; }
    public int? TokensPerDay { get; init; }
}

public sealed record UsageStatisticsSnapshot(
    int RequestsLastMinute,
    int TokensLastMinute,
    int RequestsLastDay,
    int TokensLastDay,
    int? RequestsPerMinuteRemaining,
    int? TokensPerMinuteRemaining,
    int? RequestsPerDayRemaining,
    int? TokensPerDayRemaining);

public sealed record VisionProviderInfo(string Name, bool IsActive, string Model);

namespace Foundation.Core.Chatbot.Vision.Providers;

/// <summary>
/// Réglages d'un provider vision. Remplace les <c>IOptions&lt;XxxProviderOptions&gt;</c> de DocuMind :
/// la configuration vient ici des options du module (table SQLite <c>Options</c>), pas d'un
/// <c>IConfiguration</c>, et elle est réappliquée à chaud via <c>Reconfigure</c>.
/// </summary>
public sealed record AnthropicVisionSettings
{
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "claude-sonnet-5";
    public int MaxTokens { get; init; } = 4096;
    public UsageLimits Limits { get; init; } = new();

    // Non exposés en options : aucune raison de laisser l'utilisateur les modifier.
    public const string BaseUrl = "https://api.anthropic.com/v1/messages";
    public const string ApiVersion = "2023-06-01";
}

/// <inheritdoc cref="AnthropicVisionSettings"/>
public sealed record GoogleVisionSettings
{
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "gemini-2.5-flash";
    public int MaxOutputTokens { get; init; } = 4096;
    public UsageLimits Limits { get; init; } = new();

    public const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
}

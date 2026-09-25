using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Foundation.Core.Chatbot;

/// <summary>
/// Socle d'options commun à tout chatbot Matrix : activation, connexion au homeserver et
/// configuration du modèle vision. Une classe dérivée ajoute ses propres options métier en
/// surchargeant <see cref="GetOptions"/> / <see cref="LoadOptions"/> / <see cref="IsValid"/> et en
/// appelant la base.
/// </summary>
public abstract class ChatbotOptionsBase : IOptionList
{
    public const string SectionGeneral = "General";
    public const string SectionMatrix = "Matrix";
    public const string SectionVision = "Vision";

    public const string ProviderGoogle = "Google";
    public const string ProviderAnthropic = "Anthropic";

    /// <summary>
    /// Démarre le bot automatiquement au lancement de l'application. À false, le module reste
    /// configuré (et son état continue de refléter la santé de ses dépendances), mais aucune
    /// connexion n'est ouverte tant que le démarrage n'est pas demandé depuis la page du module.
    /// </summary>
    public bool StartAtStartup { get; set; } = false;

    /// <summary>URL du homeserver Matrix, par exemple https://matrix.exemple.org.</summary>
    public string HomeServerUrl { get; set; } = string.Empty;

    /// <summary>Token d'accès du compte bot (obtenu par un login Matrix).</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Identifiant de l'unique room écoutée, de la forme !abc:serveur. Elle doit être NON chiffrée.</summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>Durée du long-poll /sync. Le homeserver retient la réponse jusqu'à ce délai s'il n'y a rien de neuf.</summary>
    public int SyncTimeoutSeconds { get; set; } = 30;

    /// <summary>Délai d'attente avant nouvelle tentative après une erreur de la boucle de sync.</summary>
    public int ErrorRetrySeconds { get; set; } = 5;

    /// <summary>Durée de vie d'un menu numéroté en attente de réponse.</summary>
    public int PendingMenuTtlMinutes { get; set; } = 10;

    /// <summary>Provider vision utilisé par défaut pour l'analyse d'images.</summary>
    public string VisionProvider { get; set; } = ProviderGoogle;

    public string GoogleApiKey { get; set; } = string.Empty;
    public string GoogleModel { get; set; } = "gemini-2.5-flash";
    public string AnthropicApiKey { get; set; } = string.Empty;
    public string AnthropicModel { get; set; } = "claude-sonnet-5";

    /// <summary>Plafond de tokens de la réponse, commun aux deux providers.</summary>
    public int VisionMaxTokens { get; set; } = 4096;

    public int VisionTimeoutSeconds { get; set; } = 100;
    public bool VisionCacheEnabled { get; set; } = true;
    public int VisionCacheMinutes { get; set; } = 60;

    /// <summary>Clé API du provider vision sélectionné, chaîne vide si aucune n'est renseignée.</summary>
    public string SelectedVisionApiKey => VisionProvider == ProviderAnthropic ? AnthropicApiKey : GoogleApiKey;

    /// <summary>
    /// La validation ne dépend <b>pas</b> de <see cref="StartAtStartup"/> : l'état du module répond à
    /// « la configuration est-elle exploitable ? », pas à « le bot tourne-t-il ? ». Un module non
    /// configuré est donc INVALID même désactivé, comme tout autre module d'Inkhound.
    /// </summary>
    public virtual bool IsValid(out List<string> errors)
    {
        errors = [];

        if (string.IsNullOrWhiteSpace(HomeServerUrl) ||
            !Uri.TryCreate(HomeServerUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            errors.Add($"{nameof(HomeServerUrl)} must be an absolute http(s) URL.");

        if (string.IsNullOrWhiteSpace(AccessToken))
            errors.Add($"{nameof(AccessToken)} is required when the chatbot is enabled.");

        if (string.IsNullOrWhiteSpace(RoomId) || !RoomId.StartsWith('!'))
            errors.Add($"{nameof(RoomId)} must be an internal room id starting with '!'.");

        if (SyncTimeoutSeconds < 1)
            errors.Add($"{nameof(SyncTimeoutSeconds)} must be at least 1.");

        if (ErrorRetrySeconds < 1)
            errors.Add($"{nameof(ErrorRetrySeconds)} must be at least 1.");

        if (PendingMenuTtlMinutes < 1)
            errors.Add($"{nameof(PendingMenuTtlMinutes)} must be at least 1.");

        // Sans clé, les commandes d'analyse d'image échoueraient à chaque appel.
        if (string.IsNullOrWhiteSpace(SelectedVisionApiKey))
            errors.Add($"An API key is required for the selected vision provider ({VisionProvider}).");

        return errors.Count == 0;
    }

    public virtual List<OptionDefinition> GetOptions() =>
    [
        new() { Name = nameof(StartAtStartup), Section = SectionGeneral, SortOrder = 0, Value = StartAtStartup.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "false", Description = "Start the Matrix bot automatically when the application starts. The bot can also be started and stopped manually from the Chatbot page.", Mandatory = false },

        new() { Name = nameof(HomeServerUrl), Section = SectionMatrix, SortOrder = 10, Value = HomeServerUrl, ValueType = EValueType.STRING, DefaultValue = "", Description = "Matrix homeserver URL, e.g. https://matrix.example.org.", Mandatory = false },
        new() { Name = nameof(AccessToken), Section = SectionMatrix, SortOrder = 20, Value = AccessToken, ValueType = EValueType.PASSWORD, DefaultValue = "", Description = "Access token of the bot account.", Mandatory = false },
        new() { Name = nameof(RoomId), Section = SectionMatrix, SortOrder = 30, Value = RoomId, ValueType = EValueType.STRING, DefaultValue = "", Description = "Internal room id (!abc:server) of the single room the bot listens to. The room MUST NOT be encrypted: this bot has no end-to-end encryption support, and clients such as Element create encrypted rooms by default.", Mandatory = false },
        new() { Name = nameof(SyncTimeoutSeconds), Section = SectionMatrix, SortOrder = 40, Value = SyncTimeoutSeconds.ToString(), ValueType = EValueType.INT, DefaultValue = "30", Description = "Long-poll duration of each /sync call.", Mandatory = false },
        new() { Name = nameof(ErrorRetrySeconds), Section = SectionMatrix, SortOrder = 50, Value = ErrorRetrySeconds.ToString(), ValueType = EValueType.INT, DefaultValue = "5", Description = "Delay before retrying after a sync loop error.", Mandatory = false },
        new() { Name = nameof(PendingMenuTtlMinutes), Section = SectionMatrix, SortOrder = 60, Value = PendingMenuTtlMinutes.ToString(), ValueType = EValueType.INT, DefaultValue = "10", Description = "How long a numbered menu waits for a reply before it is closed automatically.", Mandatory = false },

        new() { Name = nameof(VisionProvider), Section = SectionVision, SortOrder = 70, Value = VisionProvider, ValueType = EValueType.SELECT, DefaultValue = ProviderGoogle, AllowedValues = [ProviderGoogle, ProviderAnthropic], Description = "Vision LLM used to read images (cover OCR).", Mandatory = false },
        new() { Name = nameof(GoogleApiKey), Section = SectionVision, SortOrder = 80, Value = GoogleApiKey, ValueType = EValueType.PASSWORD, DefaultValue = "", Description = "Google AI Studio API key.", Mandatory = false },
        new() { Name = nameof(GoogleModel), Section = SectionVision, SortOrder = 90, Value = GoogleModel, ValueType = EValueType.STRING, DefaultValue = "gemini-2.5-flash", Description = "Google model id.", Mandatory = false },
        new() { Name = nameof(AnthropicApiKey), Section = SectionVision, SortOrder = 100, Value = AnthropicApiKey, ValueType = EValueType.PASSWORD, DefaultValue = "", Description = "Anthropic API key.", Mandatory = false },
        new() { Name = nameof(AnthropicModel), Section = SectionVision, SortOrder = 110, Value = AnthropicModel, ValueType = EValueType.STRING, DefaultValue = "claude-sonnet-5", Description = "Anthropic model id.", Mandatory = false },
        new() { Name = nameof(VisionMaxTokens), Section = SectionVision, SortOrder = 120, Value = VisionMaxTokens.ToString(), ValueType = EValueType.INT, DefaultValue = "4096", Description = "Maximum number of tokens in the model response (both providers).", Mandatory = false },
        new() { Name = nameof(VisionTimeoutSeconds), Section = SectionVision, SortOrder = 130, Value = VisionTimeoutSeconds.ToString(), ValueType = EValueType.INT, DefaultValue = "100", Description = "HTTP timeout of a vision analysis call.", Mandatory = false },
        new() { Name = nameof(VisionCacheEnabled), Section = SectionVision, SortOrder = 140, Value = VisionCacheEnabled.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "true", Description = "Cache analysis results in memory, keyed by image + prompt + provider.", Mandatory = false },
        new() { Name = nameof(VisionCacheMinutes), Section = SectionVision, SortOrder = 150, Value = VisionCacheMinutes.ToString(), ValueType = EValueType.INT, DefaultValue = "60", Description = "Lifetime of a cached analysis result.", Mandatory = false },
    ];

    public virtual bool LoadOptions(List<OptionDefinition> options, out List<string> errors)
    {
        errors = [];

        foreach (var option in options)
        {
            switch (option.Name)
            {
                case nameof(StartAtStartup): StartAtStartup = option.GetBool(); break;
                case nameof(HomeServerUrl): HomeServerUrl = option.Value; break;
                case nameof(AccessToken): AccessToken = option.Value; break;
                case nameof(RoomId): RoomId = option.Value; break;
                case nameof(SyncTimeoutSeconds): SyncTimeoutSeconds = option.GetInt(); break;
                case nameof(ErrorRetrySeconds): ErrorRetrySeconds = option.GetInt(); break;
                case nameof(PendingMenuTtlMinutes): PendingMenuTtlMinutes = option.GetInt(); break;
                case nameof(VisionProvider): VisionProvider = option.Value; break;
                case nameof(GoogleApiKey): GoogleApiKey = option.Value; break;
                case nameof(GoogleModel): GoogleModel = option.Value; break;
                case nameof(AnthropicApiKey): AnthropicApiKey = option.Value; break;
                case nameof(AnthropicModel): AnthropicModel = option.Value; break;
                case nameof(VisionMaxTokens): VisionMaxTokens = option.GetInt(); break;
                case nameof(VisionTimeoutSeconds): VisionTimeoutSeconds = option.GetInt(); break;
                case nameof(VisionCacheEnabled): VisionCacheEnabled = option.GetBool(); break;
                case nameof(VisionCacheMinutes): VisionCacheMinutes = option.GetInt(); break;
            }
        }

        return true;
    }
}

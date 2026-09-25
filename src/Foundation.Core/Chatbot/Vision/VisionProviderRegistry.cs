namespace Foundation.Core.Chatbot.Vision;

/// <summary>
/// Registre des providers vision. Contrairement à l'original DocuMind, la liste n'est pas
/// découverte par le conteneur DI : elle est fournie explicitement par le socle chatbot.
/// </summary>
public sealed class VisionProviderRegistry(IEnumerable<IVisionProvider> providers)
{
    private readonly IReadOnlyDictionary<string, IVisionProvider> _providersByName =
        providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    private string? _defaultProviderName;

    /// <summary>Provider utilisé quand l'appelant n'en nomme aucun. Rechargé à chaque LoadOptions.</summary>
    public void SetDefaultProvider(string? providerName) => _defaultProviderName = providerName;

    /// <summary>Vrai si au moins un provider dispose d'une clé API — sinon toute analyse échouera.</summary>
    public bool HasConfiguredProvider => _providersByName.Values.Any(p => p.IsConfigured);

    public IVisionProvider Resolve(string? providerName)
    {
        var requestedName = providerName ?? _defaultProviderName;

        if (requestedName is not null)
        {
            if (!_providersByName.TryGetValue(requestedName, out var provider))
            {
                throw new UnknownProviderException(requestedName);
            }

            if (!provider.IsConfigured)
            {
                throw new ProviderNotConfiguredException(provider.Name);
            }

            return provider;
        }

        return _providersByName.Values.FirstOrDefault(p => p.IsConfigured)
            ?? throw new ProviderNotConfiguredException("(aucun provider par défaut configuré)");
    }

    public IReadOnlyList<VisionProviderInfo> ListProviders() =>
        [.. _providersByName.Values
            .Select(p => new VisionProviderInfo(p.Name, p.IsConfigured, p.Model))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];

    public UsageStatisticsSnapshot GetUsageStatistics(string providerName) =>
        _providersByName.TryGetValue(providerName, out var provider)
            ? provider.GetUsageSnapshot()
            : throw new UnknownProviderException(providerName);
}

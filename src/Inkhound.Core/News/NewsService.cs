using Foundation.Core;
using Foundation.Core.Model;

namespace Inkhound.Core.News;

/// <summary>
/// Module News : porte les options et la source d'actualité (<see cref="INewsProvider"/>). Le job
/// de rafraîchissement et les lectures en base vivent dans <c>InkhoundManager</c> (partial News).
/// L'état reflète celui de la source dont dépend le provider (Bedetheque) — lu depuis son état en
/// cache, sans requête réseau supplémentaire.
/// </summary>
public class NewsService : BaseService<NewsOptions>
{
    private INewsProvider? _provider;
    private Func<StateService?>? _providerState;

    /// <inheritdoc />
    public override string GetServiceName() => "News";

    /// <summary>Source d'actualité active — <see cref="InvalidOperationException"/> si aucune n'est attachée.</summary>
    public INewsProvider Provider => _provider ?? throw new InvalidOperationException("No news provider attached.");

    /// <summary>Délai minimum (heures) entre deux relectures des pages liste.</summary>
    public int ListRefreshIntervalHours => Options.ListRefreshIntervalHours;

    /// <summary>Nombre d'échecs au-delà duquel un album n'est plus retenté.</summary>
    public int MaxEnrichAttempts => Options.MaxEnrichAttempts;

    /// <summary>
    /// Attache la source d'actualité et l'accès à l'état du service dont elle dépend. Appelé par
    /// <c>InkhoundManager.AutomaticLoadServices</c> avant le chargement des options.
    /// </summary>
    public NewsService Attach(INewsProvider provider, Func<StateService?> providerState)
    {
        _provider = provider;
        _providerState = providerState;
        return this;
    }

    protected override Task<EState> CheckInternalState()
    {
        if (_provider is null) return Task.FromResult(EState.ERROR);
        var state = _providerState?.Invoke()?.State ?? EState.NOTINIT;
        return Task.FromResult(state switch
        {
            EState.OK => EState.OK,
            EState.NOTINIT => EState.WARNING,
            _ => EState.ERROR
        });
    }
}

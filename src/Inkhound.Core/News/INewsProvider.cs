namespace Inkhound.Core.News;

/// <summary>
/// Source d'actualité BD : classement des ventes, nouvelles sorties, et enrichissement d'un album
/// (série de rattachement, auteurs, planches). L'implémentation actuelle est
/// <see cref="Bedetheque.BedethequeNewsProvider"/> ; toute requête réseau passe par la pile HTTP
/// (et les caches) de la source correspondante.
/// </summary>
public interface INewsProvider
{
    /// <summary>Clé de la source, identique à <c>ISourceService.SourceKey</c> (ex. "bedetheque").</summary>
    string ProviderKey { get; }

    /// <summary>Classement de la semaine <paramref name="week"/> (lundi), ou de la semaine en cours si <c>null</c>.</summary>
    Task<NewsTopSalesSnapshot?> FetchTopSalesAsync(DateOnly? week = null, CancellationToken ct = default);

    /// <summary>
    /// Nouvelles sorties du mois <paramref name="month"/> (1er jour du mois), ou de la fenêtre par
    /// défaut de la source si <c>null</c> (les derniers mois + le mois à venir).
    /// </summary>
    Task<IReadOnlyList<NewsScrapedEntry>> FetchReleasesAsync(DateOnly? month = null, CancellationToken ct = default);

    /// <summary>Détail complet d'un album (série de rattachement comprise), <c>null</c> si introuvable.</summary>
    Task<NewsAlbumEnrichment?> EnrichAlbumAsync(string albumId, CancellationToken ct = default);

    /// <summary>Fiche d'une série (liste des albums comprise), <c>null</c> si introuvable.</summary>
    Task<NewsSeriesDetail?> GetSeriesAsync(string seriesId, CancellationToken ct = default);
}

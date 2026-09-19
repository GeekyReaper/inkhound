namespace Inkhound.Core.Bedetheque.Catalog;

/// <summary>État global du catalogue local Bedetheque (page <c>/settings/bedetheque</c>).</summary>
/// <param name="Loaded">L'index mémoire contient au moins une série (la recherche est possible).</param>
/// <param name="TotalSeries">Nombre total de séries en base.</param>
/// <param name="OldestFetchUtc">Scraping le plus ancien parmi les lettres chargées.</param>
/// <param name="NewestFetchUtc">Scraping le plus récent parmi les lettres chargées.</param>
/// <param name="RefreshRunning">Un job de rafraîchissement est en cours.</param>
/// <param name="Letters">État des 27 lettres, dans l'ordre <c>0</c>, <c>A</c>…<c>Z</c>.</param>
public record BedethequeCatalogStatus(
    bool Loaded,
    int TotalSeries,
    DateTime? OldestFetchUtc,
    DateTime? NewestFetchUtc,
    bool RefreshRunning,
    IReadOnlyList<BedethequeCatalogLetterStatus> Letters);

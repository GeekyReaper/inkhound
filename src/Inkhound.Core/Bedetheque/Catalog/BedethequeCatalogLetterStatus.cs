namespace Inkhound.Core.Bedetheque.Catalog;

/// <summary>État d'une lettre de l'index du catalogue local.</summary>
/// <param name="Letter">Lettre d'index (<c>0</c>, <c>A</c>…<c>Z</c>).</param>
/// <param name="Count">Nombre de séries stockées pour cette lettre (0 si jamais chargée).</param>
/// <param name="FetchedAtUtc">Dernier scraping de la page, <c>null</c> si jamais chargée.</param>
public record BedethequeCatalogLetterStatus(string Letter, int Count, DateTime? FetchedAtUtc);

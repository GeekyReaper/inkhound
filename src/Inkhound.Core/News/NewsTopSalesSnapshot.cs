namespace Inkhound.Core.News;

/// <summary>Classement hebdomadaire des ventes : semaine (lundi) + entrées ordonnées par rang.</summary>
public record NewsTopSalesSnapshot(DateOnly Week, IReadOnlyList<NewsScrapedEntry> Entries);

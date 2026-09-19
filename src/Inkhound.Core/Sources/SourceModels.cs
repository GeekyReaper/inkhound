using Inkhound.Core.Models;

namespace Inkhound.Core.Sources;

public record SourceVolume(
    string SourceId,
    string Source,
    string Name,
    int? StartYear,
    int CountOfIssues,
    string? Publisher,
    string? Description,
    string? ImageUrl,
    string? SiteUrl,
    double Score = 0,
    string? Language = null);

public record SourceIssue(
    string SourceId,
    string Source,
    string? Name,
    string IssueNumber,
    DateTime? CoverDate,
    string? Description,
    string? ImageUrl,
    string? SiteUrl);

// Statistiques d'une source pour une recherche donnée — affichées dans la console du job et
// dans un résumé au-dessus des résultats, pour que l'utilisateur voie combien de résultats
// chaque source a renvoyés et en combien de temps. ErrorCode (optionnel) identifie un échec
// que l'UI sait traiter spécifiquement (ex. catalogue local à charger → lien vers la page).
public record SourceSearchStats(
    string Source,
    int ResultCount,
    long ElapsedMs,
    bool Success,
    string? ErrorMessage,
    string? ErrorCode = null)
{
    /// <summary>Le catalogue local Bedetheque est vide — l'utilisateur doit le charger.</summary>
    public const string CatalogNotLoaded = "CATALOG_NOT_LOADED";
}

public class SearchVolumesJobResult
{
    public Page<SourceVolume> Page { get; init; } = new();
    public List<SourceSearchStats> Stats { get; init; } = [];
}

using Inkhound.Core.Models;

namespace Inkhound.Core.Sources;

public interface ISourceService
{
    string SourceKey { get; }

    // Langue favorisée par la configuration de la source (ex. SearchLanguageFilter de Bedetheque),
    // au format de SourceVolume.Language ("Français", "Anglais", ...). null si la source n'en a
    // pas ou si aucun filtre n'est actif — utilisé par SearchScoring pour le bonus de langue.
    string? PreferredLanguage { get; }

    Task<Page<SourceVolume>> SearchVolumesByNameAsync(
        string query, int page = 1, int? limit = null, CancellationToken ct = default);

    Task<SourceVolume?> GetVolumeAsync(string sourceVolumeId, CancellationToken ct = default);

    Task<Page<SourceIssue>> GetIssuesPageAsync(
        string sourceVolumeId, int page = 1, int? limit = null, CancellationToken ct = default);

    Task<IReadOnlyList<SourceIssue>> GetAllIssuesForVolumeAsync(
        string sourceVolumeId, CancellationToken ct = default);

    Task<SourceIssue?> GetIssueAsync(string sourceIssueId, CancellationToken ct = default);
}

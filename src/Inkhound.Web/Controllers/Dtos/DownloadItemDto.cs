using Inkhound.Core.QBittorrent;

namespace Inkhound.Web.Controllers.Dtos;

/// <summary>
/// Ligne de téléchargement exposée au frontend. Partagée par la page Downloads
/// (<c>QBittorrentController</c>), la page Issue, la page Volume et le Dashboard — toutes affichent
/// la même ligne via le composant Angular <c>app-download-list</c>.
/// </summary>
public record DownloadItemDto(
    Guid Id,
    Guid IssueId,
    Guid? VolumeId,
    Guid? LibraryId,
    string TorrentHash,
    string TorrentTitle,
    string? TrackerName,
    string Status,
    DateTime AddedAt,
    DateTime? UpdatedAt,
    int? IssueNumber,
    string? IssueTitle,
    string? VolumeTitle,
    string? CoverUrl,
    double? Progress,
    long? Dlspeed,
    long? Eta,
    long? Size,
    int SharedWith)
{
    // TorrentTitle vient de l'item stocké (d.Download.TorrentTitle), pas d'une lecture live QBittorrent
    // (d.Torrent?.Name) : l'identité du download reste affichable même si QBittorrent ne retrouve plus
    // le torrent (hash orphelin, torrent supprimé...). Progress/Dlspeed/Eta/Size restent des indicateurs
    // live optionnels, absents quand QBittorrent ne retrouve pas le torrent.
    // VolumeId/LibraryId permettent au frontend de construire le lien vers la page de l'issue
    // (route library/:id/volume/:volumeId/issue/:issueId).
    public static DownloadItemDto From(DownloadItemData d) => new(
        d.Download.Id,
        d.Download.IssueId,
        d.Issue?.VolumeId,
        d.Volume?.LibraryId,
        d.Download.TorrentHash,
        d.Download.TorrentTitle,
        d.Download.TrackerName,
        d.Download.Status.ToString(),
        d.Download.AddedAt,
        d.Download.UpdatedAt,
        d.Issue?.IssueNumber,
        d.Issue?.Title,
        d.Volume?.Title,
        CoverOf(d),
        d.Torrent?.Progress,
        d.Torrent?.Dlspeed,
        d.Torrent?.Eta,
        d.Torrent?.Size,
        d.SharedWith);

    // Vignette de l'issue, à défaut celle du volume — les cartes du Dashboard affichent la
    // couverture plutôt qu'une simple ligne de texte. Null si aucune des deux n'en a.
    private static string? CoverOf(DownloadItemData d)
        => d.Issue?.Image?.SmallUrl ?? d.Issue?.Image?.ThumbUrl
           ?? d.Volume?.Image?.SmallUrl ?? d.Volume?.Image?.ThumbUrl;
}

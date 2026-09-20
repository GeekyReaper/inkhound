namespace Inkhound.Core.Models;

/// <summary>
/// Bannissement d'un torrent pour une issue : le couple (Issue, Torrent) est mémorisé quand
/// l'utilisateur supprime un téléchargement, pour que la recherche Prowlarr ne le repropose plus
/// (score forcé à 0 — voir <c>Scoring/TorrentBanIndex</c>). Visible et supprimable depuis la page
/// détail de l'issue.
/// </summary>
public class IssueTorrentBan
{
    public Guid Id { get; set; }

    /// <summary>Issue concernée — un ban ne vaut que pour elle (et, au scoring d'un volume, pour son volume).</summary>
    public Guid IssueId { get; set; }

    /// <summary>
    /// Hash du torrent tel que connu de qBittorrent. Conservé pour la traçabilité : un résultat
    /// Prowlarr n'expose pas de hash (il n'existe qu'après l'ajout du torrent), le rapprochement
    /// se fait donc en pratique par <see cref="DownloadUrl"/> puis par titre.
    /// </summary>
    public string TorrentHash { get; set; } = string.Empty;

    /// <summary>Titre de la release — affiché dans la liste des bans, et clé de repli du rapprochement.</summary>
    public string TorrentTitle { get; set; } = string.Empty;

    /// <summary>URL de téléchargement Prowlarr — clé de rapprochement principale.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Indexer d'origine, pour l'affichage.</summary>
    public string? TrackerName { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Origine du ban (« Download deleted », « Manual »…), pour l'affichage.</summary>
    public string? Reason { get; set; }
}

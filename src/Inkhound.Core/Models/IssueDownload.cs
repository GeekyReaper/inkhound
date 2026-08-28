namespace Inkhound.Core.Models;

public enum DownloadStatus { Downloading, Paused, Finished, Syncing, Done, Error, Unknown, NotFound, Stalled }

public class IssueDownload
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public string TorrentHash { get; set; } = string.Empty;
    public string TorrentTitle { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string? TrackerName { get; set; }
    // Nom du fichier d'archive du torrent retenu pour cette issue lors de la revue du PACK. Permet au
    // traitement d'utiliser exactement le fichier choisi, sans re-déduire l'appariement depuis le nom
    // (fragile pour les numéros atypiques : #0, hors-série…). Vide pour un grab direct / une ligne
    // créée avant l'ajout de ce champ (on retombe alors sur l'appariement par numéro).
    public string FileName { get; set; } = string.Empty;
    public DownloadStatus Status { get; set; } = DownloadStatus.Unknown;
    public DateTime AddedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

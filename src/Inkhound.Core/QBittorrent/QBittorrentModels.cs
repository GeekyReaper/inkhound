using System.Text.Json.Serialization;
using Inkhound.Core.Models;

namespace Inkhound.Core.QBittorrent;

public record QBittorrentCategory(string Name, string SavePath);

public record QBittorrentTorrent(
    string Hash,
    string Name,
    string State,
    double Progress,
    long Size,
    long Dlspeed,
    long Eta,
    [property: JsonPropertyName("added_on")] long AddedOn,
    [property: JsonPropertyName("num_complete")] int NumComplete = -1,
    [property: JsonPropertyName("num_seeds")] int NumSeeds = -1);

// État live du torrent d'un PACK pendant la revue des fichiers (avant validation de la sélection).
// MetadataReady : QBittorrent a chargé la liste des fichiers (métadonnées disponibles).
// NumComplete / NumSeeds : indicateurs de disponibilité des sources ; QBittorrent renvoie -1 quand
// l'info n'est pas connue (torrent en pause jamais annoncé).
public record PackFetchStatus(
    bool Found,
    string State,
    double Progress,
    int NumComplete,
    int NumSeeds,
    long Dlspeed,
    long Eta,
    bool MetadataReady,
    IReadOnlyList<QBittorrentTorrentFile> Files);

public record QBittorrentGrabParameters(string? Category, string? SavePath, bool AddPaused);

public record DownloadItemData(
    IssueDownload Download,
    Issue? Issue,
    Volume? Volume,
    QBittorrentTorrent? Torrent);

public record QBittorrentTorrentFile(int Index, string Name, long Size, double Progress, int Priority);

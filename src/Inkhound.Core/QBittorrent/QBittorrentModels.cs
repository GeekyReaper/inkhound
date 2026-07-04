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
    [property: JsonPropertyName("added_on")] long AddedOn);

public record QBittorrentGrabParameters(string? Category, string? SavePath, bool AddPaused);

public record DownloadItemData(
    IssueDownload Download,
    Issue? Issue,
    Volume? Volume,
    QBittorrentTorrent? Torrent);

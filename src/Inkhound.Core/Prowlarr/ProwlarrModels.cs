using System.Text.Json.Serialization;

namespace Inkhound.Core.Prowlarr;

public record ProwlarrCategory(int Id, string Name);
public record ProwlarrCapabilities(List<ProwlarrCategory> Categories);

public record ProwlarrIndexer(
    int Id,
    string Name,
    string Protocol,
    bool Enable,
    ProwlarrCapabilities? Capabilities = null);

public record ProwlarrSearchResult(
    string Title,
    string? InfoUrl,
    long Size,
    int Seeders,
    int Leechers,
    int ReleaseWeight,
    string Guid,
    int IndexerId,
    string? Indexer,
    string? Protocol,
    string? DownloadUrl,
    DateTime? PublishDate,
    List<ProwlarrCategory>? Categories = null);

public record ProwlarrGrab(
    string Guid,
    int IndexerId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DownloadClientId);

public record ProwlarrHistoryItem(int Id, string EventType, string SourceTitle, int IndexerId);

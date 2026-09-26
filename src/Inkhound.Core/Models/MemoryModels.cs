namespace Inkhound.Core.Models;

/// <summary>État d'un cache mémoire à un instant donné.</summary>
public sealed record CacheInfo(string Name, int EntryCount);

/// <summary>
/// Instantané de l'empreinte mémoire du process. Lecture pure — ne déclenche aucune collecte.
/// </summary>
/// <param name="WorkingSetBytes">
/// Mémoire résidente vue par l'OS (c'est la valeur que remonte <c>docker stats</c>) : elle inclut la
/// mémoire native de SkiaSharp/PDFium, invisible du GC.
/// </param>
/// <param name="ManagedHeapBytes">Tas managé tel que le GC le compte.</param>
/// <param name="GcHeapSizeBytes">Taille des segments du tas détenus par le GC.</param>
/// <param name="FragmentedBytes">Part de ces segments qui est libre mais non rendue à l'OS.</param>
/// <param name="TotalAvailableMemoryBytes">
/// Mémoire physique que le GC considère comme disponible. Sous Docker, c'est la limite du conteneur
/// (<c>mem_limit</c>) : si elle vaut la RAM de la machine entière, la limite n'est pas appliquée et
/// le GC n'a aucune raison de compacter.
/// </param>
/// <param name="IsServerGc">
/// true = un tas par cœur logique et des collectes tardives. Inkhound est configuré en Workstation
/// GC (voir Inkhound.Web.csproj) ; si cette valeur est true, le réglage n'a pas pris.
/// </param>
public sealed record MemorySnapshot(
    long WorkingSetBytes,
    long ManagedHeapBytes,
    long GcHeapSizeBytes,
    long FragmentedBytes,
    long TotalAvailableMemoryBytes,
    bool IsServerGc,
    bool IsConcurrentGc,
    int Gen0CollectionCount,
    int Gen1CollectionCount,
    int Gen2CollectionCount,
    List<CacheInfo> Caches);

/// <summary>Résultat d'une purge + compaction à la demande.</summary>
/// <param name="CacheEntriesRemoved">Nombre total d'entrées de cache vidées.</param>
/// <param name="BytesFreed">
/// Différence de working set avant/après. Peut être négative ou nulle : la compaction ne récupère
/// que le managé et le LOH fragmenté, jamais la mémoire native déjà libérée par les décodeurs.
/// </param>
public sealed record MemoryCompactionResult(
    MemorySnapshot Before,
    MemorySnapshot After,
    int CacheEntriesRemoved,
    long BytesFreed,
    double DurationSeconds);

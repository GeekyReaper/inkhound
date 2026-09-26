using System.Diagnostics;
using System.Runtime;
using Inkhound.Core.Models;

namespace Inkhound.Core;

/// <summary>
/// Observation et purge de l'empreinte mémoire du process — exposé par SystemController
/// (<c>GET /api/system/memory</c>, <c>POST /api/system/memory/compact</c>).
/// </summary>
public partial class InkhoundManager
{
    /// <summary>
    /// Instantané de la consommation mémoire. Lecture pure, aucune collecte déclenchée.
    /// </summary>
    public MemorySnapshot GetMemorySnapshot()
    {
        var gcInfo = GC.GetGCMemoryInfo();

        return new MemorySnapshot(
            WorkingSetBytes: Environment.WorkingSet,
            ManagedHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
            GcHeapSizeBytes: gcInfo.HeapSizeBytes,
            FragmentedBytes: gcInfo.FragmentedBytes,
            TotalAvailableMemoryBytes: gcInfo.TotalAvailableMemoryBytes,
            IsServerGc: GCSettings.IsServerGC,
            IsConcurrentGc: gcInfo.Concurrent,
            Gen0CollectionCount: GC.CollectionCount(0),
            Gen1CollectionCount: GC.CollectionCount(1),
            Gen2CollectionCount: GC.CollectionCount(2),
            Caches: [.. GetAllCaches().Select(c => new CacheInfo(c.CacheName, c.CachedEntryCount))]);
    }

    /// <summary>
    /// Vide tous les caches applicatifs puis force une collecte compactante, LOH inclus.
    /// </summary>
    /// <remarks>
    /// ⚠️ Ne récupère que la mémoire <b>managée</b>. Les pics de décodage d'image (SkiaSharp,
    /// PDFium) sont alloués en mémoire native : un GC.Collect ne les voit pas. C'est pour cette
    /// raison que la conversion d'images est sérialisée et que le pool d'ImageSharp est plafonné
    /// (voir ArchiveService.ImageDecodeGate et CbzQuality.ImageProcessingSetup) — la purge est un
    /// complément, pas le mécanisme principal.
    ///
    /// Opération volontairement bloquante et coûteuse (de l'ordre de la seconde sur un gros tas) :
    /// déclenchée à la main depuis la page System, jamais sur un chemin automatique.
    /// </remarks>
    public async Task<MemoryCompactionResult> CompactMemoryAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var before = GetMemorySnapshot();

        var removed = PurgeAllCaches();
        JobSendTrace($"[Memory] {removed} entrée(s) de cache purgée(s)");

        // Compacter le LOH est un réglage à usage unique : il ne vaut que pour la collecte suivante.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        // Second passage : récupère ce que les finaliseurs viennent de rendre collectable.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Laisse à l'OS le temps de refléter la restitution des segments dans le working set, sinon
        // la mesure « après » est prise avant que le RSS ait bougé.
        await Task.Delay(250);

        var after = GetMemorySnapshot();
        stopwatch.Stop();

        var freed = before.WorkingSetBytes - after.WorkingSetBytes;
        JobSendTrace($"[Memory] Compaction terminée en {stopwatch.Elapsed.TotalSeconds:F1}s — " +
                     $"working set {before.WorkingSetBytes / 1024 / 1024} Mo → {after.WorkingSetBytes / 1024 / 1024} Mo");

        return new MemoryCompactionResult(before, after, removed, freed, stopwatch.Elapsed.TotalSeconds);
    }
}

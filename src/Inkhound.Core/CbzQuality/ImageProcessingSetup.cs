using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Memory;

namespace Inkhound.Core.CbzQuality;

/// <summary>
/// Réglages globaux des bibliothèques de traitement d'image, à appliquer une seule fois au
/// démarrage du process (avant tout décodage).
/// </summary>
public static class ImageProcessingSetup
{
    /// <summary>
    /// Plafond du pool de buffers d'ImageSharp, en Mo.
    /// </summary>
    /// <remarks>
    /// Par défaut, ImageSharp conserve ses buffers dans un <see cref="MemoryAllocator"/> poolé qui
    /// ne rend jamais ses blocs à l'OS : une seule analyse CBZ d'intégrale (des centaines de pages
    /// décodées) fixait durablement plusieurs centaines de Mo de RSS du conteneur. Le plafond borne
    /// ce pool ; au-delà, ImageSharp alloue sur le tas managé, que le GC peut récupérer.
    /// </remarks>
    private const int AllocationLimitMegabytes = 128;

    /// <summary>
    /// Borne le pool mémoire d'ImageSharp. Idempotent, à appeler au tout début du démarrage.
    /// </summary>
    public static void Configure()
    {
        Configuration.Default.MemoryAllocator =
            MemoryAllocator.Create(new MemoryAllocatorOptions { AllocationLimitMegabytes = AllocationLimitMegabytes });
    }
}

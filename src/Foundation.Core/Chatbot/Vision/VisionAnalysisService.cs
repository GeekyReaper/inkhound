namespace Foundation.Core.Chatbot.Vision;

/// <summary>
/// Façade d'analyse d'image. Version réduite de l'<c>ImageAnalysisService</c> de DocuMind : l'image
/// provient toujours de la media repository Matrix sous forme d'octets, donc les variantes
/// URL/chemin de fichier et toute la validation associée (anti-SSRF, path traversal, sniffing MIME)
/// n'ont pas d'objet ici.
/// </summary>
public sealed class VisionAnalysisService(VisionProviderRegistry registry, VisionAnalysisCache cache, IChatTrace trace)
{
    public VisionProviderRegistry Providers => registry;

    public bool HasConfiguredProvider => registry.HasConfiguredProvider;

    public async Task<VisionResult> AnalyzeAsync(
        byte[] content,
        string mediaType,
        string prompt,
        string? providerName = null,
        CancellationToken cancellationToken = default)
    {
        var provider = registry.Resolve(providerName);

        if (cache.TryGet(content, mediaType, prompt, provider.Name, out var cached))
        {
            trace.Debug($"Analyse vision servie depuis le cache ({provider.Name}).");
            return cached!;
        }

        var result = await provider.AnalyzeAsync(new VisionRequest(content, mediaType, prompt), cancellationToken);
        cache.Set(content, mediaType, prompt, provider.Name, result);

        if (!result.Success)
        {
            trace.Warn($"Analyse vision en échec ({provider.Name}) : {result.ErrorMessage}");
        }

        return result;
    }
}

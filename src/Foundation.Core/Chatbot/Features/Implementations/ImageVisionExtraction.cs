using Foundation.Core.Chatbot.Matrix;
using Foundation.Core.Chatbot.Matrix.Models;
using Foundation.Core.Chatbot.Vision;

namespace Foundation.Core.Chatbot.Features.Implementations;

/// <summary>Issue d'une tentative d'exécution d'un prompt vision sur l'image résolue.</summary>
public sealed record ImageExtractionAttempt(bool ImageValid, VisionResult? Result, string? Error);

/// <summary>
/// Résout l'image (légende ou réponse, via <see cref="ImageEventResolver"/>), la télécharge et
/// appelle le modèle vision, avec quelques tentatives supplémentaires sur les erreurs transitoires
/// du provider. Partagé par <see cref="ImageAnalysisFeatureBase"/> (commandes d'analyse simples) et
/// par les commandes qui ont besoin du résultat brut pour le post-traiter plutôt que de le formater
/// directement.
/// </summary>
public static class ImageVisionExtraction
{
    // Google renvoie occasionnellement 503 UNAVAILABLE / 429 RESOURCE_EXHAUSTED en charge ;
    // les deux sont transitoires et méritent deux tentatives avant d'abandonner.
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    public static async Task<ImageExtractionAttempt> RunAsync(
        IMatrixClient matrixClient, VisionAnalysisService vision,
        FeatureCommand command, string roomId, string prompt, CancellationToken ct)
    {
        RoomEvent? imageEvent;
        string? error;
        try
        {
            (imageEvent, error) = await ImageEventResolver.ResolveAsync(matrixClient, command, roomId, ct);
        }
        catch (MatrixApiException ex)
        {
            return new ImageExtractionAttempt(true, null, $"Erreur Matrix lors de la résolution de l'image : {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return new ImageExtractionAttempt(true, null, $"Impossible de contacter le serveur Matrix pour résoudre l'image : {ex.Message}");
        }

        if (imageEvent is null)
        {
            return new ImageExtractionAttempt(false, null, error);
        }

        var content = imageEvent.AsMessageContent();

        MediaDownloadResult media;
        try
        {
            media = await matrixClient.DownloadMediaAsync(content.Url!, ct);
        }
        catch (MatrixApiException ex)
        {
            return new ImageExtractionAttempt(true, null, $"Erreur lors du téléchargement de l'image (Matrix) : {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return new ImageExtractionAttempt(true, null, $"Impossible de contacter le serveur Matrix pour télécharger l'image : {ex.Message}");
        }

        var mediaType = content.MimeType ?? media.ContentType ?? "image/png";

        try
        {
            var result = await AnalyzeWithRetryAsync(vision, media.Bytes, mediaType, prompt, ct);
            return new ImageExtractionAttempt(true, result, null);
        }
        catch (ProviderNotConfiguredException ex)
        {
            return new ImageExtractionAttempt(true, null, $"Analyse d'image indisponible : {ex.Message}");
        }
        catch (UnknownProviderException ex)
        {
            return new ImageExtractionAttempt(true, null, $"Analyse d'image indisponible : {ex.Message}");
        }
        catch (VisionAnalysisException ex)
        {
            return new ImageExtractionAttempt(true, null, $"Erreur du modèle vision lors de l'analyse d'image : {ex.Message}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ImageExtractionAttempt(true, null, "L'analyse d'image a dépassé le délai imparti (timeout).");
        }
    }

    private static async Task<VisionResult> AnalyzeWithRetryAsync(
        VisionAnalysisService vision, byte[] bytes, string mediaType, string prompt, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await vision.AnalyzeAsync(bytes, mediaType, prompt, cancellationToken: ct);
            }
            catch (VisionAnalysisException ex) when (attempt < RetryDelays.Length && IsTransient(ex))
            {
                await Task.Delay(RetryDelays[attempt], ct);
            }
        }
    }

    private static bool IsTransient(VisionAnalysisException ex) =>
        ex.Message.Contains("503") || ex.Message.Contains("429") ||
        ex.Message.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase);
}

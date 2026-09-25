namespace Foundation.Core.Chatbot.Vision;

/// <summary>
/// Fournisseur d'analyse d'image par modèle de langage multimodal.
/// </summary>
/// <remarks>
/// Les implémentations sont instanciées <b>une seule fois</b> par <c>BaseChatbotService</c> et
/// reconfigurées via <c>Reconfigure</c> à chaque rechargement d'options : elles portent leur
/// <see cref="UsageStatisticsTracker"/> en champ privé, qui doit survivre aux sauvegardes faites
/// depuis la page Modules.
/// </remarks>
public interface IVisionProvider
{
    string Name { get; }

    /// <summary>Vrai si une clé API non vide est configurée pour ce provider.</summary>
    bool IsConfigured { get; }

    string Model { get; }

    Task<VisionResult> AnalyzeAsync(VisionRequest request, CancellationToken cancellationToken = default);

    UsageStatisticsSnapshot GetUsageSnapshot();
}

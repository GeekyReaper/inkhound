using Foundation.Core.Chatbot;
using Foundation.Core.Chatbot.Features.Implementations;

namespace Inkhound.Core.Chatbot.Features;

/// <summary>
/// Analyse une couverture et renvoie le JSON détecté, sans recherche ni ajout. Le prompt étant
/// spécifique à la BD, cette commande vit dans la couche métier et non dans le socle générique.
/// </summary>
public sealed class OcrBdFeature(ChatbotContext context) : ImageAnalysisFeatureBase(context)
{
    public override string Name => "ocr-bd";

    public override string Description =>
        "Identifie une couverture de BD/Manga/Comics. Usage : envoyer une image avec la légende « !ocr-bd », " +
        "ou répondre à un message image avec « !ocr-bd ».";

    protected override string Prompt => BdCoverAnalysis.Prompt;
}

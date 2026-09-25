using System.Net;

namespace Foundation.Core.Chatbot.Features.Implementations;

/// <summary>
/// Base des commandes déclenchées par une image, soit en légende (une image postée avec « !ocr-bd »
/// comme légende), soit en répondant à une image déjà postée. Gère la résolution et le
/// téléchargement de l'image puis l'appel au modèle vision ; les classes dérivées ne fournissent
/// que le prompt d'analyse.
/// </summary>
public abstract class ImageAnalysisFeatureBase(ChatbotContext context) : IChatFeature
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    protected abstract string Prompt { get; }

    protected ChatbotContext Context { get; } = context;

    public async Task<FeatureExecutionResult> ExecuteAsync(FeatureCommand command, CancellationToken ct)
    {
        var attempt = await ImageVisionExtraction.RunAsync(
            Context.Matrix, Context.Vision, command, Context.RoomId, Prompt, ct);

        if (!attempt.ImageValid)
        {
            return FeatureExecutionResult.Fail($"!{Name} {attempt.Error}");
        }

        if (attempt.Result is null)
        {
            return FeatureExecutionResult.Fail(attempt.Error!);
        }

        if (!attempt.Result.Success)
        {
            return FeatureExecutionResult.Fail($"Échec de l'analyse : {attempt.Result.ErrorMessage}");
        }

        var json = attempt.Result.ExtractedJson ?? "(vide)";
        var html = $"<pre><code>{WebUtility.HtmlEncode(json)}</code></pre>";
        return FeatureExecutionResult.Ok(json, html);
    }
}

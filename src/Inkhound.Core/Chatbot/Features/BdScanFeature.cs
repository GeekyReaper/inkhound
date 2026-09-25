using System.Net;
using System.Text.Json;
using Foundation.Core.Chatbot;
using Foundation.Core.Chatbot.Features;
using Foundation.Core.Chatbot.Features.Implementations;
using Inkhound.Core.Chatbot.Services;

namespace Inkhound.Core.Chatbot.Features;

/// <summary>
/// Lit une couverture de BD/Manga/Comics via le modèle vision puis recherche la série dans
/// Inkhound. Étapes propres à <c>!bd-scan</c> : analyse en cours → éléments détectés → recherche en
/// cours → lancement de la recherche (avec titre d'album et numéro détectés comme filtres). La
/// présentation du résultat et l'assistant d'ajout sont délégués à <see cref="BdSeriesResultFlow"/>,
/// partagé avec <c>!bd-search</c>.
/// </summary>
public sealed class BdScanFeature(
    ChatbotContext context,
    BdSeriesLookupService lookupService,
    BdSeriesResultFlow resultFlow) : IChatFeature
{
    public string Name => "bd-scan";

    public string Description =>
        "Identifie une couverture de BD/Manga/Comics puis recherche automatiquement la série dans Inkhound. " +
        "Usage : envoyer une image avec la légende « !bd-scan », ou répondre à un message image avec « !bd-scan ».";

    // Lancement de la demande, posté par le socle avant l'exécution. L'annulation n'est annoncée
    // qu'ici ; les menus suivants ne la rappellent pas.
    public string? DescribeInProgress(FeatureCommand command) =>
        "🚀 Demande lancée — tapez « a » à tout moment pour l'annuler.\n🔍 Analyse de la couverture en cours…";

    public async Task<FeatureExecutionResult> ExecuteAsync(FeatureCommand command, CancellationToken ct)
    {
        var attempt = await ImageVisionExtraction.RunAsync(
            context.Matrix, context.Vision, command, context.RoomId, BdCoverAnalysis.Prompt, ct);

        if (!attempt.ImageValid)
        {
            return RequestClosure.Closed($"!{Name} {attempt.Error}", success: false);
        }

        if (attempt.Result is null)
        {
            return RequestClosure.Closed(attempt.Error!, success: false);
        }

        if (!attempt.Result.Success)
        {
            return RequestClosure.Closed($"L'OCR n'a pas pu identifier la couverture : {attempt.Result.ErrorMessage}", success: false);
        }

        // Le succès de l'appel ne garantit pas que du JSON ait été extrait de la réponse.
        if (string.IsNullOrWhiteSpace(attempt.Result.ExtractedJson))
        {
            return RequestClosure.Closed("Résultat OCR invalide (aucun JSON exploitable dans la réponse).", success: false);
        }

        BdCoverAnalysis.Detection? detection;
        try
        {
            detection = JsonSerializer.Deserialize<BdCoverAnalysis.Detection>(
                attempt.Result.ExtractedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return RequestClosure.Closed("Résultat OCR invalide (JSON non exploitable).", success: false);
        }

        if (string.IsNullOrWhiteSpace(detection?.Serie))
        {
            return RequestClosure.Closed("L'OCR n'a pas identifié de nom de série sur cette couverture.", success: false);
        }

        var (detPlain, detHtml) = FormatDetection(detection);
        await context.Matrix.SendTextMessageAsync(context.RoomId, detPlain, detHtml, ct);
        await context.Matrix.SendTextMessageAsync(context.RoomId, "🔎 Recherche de la série sur Inkhound…", null, ct);

        var outcome = await lookupService.SearchAsync(
            detection.Serie, album: detection.Titre, numeroAlbum: detection.Numero, ct);

        var userId = command.TriggerEvent.Sender;
        var hint = new AlbumHint(detection.Numero, detection.Titre);

        return outcome switch
        {
            BdLookupOutcome.Resolved resolved =>
                await resultFlow.DisplayResultAndOfferAsync(resolved.Detail, resolved.Candidates, hint, userId, ct),
            // Pas de menu de désambiguïsation séparé : on retient le 1er candidat par défaut, le
            // menu des autres résultats permet ensuite d'en changer.
            BdLookupOutcome.Ambiguous ambiguous =>
                await resultFlow.ResolveAndDisplayAsync(ambiguous.Candidates[0], ambiguous.Candidates, hint, userId, ct),
            BdLookupOutcome.NotFound notFound =>
                RequestClosure.Closed($"L'OCR a identifié « {detection.Serie} » mais {notFound.Message}", success: false),
            BdLookupOutcome.Failed failed => RequestClosure.Closed(failed.Message, success: false),
            _ => RequestClosure.Closed("Erreur inattendue.", success: false),
        };
    }

    private static (string Plain, string Html) FormatDetection(BdCoverAnalysis.Detection detection)
    {
        var lines = new List<(string Label, string Value)> { ("Type", detection.Type ?? "?") };

        if (!string.IsNullOrWhiteSpace(detection.Titre)) lines.Add(("Titre (couverture)", detection.Titre));
        if (!string.IsNullOrWhiteSpace(detection.Numero)) lines.Add(("N°", detection.Numero));
        if (!string.IsNullOrWhiteSpace(detection.Editeur)) lines.Add(("Éditeur (couverture)", detection.Editeur));
        if (detection.Auteurs is { Count: > 0 }) lines.Add(("Auteurs (couverture)", string.Join(", ", detection.Auteurs)));

        var plain = "Couverture identifiée :\n" + string.Join('\n', lines.Select(l => $"{l.Label} : {l.Value}"));
        var html = "<p><b>Couverture identifiée</b><br>" +
            string.Join("<br>", lines.Select(l => $"{Enc(l.Label)} : {Enc(l.Value)}")) + "</p>";
        return (plain, html);
    }

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
}

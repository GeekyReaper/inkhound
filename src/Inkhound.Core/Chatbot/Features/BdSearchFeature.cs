using Foundation.Core.Chatbot.Features;
using Inkhound.Core.Chatbot.Services;

namespace Inkhound.Core.Chatbot.Features;

/// <summary>
/// Recherche une série à partir d'un titre saisi, puis déroule le même parcours que <c>!bd-scan</c>
/// à partir de la recherche (couvertures + fiche + albums + choix du résultat + ajout à une
/// librairie), via <see cref="BdSeriesResultFlow"/>. Contrairement à <c>!bd-scan</c>, il n'y a pas
/// d'analyse d'image : seul le titre est transmis, sans filtre album/numéro.
/// </summary>
public sealed class BdSearchFeature(
    BdSeriesLookupService lookupService,
    BdSeriesResultFlow resultFlow) : IChatFeature
{
    public string Name => "bd-search";

    public string Description =>
        "Recherche une série BD/Manga/Comics par titre et propose de l'ajouter à une librairie. " +
        "Usage : !bd-search <titre de la série>";

    // Lancement de la demande, posté par le socle avant l'exécution. L'annulation n'est annoncée
    // qu'ici ; les menus suivants ne la rappellent pas.
    public string? DescribeInProgress(FeatureCommand command) =>
        "🚀 Demande lancée — tapez « a » à tout moment pour l'annuler.\n🔎 Recherche de la série sur Inkhound…";

    public async Task<FeatureExecutionResult> ExecuteAsync(FeatureCommand command, CancellationToken ct)
    {
        var title = string.Join(' ', command.Args).Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return RequestClosure.Closed("Usage : !bd-search <titre de la série>", success: false);
        }

        var outcome = await lookupService.SearchAsync(title, album: null, numeroAlbum: null, ct);
        var userId = command.TriggerEvent.Sender;

        return outcome switch
        {
            BdLookupOutcome.Resolved resolved =>
                await resultFlow.DisplayResultAndOfferAsync(resolved.Detail, resolved.Candidates, AlbumHint.None, userId, ct),
            // Recherche par titre seul : souvent ambiguë. On retient le 1er candidat, le menu des
            // autres résultats permet ensuite d'en changer.
            BdLookupOutcome.Ambiguous ambiguous =>
                await resultFlow.ResolveAndDisplayAsync(ambiguous.Candidates[0], ambiguous.Candidates, AlbumHint.None, userId, ct),
            BdLookupOutcome.NotFound notFound => RequestClosure.Closed(notFound.Message, success: false),
            BdLookupOutcome.Failed failed => RequestClosure.Closed(failed.Message, success: false),
            _ => RequestClosure.Closed("Erreur inattendue.", success: false),
        };
    }
}

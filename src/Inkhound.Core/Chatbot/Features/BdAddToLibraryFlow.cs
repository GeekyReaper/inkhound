using System.Net;
using Foundation.Core.Chatbot;
using Foundation.Core.Chatbot.Features;
using Inkhound.Core.Chatbot.Services;
using Inkhound.Core.Models;
using Inkhound.Core.Sources;

namespace Inkhound.Core.Chatbot.Features;

/// <summary>
/// Sous-assistant d'ajout d'une série, entré après la vérification ayant montré qu'elle n'est pas
/// encore surveillée : choix de la librairie → classification d'âge → synthèse + confirmation →
/// ajout réel. Réutilise le mécanisme « tape un nombre » de <see cref="PendingDisambiguationStore"/> :
/// chaque étape enregistre un <see cref="PendingDisambiguation"/> dont le callback exécute l'action
/// puis ré-enregistre l'étape suivante — l'état est porté par les closures. « a » annule (géré par
/// le socle).
/// </summary>
internal static class BdAddToLibraryFlow
{
    /// <summary>Menu de choix de la librairie. Retourne (plain, html) et enregistre le menu en attente.</summary>
    public static (string Plain, string Html) BuildLibraryMenu(
        ChatbotContext context, InkhoundLibraryService service,
        string userId, SourceVolume series, IReadOnlyList<Library> libraries)
    {
        var menu = NumberedMenu.Build(
            $"Dans quelle librairie ajouter « {series.Name} » ?",
            [.. libraries.Select(l => l.Name)]);

        Register(context, userId, libraries.Count,
            (choice, ct) => OnLibraryChosenAsync(context, service, userId, series, libraries[choice - 1], ct));

        return menu;
    }

    private static Task<FeatureExecutionResult> OnLibraryChosenAsync(
        ChatbotContext context, InkhoundLibraryService service,
        string userId, SourceVolume series, Library library, CancellationToken ct)
    {
        var menu = NumberedMenu.Build(
            $"Librairie « {library.Name} ». Quelle classification d'âge pour « {series.Name} » ?",
            [.. BdAgeRatings.All.Select(r => r.Label)]);

        Register(context, userId, BdAgeRatings.All.Count,
            (choice, ct2) => OnAgeRatingChosenAsync(context, service, userId, series, library, BdAgeRatings.All[choice - 1], ct2));

        return Task.FromResult(Ok(menu));
    }

    private static Task<FeatureExecutionResult> OnAgeRatingChosenAsync(
        ChatbotContext context, InkhoundLibraryService service,
        string userId, SourceVolume series, Library library, BdAgeRatings.BdAgeRating rating, CancellationToken ct)
    {
        var year = series.StartYear?.ToString() ?? "?";

        var headerPlain =
            "Synthèse de la demande d'ajout dans Inkhound :\n" +
            $"- Série : {series.Name}\n" +
            $"- Année : {year}\n" +
            $"- Librairie : {library.Name}\n" +
            $"- Classification : {rating.Label}\n" +
            "Confirmer l'ajout ?";

        var headerHtml =
            "<p><b>Synthèse de la demande d'ajout dans Inkhound :</b></p><ul>" +
            $"<li>Série : {Enc(series.Name)}</li>" +
            $"<li>Année : {Enc(year)}</li>" +
            $"<li>Librairie : {Enc(library.Name)}</li>" +
            $"<li>Classification : {Enc(rating.Label)}</li></ul><p>Confirmer l'ajout ?</p>";

        var menu = NumberedMenu.Build(headerPlain, headerHtml, ["Oui, confirmer l'ajout", "Non, annuler"]);

        Register(context, userId, 2, (choice, ct2) => OnConfirmAsync(service, series, library, rating, choice, ct2));

        return Task.FromResult(Ok(menu));
    }

    private static async Task<FeatureExecutionResult> OnConfirmAsync(
        InkhoundLibraryService service, SourceVolume series, Library library, BdAgeRatings.BdAgeRating rating,
        int choice, CancellationToken ct)
    {
        if (choice != 1)
        {
            return RequestClosure.Closed("Demande annulée : la série n'a pas été ajoutée à Inkhound.");
        }

        try
        {
            var added = await service.AddVolumeToLibraryAsync(library.Id, series.Source, series.SourceId, ct);

            // Échec de la seule classification : le volume est bien créé, on le dit plutôt que de
            // faire passer l'ajout entier pour un échec.
            try
            {
                await service.SetVolumeAgeRatingAsync(added.Id, rating.Value, ct);
            }
            catch (ChatbotGatewayException ex)
            {
                return RequestClosure.Closed(
                    $"✅ « {series.Name} » ajoutée à « {library.Name} », mais la classification n'a pas pu être appliquée : {ex.Message}",
                    $"<p>✅ <b>{Enc(series.Name)}</b> ajoutée à « {Enc(library.Name)} », mais la classification n'a pas pu être appliquée : {Enc(ex.Message)}</p>");
            }

            var plain = $"✅ « {series.Name} » ajoutée à la librairie « {library.Name} » (classification « {rating.Label} »). Elle est désormais surveillée dans Inkhound.";
            var html = $"<p>✅ <b>{Enc(series.Name)}</b> ajoutée à la librairie « {Enc(library.Name)} » (classification « {Enc(rating.Label)} »). Elle est désormais surveillée dans Inkhound.</p>";
            return RequestClosure.Closed(plain, html);
        }
        catch (ChatbotGatewayException ex)
        {
            return RequestClosure.Closed($"❌ Problème technique lors de l'ajout dans Inkhound : {ex.Message}", success: false);
        }
    }

    private static void Register(
        ChatbotContext context, string userId, int count,
        Func<int, CancellationToken, Task<FeatureExecutionResult>> resolve) =>
        context.Disambiguation.Set(context.RoomId, userId, new PendingDisambiguation(
            CandidateCount: count,
            ResolveAsync: resolve,
            ExpiresAt: DateTimeOffset.UtcNow + context.PendingMenuTtl));

    private static FeatureExecutionResult Ok((string Plain, string Html) menu) =>
        FeatureExecutionResult.Ok(menu.Plain, menu.Html);

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
}

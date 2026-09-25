using System.Net;
using Foundation.Core.Chatbot;
using Foundation.Core.Chatbot.Features;
using Inkhound.Core.Chatbot.Services;
using Inkhound.Core.Sources;

namespace Inkhound.Core.Chatbot.Features;

/// <summary>Indice sur l'album scanné (numéro/titre OCR) pour retrouver sa couverture ; <see cref="None"/> si absent (recherche par titre seul).</summary>
public readonly record struct AlbumHint(string? Numero, string? Titre)
{
    public static readonly AlbumHint None = new(null, null);
}

/// <summary>
/// Présentation partagée d'un résultat de recherche de série et assistant d'ajout, réutilisés par
/// <c>!bd-scan</c> (après OCR) et <c>!bd-search</c> (après recherche par titre). Séquence :
/// couvertures + fiche série → liste des albums → nombre de résultats avec menu « voir les autres /
/// passer » (la liste permet de changer de correspondance, avec ré-affichage complet) → vérification
/// Inkhound (déjà surveillée, ou choix de librairie → âge → synthèse → confirmation → ajout).
/// </summary>
public sealed class BdSeriesResultFlow(
    ChatbotContext context,
    BdSeriesLookupService lookupService,
    InkhoundLibraryService libraryService,
    HttpClient coverHttpClient,
    string coverUserAgent,
    int maxAlbumsListed = BdSeriesHtmlFormatter.DefaultMaxAlbumsShown)
{
    private string RoomId => context.RoomId;

    /// <summary>Résout le détail d'un candidat puis présente le résultat (cas d'une recherche ambiguë : on retient le 1er candidat par défaut).</summary>
    public async Task<FeatureExecutionResult> ResolveAndDisplayAsync(
        SourceVolume candidate, IReadOnlyList<SourceVolume> candidates, AlbumHint hint, string userId, CancellationToken ct)
    {
        try
        {
            var detail = await lookupService.ResolveDetailAsync(candidate, ct);
            return await DisplayResultAndOfferAsync(detail, candidates, hint, userId, ct);
        }
        catch (ChatbotGatewayException ex)
        {
            return FeatureExecutionResult.Fail($"Impossible de récupérer les détails de « {candidate.Name} » : {ex.Message}");
        }
    }

    /// <summary>
    /// Poste les couvertures, la fiche série et la liste des albums, puis propose le menu des autres
    /// résultats. S'il n'existe aucun autre candidat, passe directement à la vérification Inkhound.
    /// </summary>
    public async Task<FeatureExecutionResult> DisplayResultAndOfferAsync(
        SeriesDetail detail, IReadOnlyList<SourceVolume> candidates, AlbumHint hint, string userId, CancellationToken ct)
    {
        await SendSourceCoversAsync(detail, hint, ct);
        var (infoPlain, infoHtml) = BdSeriesHtmlFormatter.BuildInfoSection(detail);
        await context.Matrix.SendTextMessageAsync(RoomId, infoPlain, infoHtml, ct);

        var albums = BdSeriesHtmlFormatter.BuildAlbumsListSection(detail.Issues, detail.Series.CountOfIssues, maxAlbumsListed);
        if (albums is not null)
        {
            await context.Matrix.SendTextMessageAsync(RoomId, albums.Value.Plain, albums.Value.Html, ct);
        }

        var others = candidates.Where(c => !SameCandidate(c, detail.Series)).ToList();
        if (others.Count == 0)
        {
            return await ProceedToInkhoundAsync(detail, userId, ct);
        }

        return RegisterResultsMenu(detail, candidates, others, hint, userId);
    }

    // « N résultats trouvés » + [voir les autres résultats / passer à l'étape suivante].
    private FeatureExecutionResult RegisterResultsMenu(
        SeriesDetail detail, IReadOnlyList<SourceVolume> candidates, IReadOnlyList<SourceVolume> others,
        AlbumHint hint, string userId)
    {
        var (plain, html) = NumberedMenu.Build(
            $"{candidates.Count} résultats trouvés pour cette recherche.",
            ["Voir les autres résultats", "Passer à l'étape suivante"]);

        Register(userId, 2, (choice, ct) => choice == 1
            ? Task.FromResult(RegisterResultsListMenu(detail, candidates, others, hint, userId))
            : ProceedToInkhoundAsync(detail, userId, ct));

        return FeatureExecutionResult.Ok(plain, html);
    }

    // Liste des résultats (source + titre lié + année) pour changer la correspondance.
    private FeatureExecutionResult RegisterResultsListMenu(
        SeriesDetail current, IReadOnlyList<SourceVolume> candidates, IReadOnlyList<SourceVolume> others,
        AlbumHint hint, string userId)
    {
        var currentYear = current.Series.StartYear?.ToString() ?? "?";
        var items = new List<(string Plain, string Html)>
        {
            ($"Garder la sélection actuelle : [{current.Series.Source}] {current.Series.Name} ({currentYear})",
             $"Garder la sélection actuelle : [{Enc(current.Series.Source)}] <b>{Enc(current.Series.Name)}</b> ({Enc(currentYear)})"),
        };
        items.AddRange(others.Select(FormatResultLine));

        var (plain, html) = NumberedMenu.Build("Résultats trouvés (sélectionnez la correspondance à conserver) :", items);

        Register(userId, items.Count,
            (choice, ct) => OnResultChosenAsync(current, candidates, others, hint, choice, userId, ct));

        return FeatureExecutionResult.Ok(plain, html);
    }

    private async Task<FeatureExecutionResult> OnResultChosenAsync(
        SeriesDetail current, IReadOnlyList<SourceVolume> candidates, IReadOnlyList<SourceVolume> others,
        AlbumHint hint, int choice, string userId, CancellationToken ct)
    {
        // Choix 1 = garder la sélection actuelle → revenir au menu précédent, sans ré-afficher.
        if (choice == 1)
        {
            return RegisterResultsMenu(current, candidates, others, hint, userId);
        }

        var chosen = others[choice - 2];
        try
        {
            var detail = await lookupService.ResolveDetailAsync(chosen, ct);
            return await DisplayResultAndOfferAsync(detail, candidates, hint, userId, ct);
        }
        catch (ChatbotGatewayException ex)
        {
            return FeatureExecutionResult.Fail($"Impossible de récupérer les détails de « {chosen.Name} » : {ex.Message}");
        }
    }

    /// <summary>
    /// Vérifie si la série est déjà surveillée. Si oui, clôture la demande ; sinon lance le
    /// sous-assistant d'ajout (librairie → âge → confirmation).
    /// </summary>
    private async Task<FeatureExecutionResult> ProceedToInkhoundAsync(SeriesDetail detail, string userId, CancellationToken ct)
    {
        await context.Matrix.SendTextMessageAsync(RoomId, "🔍 Vérification dans Inkhound…", null, ct);

        try
        {
            var existing = await libraryService.FindLibraryContainingAsync(detail.Series, ct);
            if (existing is not null)
            {
                return RequestClosure.Closed(
                    $"📚 « {detail.Series.Name} » est déjà surveillée dans Inkhound (librairie « {existing.Name} »).",
                    $"<p>📚 « {Enc(detail.Series.Name)} » est déjà surveillée dans Inkhound (librairie « {Enc(existing.Name)} »).</p>");
            }

            var libraries = await libraryService.GetLibrariesAsync(ct);
            if (libraries.Count == 0)
            {
                return RequestClosure.Closed(
                    "Aucune librairie Inkhound configurée : ajout impossible.",
                    "<p>Aucune librairie Inkhound configurée : ajout impossible.</p>");
            }

            var (plain, html) = BdAddToLibraryFlow.BuildLibraryMenu(context, libraryService, userId, detail.Series, libraries);
            return FeatureExecutionResult.Ok(plain, html);
        }
        catch (ChatbotGatewayException ex)
        {
            return RequestClosure.Closed(
                $"Vérification Inkhound indisponible : {ex.Message}.",
                $"<p>Vérification Inkhound indisponible : {Enc(ex.Message)}.</p>");
        }
    }

    private void Register(string userId, int count, Func<int, CancellationToken, Task<FeatureExecutionResult>> resolve) =>
        context.Disambiguation.Set(RoomId, userId, new PendingDisambiguation(
            CandidateCount: count,
            ResolveAsync: resolve,
            ExpiresAt: DateTimeOffset.UtcNow + context.PendingMenuTtl));

    private static (string Plain, string Html) FormatResultLine(SourceVolume c)
    {
        var year = c.StartYear?.ToString() ?? "?";
        var plain = c.SiteUrl is not null
            ? $"[{c.Source}] {c.Name} ({year}) — {c.SiteUrl}"
            : $"[{c.Source}] {c.Name} ({year})";
        var titleHtml = c.SiteUrl is not null
            ? $"<a href=\"{Enc(c.SiteUrl)}\">{Enc(c.Name)}</a>"
            : Enc(c.Name);
        return (plain, $"[{Enc(c.Source)}] {titleHtml} ({Enc(year)})");
    }

    /// <summary>
    /// Poste, depuis les URL de la source, la couverture de la série puis (si un indice d'album est
    /// fourni et correspond à un tome) celle de l'album — en évitant le doublon si les deux URL sont
    /// identiques.
    /// </summary>
    private async Task SendSourceCoversAsync(SeriesDetail detail, AlbumHint hint, CancellationToken ct)
    {
        await TrySendImageAsync(detail.Series.ImageUrl, ct);

        var albumImageUrl = FindMatchingIssue(hint, detail.Issues)?.ImageUrl;
        if (!string.IsNullOrWhiteSpace(albumImageUrl) &&
            !string.Equals(albumImageUrl, detail.Series.ImageUrl, StringComparison.OrdinalIgnoreCase))
        {
            await TrySendImageAsync(albumImageUrl, ct);
        }
    }

    /// <summary>
    /// Poste une image externe (URL de la source) comme message m.image : télécharge l'image, la
    /// ré-uploade sur le homeserver — les clients Matrix ne rendent pas une URL http externe — puis
    /// envoie le m.image. Best-effort : toute erreur est tracée mais ne casse jamais la réponse.
    /// </summary>
    private async Task TrySendImageAsync(string? imageUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            // Certaines sources (Bédéthèque) renvoient 403 sans User-Agent.
            request.Headers.TryAddWithoutValidation("User-Agent", coverUserAgent);
            using var imageResponse = await coverHttpClient.SendAsync(request, ct);
            imageResponse.EnsureSuccessStatusCode();

            var bytes = await imageResponse.Content.ReadAsByteArrayAsync(ct);
            var contentType = imageResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var fileName = DeriveFileName(imageUrl);

            using var stream = new MemoryStream(bytes);
            var mxc = await context.Matrix.UploadMediaAsync(stream, contentType, fileName, ct);
            await context.Matrix.SendMediaMessageAsync(RoomId, "m.image", fileName, mxc, contentType, bytes.Length, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            context.Trace.Warn($"Impossible d'envoyer l'image depuis {imageUrl} : {ex.Message}");
        }
    }

    /// <summary>
    /// Retrouve le tome correspondant à l'album scanné : d'abord par numéro (exact puis numérique),
    /// sinon par titre (comparaison dans les deux sens, comme <see cref="BdSeriesLookupService"/>).
    /// Retourne null si l'indice est vide ou si rien ne correspond.
    /// </summary>
    private static SourceIssue? FindMatchingIssue(AlbumHint hint, IReadOnlyList<SourceIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(hint.Numero))
        {
            var numero = hint.Numero.Trim();

            var byNumber = issues.FirstOrDefault(i =>
                !string.IsNullOrWhiteSpace(i.IssueNumber) &&
                string.Equals(i.IssueNumber.Trim(), numero, StringComparison.OrdinalIgnoreCase));
            if (byNumber is not null) return byNumber;

            if (int.TryParse(numero, out var n))
            {
                var byNumeric = issues.FirstOrDefault(i => int.TryParse(i.IssueNumber?.Trim(), out var m) && m == n);
                if (byNumeric is not null) return byNumeric;
            }
        }

        if (!string.IsNullOrWhiteSpace(hint.Titre))
        {
            var album = hint.Titre.Trim();
            return issues.FirstOrDefault(i => i.Name is not null &&
                (i.Name.Contains(album, StringComparison.OrdinalIgnoreCase) ||
                 album.Contains(i.Name, StringComparison.OrdinalIgnoreCase)));
        }

        return null;
    }

    private static string DeriveFileName(string url)
    {
        try
        {
            var name = Path.GetFileName(new Uri(url).AbsolutePath);
            return string.IsNullOrWhiteSpace(name) ? "cover.jpg" : name;
        }
        catch (UriFormatException)
        {
            return "cover.jpg";
        }
    }

    private static bool SameCandidate(SourceVolume a, SourceVolume b) =>
        string.Equals(a.SourceId, b.SourceId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Source, b.Source, StringComparison.OrdinalIgnoreCase);

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
}

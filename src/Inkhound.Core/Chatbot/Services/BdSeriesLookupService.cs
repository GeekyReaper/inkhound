using Foundation.Core.Chatbot;
using Inkhound.Core.Sources;

namespace Inkhound.Core.Chatbot.Services;

/// <summary>
/// Pipeline recherche → filtrage → résolution contre Inkhound, partagé par toute commande qui doit
/// transformer un titre de série (+ filtres album/numéro optionnels) en un <see cref="SeriesDetail"/>
/// unique ou en une liste de candidats à départager. <c>!bd-scan</c> l'alimente avec les valeurs
/// détectées par OCR, <c>!bd-search</c> avec le titre saisi.
/// </summary>
public sealed class BdSeriesLookupService(
    IInkhoundChatbotGateway inkhound,
    IChatTrace trace,
    int volumeSearchPageSize = BdSeriesLookupService.DefaultVolumeSearchPageSize)
{
    /// <summary>Aligné sur l'UI Inkhound (volume.service.ts) pour reproduire les mêmes résultats.</summary>
    public const int DefaultVolumeSearchPageSize = 16;

    private const int MaxCandidatesToInspectForFilters = 8;
    private const int MaxIssuesPageSize = 500;

    public async Task<BdLookupOutcome> SearchAsync(string title, string? album, string? numeroAlbum, CancellationToken ct)
    {
        trace.Info($"Recherche de série \"{title}\" (album={album ?? "(aucun)"}, numéro={numeroAlbum ?? "(aucun)"})");

        try
        {
            var page = await inkhound.SearchVolumesAsync(title, pageSize: volumeSearchPageSize, ct: ct);
            var candidates = page.Items;

            trace.Info($"{candidates.Count} résultat(s) pour \"{title}\" : " +
                       string.Join(" | ", candidates.Select(c => $"[{c.Source}] {c.Name}")));

            if (candidates.Count == 0)
            {
                return new BdLookupOutcome.NotFound($"Aucune série trouvée pour \"{title}\".");
            }

            if (candidates.Count == 1)
            {
                return new BdLookupOutcome.Resolved(await ResolveDetailAsync(candidates[0], ct), candidates);
            }

            var hasFilter = !string.IsNullOrWhiteSpace(album) || !string.IsNullOrWhiteSpace(numeroAlbum);
            if (!hasFilter)
            {
                return new BdLookupOutcome.Ambiguous(candidates);
            }

            // Un filtre a été fourni : on retient le premier candidat qui matche. S'il n'y en a
            // aucun, on ne clôt pas la demande pour autant : on retombe sur le 1er résultat et on
            // déroule le workflow normal (l'étape « voir les autres résultats » permet d'en changer).
            var survivor = await FindFirstMatchingCandidateAsync(candidates, album, numeroAlbum, ct);
            if (survivor is null)
            {
                trace.Info($"Aucun candidat ne correspond aux filtres, repli sur le 1er des {candidates.Count} résultat(s)");
                survivor = candidates[0];
            }

            return new BdLookupOutcome.Resolved(await ResolveDetailAsync(survivor, ct), candidates);
        }
        catch (ChatbotGatewayException ex)
        {
            trace.Warn($"Échec de la recherche Inkhound pour \"{title}\" : {ex.Message}");
            return new BdLookupOutcome.Failed($"La recherche Inkhound a échoué : {ex.Message}");
        }
    }

    /// <summary>
    /// Peut lever <see cref="ChatbotGatewayException"/> : les appelants situés hors du try/catch de
    /// <see cref="SearchAsync"/> (callback de résolution d'un menu) doivent l'intercepter eux-mêmes.
    /// </summary>
    public async Task<SeriesDetail> ResolveDetailAsync(SourceVolume candidate, CancellationToken ct)
    {
        var page = await inkhound.GetIssuesBySourceAsync(
            candidate.Source, candidate.SourceId, page: 1, pageSize: ClampPageSize(candidate.CountOfIssues), ct: ct);
        return new SeriesDetail(candidate, page.Items);
    }

    private async Task<SourceVolume?> FindFirstMatchingCandidateAsync(
        IReadOnlyList<SourceVolume> candidates, string? album, string? numeroAlbum, CancellationToken ct)
    {
        var numero = ParseNumeroAlbum(numeroAlbum);
        var inspected = candidates.Take(MaxCandidatesToInspectForFilters).ToList();

        if (candidates.Count > inspected.Count)
        {
            trace.Info($"{candidates.Count} candidat(s) mais seuls les {inspected.Count} premiers sont inspectés pour le filtrage");
        }

        foreach (var candidate in inspected)
        {
            // Un numéro non numérique (hors-série « HS », édition « 47Ter »…) ne peut pas être
            // comparé : traité comme « pas de contrainte », même logique permissive que pour l'album.
            if (numero is not null && candidate.CountOfIssues < numero.Value)
            {
                trace.Info($"[{candidate.Source}] {candidate.Name} — {candidate.CountOfIssues} tome(s) < numéro {numero} demandé, écarté");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(album) && !await CandidateHasAlbumAsync(candidate, album, ct))
            {
                trace.Info($"[{candidate.Source}] {candidate.Name} — aucun tome ne correspond à \"{album}\", écarté");
                continue;
            }

            return candidate;
        }

        return null;
    }

    private async Task<bool> CandidateHasAlbumAsync(SourceVolume candidate, string album, CancellationToken ct)
    {
        var page = await inkhound.GetIssuesBySourceAsync(
            candidate.Source, candidate.SourceId, page: 1, pageSize: ClampPageSize(candidate.CountOfIssues), ct: ct);

        // Comparaison dans les deux sens : la couverture ajoute parfois une mention devant le titre
        // (« Première époque - Le Temps des bricoleurs » pour insister sur le tome 1), donc le titre
        // Inkhound est inclus dans le titre OCR sans que l'inverse soit vrai.
        return page.Items.Any(i => i.Name is not null &&
            (i.Name.Contains(album, StringComparison.OrdinalIgnoreCase) ||
             album.Contains(i.Name, StringComparison.OrdinalIgnoreCase)));
    }

    private static int? ParseNumeroAlbum(string? numeroAlbum) =>
        numeroAlbum is not null && int.TryParse(numeroAlbum, out var n) ? n : null;

    private static int ClampPageSize(int desired) => Math.Clamp(desired <= 0 ? 1 : desired, 1, MaxIssuesPageSize);
}

using Inkhound.Core.Analysis;
using Inkhound.Core.Models;
using Inkhound.Core.Prowlarr;

namespace Inkhound.Core.Scoring;

public static class ScoringTorrent
{
    // Taille plausible pour un CBZ de comic : 10 Mo – 500 Mo
    private const long MinSizeBytes = 10_485_760L;
    private const long MaxSizeBytes = 524_288_000L;

    public static ScoredSearchResultTorrent ScoringIndexerResult(
        Volume volume,
        Issue issue,
        ProwlarrSearchResult result)
    {
        var analysis         = TorrentTypeAnalyzer.Analyze(result.Title, result.Size, volume.CountOfIssues);
        var titleMatch       = ScoreTitle(volume, result);
        var issueNumberMatch = ScoreIssueNumber(issue, result);
        var yearMatch        = ScoreYear(volume, issue, result, analysis.Type);
        var authorMatch      = ScoreAuthor(issue, volume, result);
        var publisherMatch   = ScorePublisher(volume, result);
        var sizePlausibility = ScoreSize(result, analysis.Type);
        var seederScore      = ScoreSeeders(result);
        var formatScore      = ScoreFormat(result, analysis.Type);

        var baseScore = titleMatch + issueNumberMatch + yearMatch + authorMatch + publisherMatch + sizePlausibility + seederScore + formatScore;
        var total     = ApplyTypeAdjustment(baseScore, analysis, issue.IssueNumber, result.Size, yearMatch, authorMatch, publisherMatch);

        var details = new ScoreDetailsTorrent(titleMatch, issueNumberMatch, yearMatch, authorMatch, publisherMatch, sizePlausibility, seederScore, formatScore);
        return new ScoredSearchResultTorrent(result, total, details, analysis);
    }

    public static List<ScoredSearchResultTorrent> ScoreAndSort(
        Volume volume,
        Issue issue,
        List<ProwlarrSearchResult> results)
        => results
            .Select(r => ScoringIndexerResult(volume, issue, r))
            .OrderByDescending(r => r.Score)
            .ToList();

    // ── Ajustement selon le type de torrent détecté ────────────────────────

    private static float ApplyTypeAdjustment(
        float baseScore, TorrentAnalysis analysis, int issueNumber, long sizeBytes,
        float yearMatch, float authorMatch, float publisherMatch)
    {
        // Réassurance : quand le numéro d'issue est confirmé, l'année, l'auteur et l'éditeur ne changent pas
        // la nature du match mais renforcent la confiance qu'on a affaire à la bonne issue (ex : même tome
        // republié sous un autre nom, homonymie de série...). Petite bonification, plafonnée par le Clamp final.
        var reassurance = yearMatch * 0.5f + authorMatch * 0.5f + publisherMatch * 0.5f;

        if (analysis.Type == "SINGLE")
        {
            if (analysis.Label.StartsWith('#') && int.TryParse(analysis.Label.AsSpan(1), out var num))
                return num == issueNumber
                    ? Math.Clamp(70f + ScoreSizeBonusForSingle(sizeBytes) + reassurance, 0f, 100f)
                    : Math.Clamp(baseScore * 0.1f, 0f, 10f);

            // Numéro inconnu ("?") — pénalité modérée
            return Math.Clamp(baseScore * 0.7f, 0f, 100f);
        }

        if (analysis.Type == "PACK")
        {
            if (analysis.Label == "Full")
                return Math.Clamp(baseScore + 20f, 0f, 100f);

            if (analysis.Label.Contains("..."))
            {
                var sep = analysis.Label.IndexOf("...", StringComparison.Ordinal);
                if (sep > 0
                    && int.TryParse(analysis.Label.AsSpan(0, sep), out var start)
                    && int.TryParse(analysis.Label.AsSpan(sep + 3), out var end))
                    return issueNumber >= start && issueNumber <= end
                        ? Math.Clamp(baseScore + 20f, 0f, 100f)
                        : Math.Clamp(baseScore * 0.1f, 0f, 10f);
            }

            // Plage inconnue ("?") — légère pénalité
            return Math.Clamp(baseScore * 0.7f, 0f, 100f);
        }

        // UNKNOWN — pénalité forte, type indéterminable
        return Math.Clamp(baseScore * 0.5f, 0f, 100f);
    }

    private static float ScoreSizeBonusForSingle(long sizeBytes)
    {
        const long cap = 209_715_200L; // 200 MB → bonus maximum
        if (sizeBytes <= 0) return 0f;
        return Math.Min(30f, sizeBytes / (float)cap * 30f);
    }

    // ── Composantes de scoring ─────────────────────────────────────────────

    // Comparaison titre normalisé entre volume.Title et result.Title (max 40)
    // internal : réutilisé par ScoringVolumePack (recherche au niveau Volume, sans Issue précise).
    internal static float ScoreTitle(Volume volume, ProwlarrSearchResult result)
    {
        var volNorm = TextSimilarity.Normalize(volume.Title);
        var resNorm = TextSimilarity.Normalize(result.Title);

        if (volNorm == resNorm) return 40f;
        if (resNorm.Contains(volNorm) || volNorm.Contains(resNorm)) return 25f;

        var distance = TextSimilarity.LevenshteinDistance(volNorm, resNorm);
        return Math.Max(0f, 20f - distance);
    }

    // Présence du numéro d'issue dans le titre du résultat (max 20)
    private static float ScoreIssueNumber(Issue issue, ProwlarrSearchResult result)
    {
        var num = issue.IssueNumber;
        var title = result.Title;

        // Formes courantes : "#45", " 45 ", " 045 ", "(45)"
        if (title.Contains($"#{num}", StringComparison.OrdinalIgnoreCase)) return 20f;
        if (title.Contains($" {num} ", StringComparison.OrdinalIgnoreCase)) return 15f;
        if (title.Contains($"({num})", StringComparison.OrdinalIgnoreCase)) return 15f;
        if (title.Contains($" {num:D3} ", StringComparison.OrdinalIgnoreCase)) return 15f;
        if (title.Contains(num.ToString(), StringComparison.OrdinalIgnoreCase)) return 8f;
        return 0f;
    }

    // Présence d'un nom d'auteur (prénom ou nom, insensible aux accents/casse) dans le titre (max 12).
    // Union de issue.Authors et volume.Authors (dédoublonnés) : une compilation/PACK cite souvent
    // l'équipe créative globale du volume plutôt que les seuls crédits d'une issue précise.
    private static float ScoreAuthor(Issue issue, Volume volume, ProwlarrSearchResult result)
    {
        var authors = issue.Authors
            .Concat(volume.Authors)
            .DistinctBy(a => TextSimilarity.Normalize(a.Name))
            .ToList();
        return ScoreAuthorMatch(authors, result);
    }

    // Correspondance tokenisée nom d'auteur ↔ titre du résultat (max 12).
    // internal : réutilisé par ScoringVolumePack avec volume.Authors seul (pas d'Issue précise).
    internal static float ScoreAuthorMatch(List<VolumeAuthor> authors, ProwlarrSearchResult result)
    {
        if (authors.Count == 0) return 0f;

        var titleTokens = TextSimilarity.Normalize(result.Title)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();
        if (titleTokens.Count == 0) return 0f;

        var best = 0f;
        foreach (var author in authors)
        {
            var nameTokens = TextSimilarity.Normalize(author.Name)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 3)
                .ToArray();
            if (nameTokens.Length == 0) continue;

            var matched = nameTokens.Count(titleTokens.Contains);
            if (matched == 0) continue;

            // Nom complet retrouvé (prénom + nom) : signal fort. Un seul token (prénom ou nom seul) : signal partiel.
            var score = matched == nameTokens.Length ? 12f : 6f;
            best = Math.Max(best, score);
        }

        return best;
    }

    // Correspondance d'année (max 10). Pour un SINGLE, l'année exacte de issue.Year ?? volume.Year
    // est attendue. Pour un PACK, une compilation affiche typiquement l'année du scan plutôt que
    // celle d'une issue précise : on accepte alors toute année entre le début du volume et aujourd'hui.
    private static float ScoreYear(Volume volume, Issue issue, ProwlarrSearchResult result, string torrentType)
    {
        if (torrentType == "PACK")
            return ScoreYearForPack(volume, result);

        var year = issue.Year ?? volume.Year;
        if (year is null) return 0f;

        if (result.PublishDate?.Year == year) return 10f;
        if (result.Title.Contains(year.Value.ToString(), StringComparison.Ordinal)) return 7f;
        return 0f;
    }

    // Tolérance d'année pour un PACK (max 10) : toute année entre le début du volume et aujourd'hui.
    // internal : réutilisé par ScoringVolumePack, où tous les résultats visés sont des compilations.
    internal static float ScoreYearForPack(Volume volume, ProwlarrSearchResult result)
    {
        if (volume.Year is not { } start) return 0f;
        var end = DateTime.UtcNow.Year;

        if (result.PublishDate is { } published && published.Year >= start && published.Year <= end)
            return 10f;

        for (var y = start; y <= end; y++)
            if (result.Title.Contains(y.ToString(), StringComparison.Ordinal))
                return 6f;

        return 0f;
    }

    // Présence de l'éditeur du volume dans le titre (max 8)
    // internal : réutilisé par ScoringVolumePack.
    internal static float ScorePublisher(Volume volume, ProwlarrSearchResult result)
    {
        if (string.IsNullOrWhiteSpace(volume.Publisher)) return 0f;

        var publisherNorm = TextSimilarity.Normalize(volume.Publisher);
        if (publisherNorm.Length == 0) return 0f;

        return TextSimilarity.Normalize(result.Title).Contains(publisherNorm) ? 8f : 0f;
    }

    // Plausibilité de la taille (max 10) — progressive pour les PACKs, plage fixe pour les SINGLEs
    // internal : réutilisé par ScoringVolumePack.
    internal static float ScoreSize(ProwlarrSearchResult result, string torrentType)
    {
        if (result.Size <= 0) return 0f;

        if (torrentType == "PACK")
        {
            // Pour les packs, plus c'est gros mieux c'est (qualité de scan plus élevée)
            const long mb = 1_048_576L;
            var sizeMB = result.Size / (float)mb;
            if (sizeMB >= 500f) return 10f;
            if (sizeMB >= 200f) return 8f;
            if (sizeMB >= 50f)  return 5f;
            return 2f;
        }

        if (result.Size >= MinSizeBytes && result.Size <= MaxSizeBytes) return 10f;
        if (result.Size >= MinSizeBytes / 2 && result.Size <= MaxSizeBytes * 2) return 5f;
        return 0f;
    }

    // Score seeders pour torrents, bonus fixe pour usenet (max 10)
    // internal : réutilisé par ScoringVolumePack.
    internal static float ScoreSeeders(ProwlarrSearchResult result)
    {
        // Usenet : pas de seeders, bonus plat
        if (result.Protocol?.Equals("torrent", StringComparison.OrdinalIgnoreCase) != true)
            return 7f;

        return Math.Min(10f, result.Seeders / 3f);
    }

    // Détection du format d'archive dans le titre (max 10)
    // internal : réutilisé par ScoringVolumePack.
    internal static float ScoreFormat(ProwlarrSearchResult result, string torrentType)
    {
        var title = result.Title;

        if (torrentType == "PACK")
        {
            // Pour les packs, le format est secondaire : la taille indique la qualité
            if (ContainsIgnoreCase(title, "cbz")) return 8f;
            if (ContainsIgnoreCase(title, "cbr")) return 7f;
            if (ContainsIgnoreCase(title, "pdf")) return 6f;
            return 6f;
        }

        if (ContainsIgnoreCase(title, "cbz")) return 10f;
        if (ContainsIgnoreCase(title, "cbr")) return 4f;
        if (ContainsIgnoreCase(title, "pdf")) return 2f;

        // Format inconnu — score neutre
        return 6f;
    }

    // ── Utilitaires ────────────────────────────────────────────────────────

    private static bool ContainsIgnoreCase(string source, string value)
        => source.Contains(value, StringComparison.OrdinalIgnoreCase);
}

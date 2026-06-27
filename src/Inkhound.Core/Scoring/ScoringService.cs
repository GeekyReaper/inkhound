using System.Globalization;
using System.Text;
using Inkhound.Core.Models;
using Inkhound.Core.Prowlarr;

namespace Inkhound.Core.Scoring;

public static class ScoringService
{
    // Taille plausible pour un CBZ de comic : 10 Mo – 500 Mo
    private const long MinSizeBytes = 10_485_760L;
    private const long MaxSizeBytes = 524_288_000L;

    public static ScoredSearchResult ScoringIndexerResult(
        Volume volume,
        Issue issue,
        ProwlarrSearchResult result)
    {
        var titleMatch      = ScoreTitle(volume, result);
        var issueNumberMatch = ScoreIssueNumber(issue, result);
        var yearMatch       = ScoreYear(volume, issue, result);
        var sizePlausibility = ScoreSize(result);
        var seederScore     = ScoreSeeders(result);
        var formatScore     = ScoreFormat(result);

        var total = Math.Clamp(
            titleMatch + issueNumberMatch + yearMatch + sizePlausibility + seederScore + formatScore,
            0f, 100f);

        var details = new ScoreDetails(titleMatch, issueNumberMatch, yearMatch, sizePlausibility, seederScore, formatScore);
        return new ScoredSearchResult(result, total, details);
    }

    public static List<ScoredSearchResult> ScoreAndSort(
        Volume volume,
        Issue issue,
        List<ProwlarrSearchResult> results)
        => results
            .Select(r => ScoringIndexerResult(volume, issue, r))
            .OrderByDescending(r => r.Score)
            .ToList();

    // ── Composantes de scoring ─────────────────────────────────────────────

    // Comparaison titre normalisé entre volume.Title et result.Title (max 40)
    private static float ScoreTitle(Volume volume, ProwlarrSearchResult result)
    {
        var volNorm = Normalize(volume.Title);
        var resNorm = Normalize(result.Title);

        if (volNorm == resNorm) return 40f;
        if (resNorm.Contains(volNorm) || volNorm.Contains(resNorm)) return 25f;

        var distance = LevenshteinDistance(volNorm, resNorm);
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

    // Correspondance d'année : issue.Year ?? volume.Year (max 10)
    private static float ScoreYear(Volume volume, Issue issue, ProwlarrSearchResult result)
    {
        var year = issue.Year ?? volume.Year;
        if (year is null) return 0f;

        if (result.PublishDate?.Year == year) return 10f;
        if (result.Title.Contains(year.Value.ToString(), StringComparison.Ordinal)) return 7f;
        return 0f;
    }

    // Plausibilité de la taille pour un comic CBZ (max 10)
    private static float ScoreSize(ProwlarrSearchResult result)
    {
        if (result.Size <= 0) return 0f;
        if (result.Size >= MinSizeBytes && result.Size <= MaxSizeBytes) return 10f;
        if (result.Size >= MinSizeBytes / 2 && result.Size <= MaxSizeBytes * 2) return 5f;
        return 0f;
    }

    // Score seeders pour torrents, bonus fixe pour usenet (max 10)
    private static float ScoreSeeders(ProwlarrSearchResult result)
    {
        // Usenet : pas de seeders, bonus plat
        if (result.Protocol?.Equals("torrent", StringComparison.OrdinalIgnoreCase) != true)
            return 7f;

        return Math.Min(10f, result.Seeders / 3f);
    }

    // Détection du format d'archive dans le titre (max 10)
    private static float ScoreFormat(ProwlarrSearchResult result)
    {
        var title = result.Title;

        if (ContainsIgnoreCase(title, "cbz")) return 10f;
        if (ContainsIgnoreCase(title, "cbr")) return 4f;
        if (ContainsIgnoreCase(title, "pdf")) return 2f;

        // Format inconnu — score neutre
        return 6f;
    }

    // ── Utilitaires ────────────────────────────────────────────────────────

    private static bool ContainsIgnoreCase(string source, string value)
        => source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(
                Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                d[i - 1, j - 1] + cost);
        }

        return d[a.Length, b.Length];
    }
}

using Inkhound.Core.Analysis;
using Inkhound.Core.Models;
using Inkhound.Core.Prowlarr;

namespace Inkhound.Core.Scoring;

public record ScoreDetailsVolumePack(
    float TitleMatch,
    float YearMatch,
    float AuthorMatch,
    float PublisherMatch,
    float SizePlausibility,
    float SeederScore,
    float FormatScore,
    float CoverageBonus);

public record ScoredSearchResultVolumePack(
    ProwlarrSearchResult Result,
    float Score,
    ScoreDetailsVolumePack Details,
    TorrentAnalysis Analysis,
    int CoveredIssueCount,
    int TotalMissingIssueCount);

// Score un résultat Prowlarr par rapport à un Volume entier (pas une Issue précise) : contrairement à
// ScoringTorrent (favorise un SINGLE qui matche exactement une issue ciblée), ce scorer favorise le
// résultat qui couvre le plus grand nombre d'issues MISSING du volume — l'objectif étant de maximiser
// le nombre d'issues obtenues via un seul torrent (PACK/compilation).
public static class ScoringVolumePack
{
    // Poids dominant : traduit directement l'objectif "maximiser les issues couvertes". Un PACK qui
    // couvre 20 des 24 issues manquantes surclasse mécaniquement un SINGLE qui n'en couvre qu'une.
    private const float MaxCoverageBonus = 60f;

    // Confiance réduite pour une couverture non vérifiée précisément (PACK "Full"/"?" sans plage
    // explicite) par rapport à une plage de tomes confirmée par la regex.
    private const float UnverifiedCoverageConfidence = 0.7f;

    public static ScoredSearchResultVolumePack ScoringIndexerResult(
        Volume volume,
        List<Issue> missingIssues,
        ProwlarrSearchResult result)
    {
        var analysis = TorrentTypeAnalyzer.Analyze(result.Title, result.Size, volume.CountOfIssues);
        var covered  = CountCoveredIssues(analysis, missingIssues);

        var titleMatch       = ScoringTorrent.ScoreTitle(volume, result);
        var yearMatch        = ScoringTorrent.ScoreYearForPack(volume, result);
        var authorMatch      = ScoringTorrent.ScoreAuthorMatch(volume.Authors, result);
        var publisherMatch   = ScoringTorrent.ScorePublisher(volume, result);
        var sizePlausibility = ScoringTorrent.ScoreSize(result, analysis.Type);
        var seederScore      = ScoringTorrent.ScoreSeeders(result);
        var formatScore      = ScoringTorrent.ScoreFormat(result, analysis.Type);

        var coverageBonus = MaxCoverageBonus * covered / Math.Max(1, missingIssues.Count);

        var total = Math.Clamp(
            titleMatch + yearMatch + authorMatch + publisherMatch
            + sizePlausibility + seederScore + formatScore + coverageBonus,
            0f, 100f);

        var details = new ScoreDetailsVolumePack(
            titleMatch, yearMatch, authorMatch, publisherMatch,
            sizePlausibility, seederScore, formatScore, coverageBonus);

        return new ScoredSearchResultVolumePack(result, total, details, analysis, covered, missingIssues.Count);
    }

    public static List<ScoredSearchResultVolumePack> ScoreAndSort(
        Volume volume,
        List<Issue> missingIssues,
        List<ProwlarrSearchResult> results)
        => results
            .Select(r => ScoringIndexerResult(volume, missingIssues, r))
            .OrderByDescending(r => r.Score)
            .ToList();

    private static int CountCoveredIssues(TorrentAnalysis analysis, List<Issue> missingIssues)
    {
        if (analysis.Type == "PACK")
        {
            if (analysis.Label.Contains("..."))
            {
                var sep = analysis.Label.IndexOf("...", StringComparison.Ordinal);
                if (sep > 0
                    && int.TryParse(analysis.Label.AsSpan(0, sep), out var start)
                    && int.TryParse(analysis.Label.AsSpan(sep + 3), out var end))
                    return missingIssues.Count(i => i.IssueNumber >= start && i.IssueNumber <= end);
            }

            // "Full" ou "?" — compilation présumée complète, confiance réduite (pas de plage vérifiée)
            return (int)Math.Round(missingIssues.Count * UnverifiedCoverageConfidence);
        }

        if (analysis.Type == "SINGLE"
            && analysis.Label.StartsWith('#')
            && int.TryParse(analysis.Label.AsSpan(1), out var num))
            return missingIssues.Any(i => i.IssueNumber == num) ? 1 : 0;

        return 0;
    }
}

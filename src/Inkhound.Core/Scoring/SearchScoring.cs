namespace Inkhound.Core.Scoring;

public static class SearchScoring
{
    // Score de pertinence (0–100) entre la requête tapée par l'utilisateur et le titre d'un
    // résultat de recherche — permet à InkhoundManager.SearchVolumesAsync de fusionner et
    // trier les résultats de plusieurs sources (ComicVine, Bedetheque, ...) par pertinence
    // plutôt que de les grouper par source.
    public static double ScoreTitleMatch(string query, string title, int countOfIssues = 0, string? language = null)
    {
        var queryNorm = TextSimilarity.Normalize(query);
        var titleNorm = TextSimilarity.Normalize(title);

        if (queryNorm.Length == 0 || titleNorm.Length == 0)
            return 0;

        double score;
        if (titleNorm == queryNorm)
            score = 100;
        else if (titleNorm.StartsWith(queryNorm, StringComparison.Ordinal))
            score = 85;
        else if (titleNorm.Contains(queryNorm) || queryNorm.Contains(titleNorm))
            score = 70;
        else
        {
            var maxLen = Math.Max(titleNorm.Length, queryNorm.Length);
            var distance = TextSimilarity.LevenshteinDistance(titleNorm, queryNorm);
            score = Math.Max(0, 60 * (1 - (double)distance / maxLen));
        }

        // Départage à pertinence égale : légère préférence pour les séries plus complètes.
        score += Math.Min(5, countOfIssues / 10.0);

        // Priorise les résultats identifiés comme étant en français — utile seulement pour les
        // sources qui exposent cette métadonnée (ex. Bedetheque) ; les autres (ex. ComicVine,
        // qui n'a pas de champ langue) ne reçoivent ni bonus ni pénalité, faute de signal fiable.
        if (!string.IsNullOrEmpty(language) && language.Equals("Français", StringComparison.OrdinalIgnoreCase))
            score += 10;

        return Math.Round(score, 1);
    }
}

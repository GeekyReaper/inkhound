namespace Inkhound.Core.Scoring;

public static class SearchScoring
{
    // Articles retirés en BORD de chaîne uniquement (début : "Les Trois Fantômes…", requête
    // utilisateur ; fin : forme Bedetheque "Trois Fantômes… (Les)"). Jamais au milieu — "de",
    // "des" internes sont conservés et restent discriminants.
    private static readonly HashSet<string> EdgeArticles = new(StringComparer.Ordinal)
    {
        "le", "la", "les", "l", "un", "une", "des", "the", "a", "an",
    };

    // Score de pertinence (0–100) entre la requête tapée par l'utilisateur et le titre d'un
    // résultat de recherche — permet à InkhoundManager.SearchVolumesAsync de fusionner et
    // trier les résultats de plusieurs sources (ComicVine, Bedetheque, ...) par pertinence
    // plutôt que de les grouper par source.
    // `preferredLanguage` est la langue favorisée par la configuration des sources
    // (ISourceService.PreferredLanguage, ex. le SearchLanguageFilter de Bedetheque) ; un résultat
    // dont `language` lui correspond reçoit un bonus. Les sources sans métadonnée langue (ex.
    // ComicVine) ne reçoivent ni bonus ni pénalité, faute de signal fiable.
    public static double ScoreTitleMatch(string query, string title, int countOfIssues = 0,
        string? language = null, string? preferredLanguage = null)
    {
        var queryNorm = NormalizeTitle(query);
        var titleNorm = NormalizeTitle(title);

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

        if (!string.IsNullOrEmpty(language) && !string.IsNullOrEmpty(preferredLanguage)
            && language.Equals(preferredLanguage, StringComparison.OrdinalIgnoreCase))
            score += 10;

        return Math.Round(score, 1);
    }

    // TextSimilarity.Normalize (accents, casse, ponctuation → espaces — "(Les)" devient " les" en
    // fin de chaîne) puis retrait d'un article en tête et/ou en queue, pour que "Les Trois Fantômes
    // de Tesla" (ComicVine / requête) et "Trois Fantômes de Tesla (Les)" (Bedetheque) se réduisent
    // à la même forme. Un titre réduit à un seul article est conservé tel quel.
    internal static string NormalizeTitle(string input)
    {
        var normalized = TextSimilarity.Normalize(input);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return normalized;

        var start = EdgeArticles.Contains(words[0]) ? 1 : 0;
        var end = EdgeArticles.Contains(words[^1]) ? words.Length - 1 : words.Length;
        if (end - start < 1) return normalized;

        return string.Join(' ', words[start..end]);
    }
}

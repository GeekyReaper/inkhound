namespace Inkhound.Core.Bedetheque.Catalog;

/// <summary>Une série du catalogue retenue par la recherche, avec son score de rapprochement.</summary>
/// <param name="Entry">Série du catalogue.</param>
/// <param name="Score">Score 1-1000 (voir <see cref="BedethequeCatalogIndex.Search"/>).</param>
public record BedethequeCatalogMatch(BedethequeCatalogEntry Entry, int Score);

/// <summary>
/// Index mémoire du catalogue local Bedetheque : titres pré-normalisés au chargement (pour ne pas
/// renormaliser ~77 000 titres à chaque requête), recherche par scan linéaire avec rapprochement
/// flou (port de <c>SearchSeriesInCatalog</c> / <c>ScoreCatalogMatch</c> de bdguest-scrapper).
/// Le tableau interne est remplacé atomiquement par <see cref="Load"/> — lectures sans verrou.
/// </summary>
public class BedethequeCatalogIndex
{
    // Sous ce ratio de similarité Levenshtein (1 - d / maxLen), un token de la requête est
    // considéré sans correspondance et la série est rejetée.
    private const double TokenSimilarityMin = 0.5;

    private sealed record CatalogItem(BedethequeCatalogEntry Entry, string Norm, string[] Tokens);

    private volatile CatalogItem[] _items = [];

    /// <summary>Nombre de séries indexées.</summary>
    public int Count => _items.Length;

    /// <summary>Au moins une série est indexée — la recherche est possible.</summary>
    public bool IsLoaded => _items.Length > 0;

    /// <summary>Remplace l'index par les entrées fournies (ids nuls / titres vides ignorés).</summary>
    public void Load(IEnumerable<BedethequeCatalogEntry> entries)
    {
        _items = entries
            .Where(e => e.Id != 0 && !string.IsNullOrWhiteSpace(e.Title))
            .Select(e =>
            {
                var tokens = BedethequeTitleNormalizer.Tokenize(
                    BedethequeTitleNormalizer.ReorderParentheticalPrefix(e.Title));
                return new CatalogItem(e, string.Join(' ', tokens), tokens);
            })
            .ToArray();
    }

    /// <summary>
    /// Recherche les séries dont le titre se rapproche de <paramref name="query"/>.
    /// Échelle de score : 1000 = titre normalisé identique ; 900 − écart de longueur (≤ 99) = la
    /// requête est un mot entier du titre ; 800 − écart (≤ 200) = la requête est une sous-chaîne ;
    /// 1-500 = rapprochement flou par tokens (0,7 × similarité moyenne + 0,3 × couverture du titre).
    /// </summary>
    /// <param name="query">Texte saisi par l'utilisateur.</param>
    /// <param name="language">Filtre exact sur <see cref="BedethequeCatalogEntry.Language"/>, <c>null</c> = toutes.</param>
    /// <param name="maxResults">Nombre maximum de résultats (tri score desc, puis titre).</param>
    /// <param name="minScore">Score minimum (plancher effectif 1 : un score 0 n'est jamais renvoyé).</param>
    public List<BedethequeCatalogMatch> Search(string query, string? language, int maxResults, int minScore)
    {
        var items = _items;
        if (items.Length == 0 || string.IsNullOrWhiteSpace(query)) return [];

        var queryTokens = BedethequeTitleNormalizer.Tokenize(query);
        var normQuery = string.Join(' ', queryTokens);
        if (normQuery.Length == 0) return [];

        var threshold = Math.Max(1, minScore);
        var scored = new List<(CatalogItem Item, int Score)>();
        foreach (var item in items)
        {
            if (language is not null
                && !string.Equals(item.Entry.Language, language, StringComparison.OrdinalIgnoreCase))
                continue;

            var score = ScoreMatch(normQuery, queryTokens, item.Norm, item.Tokens);
            if (score >= threshold) scored.Add((item, score));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Item.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxResults))
            .Select(s => new BedethequeCatalogMatch(s.Item.Entry, s.Score))
            .ToList();
    }

    private static int ScoreMatch(string normQuery, string[] queryTokens, string normTitle, string[] titleTokens)
    {
        if (normTitle.Length == 0) return 0;
        if (normTitle == normQuery) return 1000;
        if (Array.IndexOf(titleTokens, normQuery) >= 0)
            return 900 - Math.Min(99, normTitle.Length - normQuery.Length);
        if (normTitle.Contains(normQuery, StringComparison.Ordinal))
            return 800 - Math.Min(200, normTitle.Length - normQuery.Length);

        var tokenCovered = new bool[titleTokens.Length];
        double similaritySum = 0;

        foreach (var qt in queryTokens)
        {
            double best = 0;
            for (var j = 0; j < titleTokens.Length; j++)
            {
                var tt = titleTokens[j];
                double similarity;
                if (tt == qt) similarity = 1.0;
                else
                {
                    var maxLen = Math.Max(qt.Length, tt.Length);
                    if (maxLen == 0) continue;
                    // Au-delà de la moitié de la longueur, la similarité passe sous le seuil de
                    // toute façon : sortie anticipée de Levenshtein.
                    var maxDistance = (int)Math.Ceiling(maxLen * (1 - TokenSimilarityMin));
                    var d = BedethequeTitleNormalizer.Levenshtein(qt, tt, maxDistance);
                    similarity = d > maxDistance ? 0 : 1.0 - (double)d / maxLen;
                }
                if (similarity >= TokenSimilarityMin) tokenCovered[j] = true;
                if (similarity > best) best = similarity;
            }

            // Un token de la requête sans correspondance plausible → rejet de la série.
            if (best < TokenSimilarityMin) return 0;
            similaritySum += best;
        }

        var averageSimilarity = similaritySum / queryTokens.Length;

        double totalChars = 0, matchedChars = 0;
        for (var j = 0; j < titleTokens.Length; j++)
        {
            totalChars += titleTokens[j].Length;
            if (tokenCovered[j]) matchedChars += titleTokens[j].Length;
        }
        var titleCoverage = totalChars > 0 ? matchedChars / totalChars : 1.0;

        var combined = 0.7 * averageSimilarity + 0.3 * titleCoverage;
        return Math.Max(1, (int)Math.Round(combined * 500));
    }
}

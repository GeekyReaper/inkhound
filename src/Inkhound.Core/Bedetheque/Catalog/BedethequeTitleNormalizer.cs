using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Inkhound.Core.Bedetheque.Catalog;

/// <summary>
/// Normalisation des titres de séries pour la recherche dans le catalogue local (port de la logique
/// de <c>bdguest-scrapper</c>). Appliquée symétriquement au titre stocké et à la requête : article
/// de tête retiré, casse/accents/ponctuation neutralisés, synonymes canonicalisés
/// (<c>&amp;</c>/<c>and</c> → <c>et</c>, nombres en lettres → chiffres).
/// Volontairement indépendante d'ICU (table <see cref="BaseChar"/> de repli) : en conteneur avec
/// <c>InvariantGlobalization</c>, <c>string.Normalize</c> peut ne rien décomposer.
/// </summary>
public static class BedethequeTitleNormalizer
{
    private static readonly Regex ParentheticalPattern = new(@"\(([^()]*)\)", RegexOptions.Compiled);

    // Articles français courants que le site range en fin de titre : "Les Légendaires" →
    // "Légendaires (Les)". Couvre l'apostrophe droite et l'apostrophe typographique.
    private static readonly string[] LeadingArticles = ["l'", "l’", "les ", "le ", "la ", "des ", "une ", "un "];

    // "un"/"une"/"one" volontairement absents : trop souvent article, pas nombre.
    private static readonly Dictionary<string, string> TokenSynonyms = new(StringComparer.Ordinal)
    {
        ["and"] = "et",
        ["zero"] = "0", ["deux"] = "2", ["trois"] = "3", ["quatre"] = "4", ["cinq"] = "5",
        ["six"] = "6", ["sept"] = "7", ["huit"] = "8", ["neuf"] = "9", ["dix"] = "10",
        ["onze"] = "11", ["douze"] = "12", ["treize"] = "13", ["quatorze"] = "14", ["quinze"] = "15",
        ["seize"] = "16", ["vingt"] = "20", ["trente"] = "30", ["quarante"] = "40",
        ["cinquante"] = "50", ["soixante"] = "60", ["cent"] = "100", ["mille"] = "1000",
        ["two"] = "2", ["three"] = "3", ["four"] = "4", ["five"] = "5", ["seven"] = "7",
        ["eight"] = "8", ["nine"] = "9", ["ten"] = "10", ["eleven"] = "11", ["twelve"] = "12",
        ["thirteen"] = "13", ["fourteen"] = "14", ["fifteen"] = "15", ["sixteen"] = "16",
        ["seventeen"] = "17", ["eighteen"] = "18", ["nineteen"] = "19", ["twenty"] = "20",
        ["thirty"] = "30", ["forty"] = "40", ["fifty"] = "50", ["sixty"] = "60",
        ["seventy"] = "70", ["eighty"] = "80", ["ninety"] = "90", ["hundred"] = "100",
        ["thousand"] = "1000",
    };

    /// <summary>
    /// Replace en tête les groupes entre parenthèses du format Bedetheque :
    /// <c>"Schtroumpfs (Les)"</c> → <c>"Les Schtroumpfs"</c>, <c>"X (A) (B)"</c> → <c>"A B X"</c>.
    /// </summary>
    public static string ReorderParentheticalPrefix(string title)
    {
        var matches = ParentheticalPattern.Matches(title);
        if (matches.Count == 0) return title;

        var prefix = string.Join(" ", matches.Select(m => m.Groups[1].Value.Trim()).Where(s => s.Length > 0));
        var baseName = ParentheticalPattern.Replace(title, " ").Trim();
        return string.IsNullOrEmpty(prefix) ? baseName
             : string.IsNullOrEmpty(baseName) ? prefix
             : $"{prefix} {baseName}";
    }

    /// <summary>Retire un article français placé en tête (<c>les</c>, <c>l'</c>, <c>une</c>…).</summary>
    public static string StripLeadingArticle(string query)
    {
        var trimmed = query.TrimStart();
        foreach (var article in LeadingArticles)
        {
            if (trimmed.StartsWith(article, StringComparison.OrdinalIgnoreCase))
                return trimmed[article.Length..].TrimStart();
        }
        return trimmed;
    }

    /// <summary>
    /// Forme canonique d'un texte pour la comparaison : article de tête retiré, minuscules,
    /// <c>&amp;</c> → <c>et</c>, <c>$</c> → <c>s</c>, points supprimés (<c>I.R.$.</c> → <c>irs</c>),
    /// ligatures éclatées, accents retirés, tout caractère non alphanumérique remplacé par un espace
    /// unique.
    /// </summary>
    public static string NormalizeForSearch(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var lower = StripLeadingArticle(input).ToLowerInvariant()
            .Replace("&", " et ")
            .Replace("$", "s")
            .Replace(".", "")
            .Replace("œ", "oe").Replace("æ", "ae").Replace("ß", "ss");

        string decomposed;
        try { decomposed = lower.Normalize(NormalizationForm.FormD); }
        catch { decomposed = lower; }

        var sb = new StringBuilder(decomposed.Length);
        var lastSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            var c = BaseChar(ch);
            if (char.IsLetterOrDigit(c)) { sb.Append(c); lastSpace = false; }
            else if (!lastSpace) { sb.Append(' '); lastSpace = true; }
        }
        return sb.ToString().Trim();
    }

    // Repli pour les accents précomposés quand Normalize(FormD) est un no-op (InvariantGlobalization).
    private static char BaseChar(char c) => c switch
    {
        'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' => 'a',
        'ç' => 'c',
        'è' or 'é' or 'ê' or 'ë' => 'e',
        'ì' or 'í' or 'î' or 'ï' => 'i',
        'ñ' => 'n',
        'ò' or 'ó' or 'ô' or 'õ' or 'ö' or 'ø' => 'o',
        'ù' or 'ú' or 'û' or 'ü' => 'u',
        'ý' or 'ÿ' => 'y',
        _ => c,
    };

    /// <summary>Remplace chaque token par son synonyme canonique s'il en a un.</summary>
    public static string[] CanonicalizeTokens(string[] tokens)
    {
        var result = new string[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
            result[i] = TokenSynonyms.TryGetValue(tokens[i], out var canon) ? canon : tokens[i];
        return result;
    }

    /// <summary>
    /// Pipeline complet pour une requête utilisateur : normalisation puis tokens canonicalisés.
    /// Pour un titre du catalogue, appeler d'abord <see cref="ReorderParentheticalPrefix"/>.
    /// </summary>
    public static string[] Tokenize(string input)
        => CanonicalizeTokens(NormalizeForSearch(input).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Distance de Levenshtein (deux lignes glissantes). <paramref name="max"/> permet une sortie
    /// anticipée : dès que toute la ligne courante dépasse ce seuil, renvoie <c>max + 1</c>.
    /// </summary>
    public static int Levenshtein(string a, string b, int max = int.MaxValue)
    {
        int la = a.Length, lb = b.Length;
        if (Math.Abs(la - lb) > max) return max + 1;
        if (la == 0) return lb;
        if (lb == 0) return la;

        var prev = new int[lb + 1];
        var curr = new int[lb + 1];
        for (var j = 0; j <= lb; j++) prev[j] = j;

        for (var i = 1; i <= la; i++)
        {
            curr[0] = i;
            var rowMin = curr[0];
            for (var j = 1; j <= lb; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                if (curr[j] < rowMin) rowMin = curr[j];
            }
            if (rowMin > max) return max + 1;
            (prev, curr) = (curr, prev);
        }
        return prev[lb];
    }
}

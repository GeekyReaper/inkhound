using System.Text.RegularExpressions;
using Inkhound.Core.Models;

namespace Inkhound.Core.Scoring;

/// <summary>
/// Outils de comparaison « nom de série du volume » ↔ « titre d'un torrent », partagés par les règles de
/// rejet automatique. Tout passe par <see cref="TextSimilarity.Normalize"/> (casse, accents, ponctuation).
/// </summary>
internal static class SeriesNameMatcher
{
    // Mots sans valeur discriminante (articles, prépositions) et mots de format/langue présents dans la
    // quasi-totalité des titres de release : les garder gonflerait artificiellement le recoupement.
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "le", "la", "les", "l", "un", "une", "des", "de", "du", "d", "et", "the", "a", "an", "of", "en",
        "au", "aux", "sur", "sous", "bd", "comics", "comic", "pdf", "cbz", "cbr", "fr", "french"
    };

    // Premier marqueur de numérotation d'un titre de release : « T01 », « Tome(s) », « Vol », « #3 »,
    // ou une plage nue « 1-11 » / « 1.11 ». Le (?<![A-Za-z]) évite de prendre le « T » final d'un mot.
    private static readonly Regex TomeMarker = new(
        @"(?:(?<![A-Za-z])T\.?\s?\d{1,3}\b|\btomes?\b|\bvol\b|#\d|\b\d{1,3}[-.]\d{1,3}\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Parenthetical = new(@"\s*\([^)]*\)", RegexOptions.Compiled);

    /// <summary>Mots significatifs (normalisés, sans mots vides).</summary>
    public static List<string> Tokens(string text)
        => [.. TextSimilarity.Normalize(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !StopWords.Contains(t))];

    /// <summary>
    /// Partie « nom de série » d'un titre de release : le texte avant le premier marqueur de tome. Retourne
    /// <c>null</c> s'il n'y a aucun marqueur (titre nu : on ne peut pas conclure) ; si le marqueur ouvre le
    /// titre (« T01 La Duelliste… »), le titre entier sert de repli.
    /// </summary>
    public static string? SeriesPart(string torrentTitle)
    {
        var marker = TomeMarker.Match(torrentTitle);
        if (!marker.Success) return null;

        var prefix = torrentTitle[..marker.Index];
        return Tokens(prefix).Count > 0 ? prefix : torrentTitle;
    }

    /// <summary>
    /// Le texte <paramref name="haystack"/> désigne-t-il la série <paramref name="volumeTitle"/> ? Au moins
    /// la moitié des mots significatifs du titre du volume (qualificatif entre parenthèses retiré) doivent
    /// s'y retrouver, à un préfixe de ≥ 4 lettres près (« duel » / « duelliste »). Un titre en sigle
    /// (« N.É.O. », « I.R.$. ») est comparé lettres collées.
    /// </summary>
    public static bool MatchesSeries(string volumeTitle, string haystack)
    {
        var main = MainTitle(volumeTitle);
        var raw = TextSimilarity.Normalize(main).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (raw.Length >= 2 && raw.Count(t => t.Length == 1) >= raw.Length - 1)
        {
            var acronym = string.Concat(raw);
            var key = acronym[..Math.Max(2, acronym.Length - 1)];
            return TextSimilarity.Normalize(haystack).Replace(" ", string.Empty).Contains(key, StringComparison.Ordinal);
        }

        var wanted = Tokens(main);
        if (wanted.Count == 0) return true;

        var present = Tokens(haystack).ToHashSet(StringComparer.Ordinal);
        var matched = wanted.Count(w => TokenPresent(w, present));
        return matched / (double)wanted.Count >= 0.5;
    }

    /// <summary>
    /// Un nom d'auteur (≥ 4 lettres, volume et issue) figure-t-il dans le titre ? Les séries aux titres
    /// alternatifs (« Conquêtes » = « Le Sang du Dragon ») se reconnaissent à leur équipe créative.
    /// </summary>
    public static bool MentionsAnyAuthor(IEnumerable<VolumeAuthor> authors, string torrentTitle)
    {
        var titleTokens = Tokens(torrentTitle).ToHashSet(StringComparer.Ordinal);
        return authors
            .SelectMany(a => Tokens(a.Name))
            .Any(t => t.Length >= 4 && titleTokens.Contains(t));
    }

    private static string MainTitle(string title)
    {
        var main = Parenthetical.Replace(title, string.Empty).Trim();
        return main.Length > 0 ? main : title;
    }

    private static bool TokenPresent(string wanted, HashSet<string> present)
    {
        if (present.Contains(wanted)) return true;
        if (wanted.Length < 4) return false;
        return present.Any(p => p.Length >= 4 && (p.StartsWith(wanted, StringComparison.Ordinal) || wanted.StartsWith(p, StringComparison.Ordinal)));
    }
}

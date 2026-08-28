using System.IO;
using System.Text.RegularExpressions;

namespace Inkhound.Core.Analysis;

public record TorrentAnalysis(string Type, string Label);

public static class TorrentTypeAnalyzer
{
    private const long MB = 1_048_576L;

    // Variantes avec et sans accents pour robustesse avec les titres internationaux
    private static readonly string[] PackKeywords =
        ["pack", "intégral", "intégrale", "integrale", "integral", "complet", "complète", "complete", "collection", "omnibus"];

    // Extraction du numéro d'issue depuis un nom de fichier individuel (ex : "Batman - T01 - Titre.cbz")
    private static readonly Regex FilePrefixNumberRegex     = new(@"(?:T|Tome|Vol|Volume|#)[\s.]?0*(\d{1,3})\b",    RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // 0+ suivi d'un nombre optionnel : "001" → 1, "02" → 2, et "0"/"00"/"000" → 0 (groupe vide).
    // Le préfixe 0+ obligatoire évite de capturer un petit nombre incident non zéro-paddé
    // (ex : "Vol. 3" dans "Batman (Vol. 3) 027" — on veut 27, pas 3).
    private static readonly Regex FileZeroPaddedNumberRegex = new(@"\b0+(\d{0,3})\b",                               RegexOptions.Compiled);

    // \b final sur chaque groupe capturant : empêche une troncature d'un nombre à 4 chiffres
    // (ex : une année "2020") en une capture à 3 chiffres qui serait prise pour une fin de plage.
    // Accepte "T"/"Tome"/"Tomes" (singulier ou pluriel) et "a" non accentué en plus de "à", pour
    // couvrir la convention scène "Tomes.01.a.24" en plus de "T1 à T41".
    private static readonly Regex FrenchRangeRegex  = new(@"(?:T|Tomes?)[\s.]?(\d{1,3})\b[\s.]*[aà][\s.]*(?:T|Tomes?)?[\s.]?(\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BracketRangeRegex = new(@"\[?T(\d{1,3})\b\s*\.\s*T(\d{1,3})\b\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DashRangeRegex    = new(@"(?:T|Vol|Tome|#)[\s.]?(\d{1,3})\b[\s.]*[-–][\s.]*(?:T|Vol|Tome|#)?[\s.]?(\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SingleNumberRegex = new(@"(?:T|Tome|Vol|#)[\s.]?(\d{1,3})\b",   RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Extrait le numéro d'issue d'un nom de fichier de torrent (ex : "Batman - T01 - Titre.cbz" → 1).
    /// Retourne null si aucun numéro n'est trouvé.
    /// </summary>
    public static int? ExtractIssueNumber(string filename)
    {
        var stem = Path.GetFileNameWithoutExtension(filename);

        // Priorité 1 : préfixe explicite (T01, Tome 1, Vol 3, #12)
        var m = FilePrefixNumberRegex.Match(stem);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n1)) return n1;

        // Priorité 2 : nombre zéro-paddé sans préfixe (001, 02, ...) ou zéro seul (0, 00, 000 → 0)
        m = FileZeroPaddedNumberRegex.Match(stem);
        if (m.Success)
        {
            var digits = m.Groups[1].Value;
            if (digits.Length == 0) return 0;
            if (int.TryParse(digits, out var n2)) return n2;
        }

        return null;
    }

    /// <summary>
    /// Analyse le titre d'un résultat Prowlarr/NZB. <paramref name="maxIssueNumber"/> (ex : Volume.CountOfIssues)
    /// sert de garde-fou : une plage détectée dont la borne finale le dépasse est rejetée (ce n'est pas une plage
    /// de tomes plausible — probablement une année ou une valeur mal extraite) et on retombe sur les règles suivantes.
    /// </summary>
    public static TorrentAnalysis Analyze(string title, long sizeBytes, int? maxIssueNumber = null)
    {
        // Règle 1 — plage française : T1.à.T41
        if (TryMatchRange(FrenchRangeRegex, title, maxIssueNumber, out var range1)) return range1;

        // Règle 2 — plage entre crochets : [T01.T41]
        if (TryMatchRange(BracketRangeRegex, title, maxIssueNumber, out var range2)) return range2;

        // Règle 3 — plage tiret : T1-T12, T1–T12
        if (TryMatchRange(DashRangeRegex, title, maxIssueNumber, out var range3)) return range3;

        // Règle 4 — mots-clés pack (variantes accentuées et non accentuées)
        var lower = title.ToLowerInvariant();
        if (PackKeywords.Any(kw => lower.Contains(kw)))
            return new("PACK", "Full");

        // Règle 5 — numéro seul : T41, Tome.41, Vol 3, #12
        var m = SingleNumberRegex.Match(title);
        if (m.Success)
            return sizeBytes < 500 * MB
                ? new("SINGLE", $"#{m.Groups[1].Value}")
                : new("PACK", "?");

        // Règle 6 — fallback taille
        if (sizeBytes > 500 * MB) return new("PACK", "?");
        if (sizeBytes < 200 * MB) return new("SINGLE", "?");
        return new("UNKNOWN", "?");
    }

    private static bool TryMatchRange(Regex regex, string title, int? maxIssueNumber, out TorrentAnalysis analysis)
    {
        var m = regex.Match(title);
        if (m.Success
            && int.TryParse(m.Groups[1].Value, out var start)
            && int.TryParse(m.Groups[2].Value, out var end)
            && end >= start
            && (!maxIssueNumber.HasValue || maxIssueNumber.Value <= 0 || end <= maxIssueNumber.Value))
        {
            analysis = new("PACK", $"{start}...{end}");
            return true;
        }

        analysis = null!;
        return false;
    }
}

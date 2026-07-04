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
    private static readonly Regex FileZeroPaddedNumberRegex = new(@"\b0+(\d{1,3})\b",                               RegexOptions.Compiled);

    private static readonly Regex FrenchRangeRegex  = new(@"T(\d{1,3})[\s.]*à[\s.]*T?(\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BracketRangeRegex = new(@"\[?T(\d{1,3})\s*\.\s*T(\d{1,3})\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DashRangeRegex    = new(@"(?:T|Vol|Tome|#)[\s.]?(\d{1,3})[\s.]*[-–][\s.]*(?:T|Vol|Tome|#)?[\s.]?(\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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

        // Priorité 2 : nombre zéro-paddé sans préfixe (001, 02, ...)
        m = FileZeroPaddedNumberRegex.Match(stem);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n2)) return n2;

        return null;
    }

    public static TorrentAnalysis Analyze(string title, long sizeBytes)
    {
        // Règle 1 — plage française : T1.à.T41
        var m = FrenchRangeRegex.Match(title);
        if (m.Success)
            return new("PACK", $"{int.Parse(m.Groups[1].Value)}...{int.Parse(m.Groups[2].Value)}");

        // Règle 2 — plage entre crochets : [T01.T41]
        m = BracketRangeRegex.Match(title);
        if (m.Success)
            return new("PACK", $"{int.Parse(m.Groups[1].Value)}...{int.Parse(m.Groups[2].Value)}");

        // Règle 3 — plage tiret : T1-T12, T1–T12
        m = DashRangeRegex.Match(title);
        if (m.Success)
            return new("PACK", $"{int.Parse(m.Groups[1].Value)}...{int.Parse(m.Groups[2].Value)}");

        // Règle 4 — mots-clés pack (variantes accentuées et non accentuées)
        var lower = title.ToLowerInvariant();
        if (PackKeywords.Any(kw => lower.Contains(kw)))
            return new("PACK", "Full");

        // Règle 5 — numéro seul : T41, Tome.41, Vol 3, #12
        m = SingleNumberRegex.Match(title);
        if (m.Success)
            return sizeBytes < 500 * MB
                ? new("SINGLE", $"#{m.Groups[1].Value}")
                : new("PACK", "?");

        // Règle 6 — fallback taille
        if (sizeBytes > 500 * MB) return new("PACK", "?");
        if (sizeBytes < 200 * MB) return new("SINGLE", "?");
        return new("UNKNOWN", "?");
    }
}

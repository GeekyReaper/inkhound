using System.Text.RegularExpressions;

namespace Inkhound.Core.Analysis;

public record TorrentAnalysis(string Type, string Label);

public static class TorrentTypeAnalyzer
{
    private const long MB = 1_048_576L;

    // Variantes avec et sans accents pour robustesse avec les titres internationaux
    private static readonly string[] PackKeywords =
        ["pack", "intégral", "intégrale", "integrale", "integral", "complet", "complète", "complete", "collection", "omnibus"];

    private static readonly Regex FrenchRangeRegex  = new(@"T(\d{1,3})[\s.]*à[\s.]*T?(\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BracketRangeRegex = new(@"\[?T(\d{1,3})\s*\.\s*T(\d{1,3})\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DashRangeRegex    = new(@"(?:T|Vol|Tome|#)[\s.]?(\d{1,3})[\s.]*[-–][\s.]*(?:T|Vol|Tome|#)?[\s.]?(\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SingleNumberRegex = new(@"(?:T|Tome|Vol|#)[\s.]?(\d{1,3})\b",   RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

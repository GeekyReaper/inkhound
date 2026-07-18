using System.Text.RegularExpressions;

namespace Inkhound.Core.Analysis;

public record ParsedVolumeName(string Title, int? Year, int? MinTomes, int? IssueNumber, List<string>? metadata);

public static partial class SourceAnalyzer
{
    private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CBR", "CBZ", "PDF", "Ebook", "epub", "NoTag", "NoTAG", "NOTAG",
        "FR", "FRENCH", "EN", "ENGLISH", "VF", "VO",
        "BD", "INTEGRALE", "COLLECTION", "HS", "MANGA",
    };
    private static readonly HashSet<string> SplitString = new(StringComparer.OrdinalIgnoreCase)
    {
        "-", ".", "+", ","
    };

    #region REGEX
    [GeneratedRegex(@"\s*(\d{4})\s*")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"#\s*0*(\d+)")]
    private static partial Regex HashNumberRegex();

    [GeneratedRegex(@"\b[T,t,V,v][omevlu]*[\s\.\-_]*0*(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TomeNumberRegex();

    [GeneratedRegex(@"^0*(\d+)")]
    private static partial Regex LeadingNumberRegex();

    [GeneratedRegex(@"[-–_]\s*0*(\d{1,4})\s*[-–_]")]
    private static partial Regex DashEnclosedNumberRegex();


    [GeneratedRegex(@"\b0*(\d{1,4})\b")]
    private static partial Regex IsolatedNumberRegex();

    [GeneratedRegex(@"[\[{][^\[\]{}]*[\]}]")]
    private static partial Regex BracketTagRegex();

    [GeneratedRegex(@"\(\s*(\d+)\s*tomes?\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex TomesCountRegex();

    [GeneratedRegex(@"\(\s*(\d{4})\s*\)")]
    private static partial Regex YearParenRegex();
    #endregion

    private static string StripNoiseWords(IEnumerable<string> noiseWords, string input)
    {
        var pattern = $@"\b({string.Join("|", noiseWords.Select(Regex.Escape))})\b";
        var result = Regex.Replace(input, pattern, " ", RegexOptions.IgnoreCase);
        return string.Join(" ", result.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static List<ParsedVolumeName> ExtractVolumeNameCandidates(string input)
    {
        var result = new List<ParsedVolumeName>();
        var s = input.Trim();
        // 1. Strip bracket tags: [BD], [cbr], [aATAa], {CBR & CBZ], etc.
        s = BracketTagRegex().Replace(s, " ");

        s = StripNoiseWords(NoiseWords, s);



        //s = StripNoiseWords2(s);

        // 2. Extract (N Tomes) → minTomes
        int? minTomes = null;
        var tomesMatch = TomesCountRegex().Match(s);
        if (tomesMatch.Success && int.TryParse(tomesMatch.Groups[1].Value, out var tc))
        {
            minTomes = tc;
            s = s.Remove(tomesMatch.Index, tomesMatch.Length);
        }

        // 3. Extract standalone (YYYY) → year
        int? year = null;
        var yearMatch = YearParenRegex().Match(s);
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var y))
        {
            year = y;
            s = s.Remove(yearMatch.Index, yearMatch.Length);
        }


        // Clean split carateres

        var charClass = "[" + string.Concat(SplitString.Select(Regex.Escape)) + "]";

        s = Regex.Replace(s, $@"({charClass}\s*){{2,}}", "");  // consécutifs
        s = Regex.Replace(s, $@"{charClass}\s*$", "");           // en fin
        s = Regex.Replace(s, $@"^\s*{charClass}", "");           // en début

        s = s.Trim();

        var alltitles = new List<string>();

        // Find split character
        foreach (var t in SplitString)
        {
            var segment = s.Split(t, StringSplitOptions.RemoveEmptyEntries & StringSplitOptions.TrimEntries);
            if (segment != null && segment.Count() > 1)
            {
                var titles = new List<string>();
                foreach (var seg in segment)
                {

                    var tomematch = TomeNumberRegex().Match(seg);
                    var yearmatch = YearRegex().Match(seg);
                    if (tomematch.Success && int.TryParse(tomematch.Groups[1].Value, out var tomeNumber))
                    {
                        minTomes = minTomes == null ? tomeNumber : minTomes > tomeNumber ? minTomes : tomeNumber;
                    }
                    else if (yearmatch.Success && int.TryParse(yearmatch.Groups[1].Value, out var tomyear))
                    {
                        year = year == null ? tomyear : (year < tomyear) ? year : tomyear;
                    }
                    else
                    {
                        titles.Add(seg);
                    }

                }
                foreach (var title in titles)
                {
                    result.Add(new ParsedVolumeName(title, year, minTomes, 0, titles));
                }

                result.Add(new ParsedVolumeName(string.Join(" ", titles), year, minTomes, 0, null));
            }
        }
        if (result.Count == 0)
        {
            result.Add(new ParsedVolumeName(s, year, minTomes, 0, null));
        }
        return result;
    }


    public static int? ParseIssueNumber(string filename)
    {
        var name = Path.GetFileNameWithoutExtension(filename);

        // 1. Hash format: "Batman #012"
        var m = HashNumberRegex().Match(name);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) return n;

        // 2. French "Tome" format: T01, T 02
        m = TomeNumberRegex().Match(name);
        if (m.Success && int.TryParse(m.Groups[1].Value, out n)) return n;

        // 3. Starts with a number: "01 - ", "002 "
        m = LeadingNumberRegex().Match(name);
        if (m.Success && int.TryParse(m.Groups[1].Value, out n)) return n;

        // 4. Number enclosed in dashes: " - 012 - "
        m = DashEnclosedNumberRegex().Match(name);
        if (m.Success && int.TryParse(m.Groups[1].Value, out n)) return n;

        // 5. First isolated integer, excluding years (1800–2099)
        return IsolatedNumberRegex().Matches(name)
            .Select(x => int.TryParse(x.Groups[1].Value, out var v) ? v : (int?)null)
            .FirstOrDefault(v => v.HasValue && !(v >= 1800 && v <= 2099));
    }
}

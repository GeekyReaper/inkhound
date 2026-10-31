using System.Text.RegularExpressions;
using Inkhound.Core.Models;
using Inkhound.Core.Prowlarr;

namespace Inkhound.Core.Scoring;

/// <summary>
/// Règles génériques de rejet automatique d'un résultat de recherche : un résultat rejeté est ramené à un
/// score de 0 (comme un torrent banni), ce qui évite d'avoir à le bannir à la main.
/// <para>
/// Chaque règle a été établie et validée sur une copie de la base de production : elle écarte des torrents
/// que l'utilisateur avait bannis <b>et ne rejette aucun de ses downloads conservés</b> (voir
/// <c>Inkhound.Core.Tests/Scoring/Fixtures</c> et <c>TorrentRejectionRulesDatasetTests</c>). Toute nouvelle
/// règle doit repasser ce test de non-régression ; les pistes écartées sont consignées dans le CLAUDE.md
/// d'Inkhound.Core.
/// </para>
/// </summary>
internal static class TorrentRejectionRules
{
    // ── Règles « série » ────────────────────────────────────────────────────

    // Plage de tomes : « T01 à T09 », « T1-T12 », « Tomes 1 à 20 », « [T01.T25] ».
    private static readonly Regex TomeRange = new(
        @"\b(?:T\.?\s?\d{1,3}\s*(?:[-–.]|\s?a\s?|\s?à\s?)\s*T?\.?\s?\d{1,3}|tomes?\s*\d{1,3}\b\s*(?:[-–]|a|à|et)\s*\d{1,3}\b|\[T\d{1,3}\.T\d{1,3}\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SingleTomeMarker = new(
        @"(?:\bT\.?\s?\d{1,3}\b|\btome\s?\d{1,3}\b|#\d)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CompilationWord = new(
        @"\b(?:integral\w*|pack|collection|complet\w*)\b", RegexOptions.Compiled);

    private static readonly Regex Year = new(@"(?<!\d)((?:19|20)\d{2})(?!\d)", RegexOptions.Compiled);

    // ── Règles « format / langue / contenu » ────────────────────────────────

    private static readonly Regex EbookOnlyFormat = new(@"\b(?:epub|mobi|azw3?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ComicFormat = new(@"\b(?:cbz|cbr|pdf|comic)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Sensible à la casse : « EN » (anglais) ne doit pas se confondre avec le mot « en ».
    private static readonly Regex EnglishMarker = new(@"(?<![A-Za-z])(?:EN|ENG|ENGLISH)(?![A-Za-z/])", RegexOptions.Compiled);
    private static readonly Regex FrenchMarker = new(@"\b(?:FR|FRENCH|VF|MULTI)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NonBdKeyword = new(
        @"\b(?:jdr|rpg|jeu de role|artbook|art book|audiobook|mp3|flac|dvd|mkv)\b", RegexOptions.Compiled);

    /// <summary>
    /// Première règle qui rejette ce résultat, ou <c>null</c> s'il est acceptable. <paramref name="issue"/>
    /// est <c>null</c> pour une recherche au niveau volume : les règles qui exigent une issue précise
    /// (année anachronique) sont alors ignorées.
    /// </summary>
    public static TorrentRejection? Evaluate(Volume volume, Issue? issue, ProwlarrSearchResult result)
    {
        var title = result.Title;

        if (IsNonComicFormat(title))
            return new(TorrentRejection.NonComicFormatCode, "Not a comic archive (e-book format only).");
        if (IsEnglishOnly(title))
            return new(TorrentRejection.EnglishOnlyCode, "English-only release.");
        if (IsNonBdContent(title))
            return new(TorrentRejection.NonBdContentCode, "Not a comic (RPG, art book, audio or video content).");

        IEnumerable<VolumeAuthor> authors = issue is null ? volume.Authors : issue.Authors.Concat(volume.Authors);
        if (IsWrongSeries(volume.Title, authors, title))
            return new(TorrentRejection.WrongSeriesCode, "The torrent belongs to another series.");

        if (issue is { Year: { } issueYear } && IsAnachronisticYear(issueYear, title))
            return new(TorrentRejection.AnachronisticYearCode, "Dated before this issue was published.");

        return null;
    }

    /// <summary>
    /// Le titre annonce un tome (« T01 », « Tome 2 », « #3 », plage) mais le nom de série qui le précède
    /// n'est pas celui du volume — sauf si un auteur du volume/de l'issue est cité. Sans marqueur de tome
    /// (titre nu : « Les Petits Hommes »), on ne conclut pas.
    /// </summary>
    internal static bool IsWrongSeries(string volumeTitle, IEnumerable<VolumeAuthor> authors, string torrentTitle)
    {
        var seriesPart = SeriesNameMatcher.SeriesPart(torrentTitle);
        if (seriesPart is null) return false;

        return !SeriesNameMatcher.MatchesSeries(volumeTitle, seriesPart)
            && !SeriesNameMatcher.MentionsAnyAuthor(authors, torrentTitle);
    }

    /// <summary>
    /// Torrent à tome unique (ni plage, ni intégrale/pack/collection) dont la plus grande année est d'au moins
    /// deux ans antérieure à celle de l'issue : l'album n'existait pas encore. Les compilations sont exclues
    /// (leur année est celle du scan ou du début de série).
    /// </summary>
    internal static bool IsAnachronisticYear(int issueYear, string torrentTitle)
    {
        if (!SingleTomeMarker.IsMatch(torrentTitle) || TomeRange.IsMatch(torrentTitle)) return false;
        if (CompilationWord.IsMatch(TextSimilarity.Normalize(torrentTitle))) return false;

        var years = Year.Matches(torrentTitle).Select(m => int.Parse(m.Groups[1].Value)).ToList();
        return years.Count > 0 && years.Max() < issueYear - 1;
    }

    /// <summary>Format livre numérique seul (epub/mobi/azw), sans archive de BD (cbz/cbr/pdf/comic).</summary>
    internal static bool IsNonComicFormat(string torrentTitle)
        => EbookOnlyFormat.IsMatch(torrentTitle) && !ComicFormat.IsMatch(torrentTitle);

    /// <summary>Marqueur anglais isolé (EN/ENG/ENGLISH) sans marqueur français ni multilingue.</summary>
    internal static bool IsEnglishOnly(string torrentTitle)
        => EnglishMarker.IsMatch(torrentTitle) && !FrenchMarker.IsMatch(torrentTitle);

    /// <summary>Jeu de rôle, art book, audio ou vidéo.</summary>
    internal static bool IsNonBdContent(string torrentTitle)
        => NonBdKeyword.IsMatch(TextSimilarity.Normalize(torrentTitle));
}

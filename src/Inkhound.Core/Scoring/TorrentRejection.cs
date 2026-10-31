namespace Inkhound.Core.Scoring;

/// <summary>
/// Motif de rejet automatique d'un résultat de recherche (voir <see cref="TorrentRejectionRules"/>).
/// <paramref name="Code"/> est stable (testable, utilisable côté UI) ; <paramref name="Message"/> est
/// le texte affiché en infobulle.
/// </summary>
public record TorrentRejection(string Code, string Message)
{
    public const string WrongSeriesCode = "wrong-series";
    public const string AnachronisticYearCode = "anachronistic-year";
    public const string NonComicFormatCode = "non-comic-format";
    public const string EnglishOnlyCode = "english-only";
    public const string NonBdContentCode = "non-bd-content";
}

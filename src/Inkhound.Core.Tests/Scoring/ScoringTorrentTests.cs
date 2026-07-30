using Inkhound.Core.Models;
using Inkhound.Core.Prowlarr;
using Inkhound.Core.Scoring;

namespace Inkhound.Core.Tests.Scoring;

public class ScoringTorrentTests
{
    private const long Mb = 1_048_576L;

    private static Volume MakeVolume(List<VolumeAuthor> authors)
        => new() { Title = "Elfes", CountOfIssues = 30, Authors = authors };

    private static Issue MakeIssue(int issueNumber, int? year, List<VolumeAuthor> authors)
        => new() { IssueNumber = issueNumber, Year = year, Authors = authors };

    private static ProwlarrSearchResult MakeResult(string title, long sizeBytes)
        => new(title, null, sizeBytes, 10, 2, 0, "guid", 1, "Indexer", "torrent", null, null);

    [Fact]
    public void ScoringIndexerResult_SingleAvecAnneeCorrespondante_ScoreHautQueSansAnnee()
    {
        var volume = MakeVolume([]);
        var issue = MakeIssue(issueNumber: 27, year: 2020, authors: []);

        var withYear = MakeResult("Jarry - Elfes - Tome 27 - 2020 [aATAa] [cbr]", 30 * Mb);
        var withoutYear = MakeResult("Jarry - Elfes - Tome 27 [aATAa] [cbr]", 30 * Mb);

        var scoreWith = ScoringTorrent.ScoringIndexerResult(volume, issue, withYear).Score;
        var scoreWithout = ScoringTorrent.ScoringIndexerResult(volume, issue, withoutYear).Score;

        Assert.True(scoreWith > scoreWithout,
            $"Score avec année ({scoreWith}) devrait être supérieur au score sans année ({scoreWithout})");
    }

    [Fact]
    public void ScoringIndexerResult_SingleAvecAuteurPresent_ScoreHautQueSansAuteur()
    {
        var volume = MakeVolume([]);
        var issue = MakeIssue(issueNumber: 27, year: null, authors: [new VolumeAuthor("Bertrand Benoît", "colorist")]);

        var withAuthor = MakeResult("Jarry - Benoît - Elfes - Tome 27 [aATAa] [cbr]", 30 * Mb);
        var withoutAuthor = MakeResult("Jarry - Elfes - Tome 27 [aATAa] [cbr]", 30 * Mb);

        var scoreWith = ScoringTorrent.ScoringIndexerResult(volume, issue, withAuthor).Score;
        var scoreWithout = ScoringTorrent.ScoringIndexerResult(volume, issue, withoutAuthor).Score;

        Assert.True(scoreWith > scoreWithout,
            $"Score avec auteur ({scoreWith}) devrait être supérieur au score sans auteur ({scoreWithout})");
    }

    [Fact]
    public void ScoringIndexerResult_AuteurDuVolumeUtiliseSiIssueSansCredits()
    {
        var volume = MakeVolume([new VolumeAuthor("Bertrand Benoît", "colorist")]);
        var issue = MakeIssue(issueNumber: 27, year: null, authors: []); // pas de credits au niveau issue

        var withAuthor = MakeResult("Jarry - Benoît - Elfes - Tome 27 [aATAa] [cbr]", 30 * Mb);
        var withoutAuthor = MakeResult("Jarry - Elfes - Tome 27 [aATAa] [cbr]", 30 * Mb);

        var scoreWith = ScoringTorrent.ScoringIndexerResult(volume, issue, withAuthor).Score;
        var scoreWithout = ScoringTorrent.ScoringIndexerResult(volume, issue, withoutAuthor).Score;

        Assert.True(scoreWith > scoreWithout,
            $"Score avec repli sur volume.Authors ({scoreWith}) devrait être supérieur au score sans auteur ({scoreWithout})");
    }

    [Fact]
    public void ScoringIndexerResult_TitreDuBugRapporte_EstClasseSingleAvecScoreEleve()
    {
        var volume = MakeVolume([]);
        var issue = MakeIssue(issueNumber: 27, year: 2020,
            authors: [new VolumeAuthor("Bertrand Benoît", "artist, colorist")]);

        var result = MakeResult("[BD] Jarry - Benoît - Elfes - Tome 27 - 2020 [aATAa] [cbr]", 30 * Mb);

        var scored = ScoringTorrent.ScoringIndexerResult(volume, issue, result);

        Assert.Equal("SINGLE", scored.Analysis.Type);
        Assert.Equal("#27", scored.Analysis.Label);
        Assert.True(scored.Score >= 70f, $"Score attendu >= 70, obtenu {scored.Score}");
    }
}

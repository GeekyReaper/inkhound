using Inkhound.Core.Models;
using Inkhound.Core.Prowlarr;
using Inkhound.Core.Scoring;

namespace Inkhound.Core.Tests.Scoring;

public class ScoringTorrentTests
{
    private const long Mb = 1_048_576L;

    private static Volume MakeVolume(List<VolumeAuthor> authors, string title = "Elfes", string? publisher = null, int? year = null)
        => new() { Title = title, CountOfIssues = 30, Authors = authors, Publisher = publisher, Year = year };

    private static Issue MakeIssue(int issueNumber, int? year, List<VolumeAuthor> authors)
        => new() { IssueNumber = issueNumber, Year = year, Authors = authors };

    private static ProwlarrSearchResult MakeResult(string title, long sizeBytes)
        => new(title, null, sizeBytes, 10, 2, 0, "guid", 1, "Indexer", "torrent", null, null);

    private static ProwlarrSearchResult MakeResult(string title, long sizeBytes, int seeders, string protocol = "torrent")
        => new(title, null, sizeBytes, seeders, 2, 0, "guid", 1, "Indexer", protocol, null, null);

    private static ProwlarrSearchResult MakeResult(string title, long sizeBytes, string? downloadUrl)
        => new(title, null, sizeBytes, 10, 2, 0, "guid", 1, "Indexer", "torrent", downloadUrl, null);

    private static IssueTorrentBan MakeBan(string downloadUrl = "", string torrentTitle = "")
        => new() { Id = Guid.NewGuid(), IssueId = Guid.NewGuid(), DownloadUrl = downloadUrl, TorrentTitle = torrentTitle, CreatedAt = DateTime.UtcNow };

    // Titre « parfait » : SINGLE #27 avec année, auteur et format reconnus (cf. le cas du bug rapporté).
    private const string PerfectSingleTitle = "[BD] Jarry - Benoît - Elfes - Tome 27 - 2020 [aATAa] [cbr]";

    private static (Volume Volume, Issue Issue) MakePerfectSingleContext()
        => (MakeVolume([]),
            MakeIssue(issueNumber: 27, year: 2020, authors: [new VolumeAuthor("Bertrand Benoît", "artist, colorist")]));

    [Fact]
    public void ScoringIndexerResult_TorrentBanni_ScoreZeroEtDrapeau()
    {
        var (volume, issue) = MakePerfectSingleContext();
        var result = MakeResult(PerfectSingleTitle, 200 * Mb, downloadUrl: "https://tracker/download/abc");
        var bans = TorrentBanIndex.From([MakeBan(downloadUrl: "https://tracker/download/abc")]);

        var scored = ScoringTorrent.ScoringIndexerResult(volume, issue, result, bans);

        Assert.True(scored.Banned);
        Assert.Equal(0f, scored.Score);
        // Sans le ban, ce SINGLE parfait score très haut : c'est bien le ban qui écrase le score.
        Assert.True(ScoringTorrent.ScoringIndexerResult(volume, issue, result).Score > 70f);
    }

    [Fact]
    public void ScoringIndexerResult_BanDUnAutreTorrent_ScoreInchange()
    {
        var (volume, issue) = MakePerfectSingleContext();
        var result = MakeResult(PerfectSingleTitle, 200 * Mb, downloadUrl: "https://tracker/download/abc");
        var bans = TorrentBanIndex.From([MakeBan(downloadUrl: "https://tracker/download/other", torrentTitle: "Autre release")]);

        var withBans = ScoringTorrent.ScoringIndexerResult(volume, issue, result, bans);
        var without = ScoringTorrent.ScoringIndexerResult(volume, issue, result);

        Assert.False(withBans.Banned);
        Assert.Equal(without.Score, withBans.Score, precision: 3);
    }

    [Fact]
    public void ScoreAndSort_ResultatBanni_RelegueEnFinDeListe()
    {
        var (volume, issue) = MakePerfectSingleContext();
        var banned = MakeResult(PerfectSingleTitle, 200 * Mb, downloadUrl: "https://tracker/download/abc");
        var mediocre = MakeResult("Elfes - Tome 27 [cbr]", 200 * Mb, downloadUrl: "https://tracker/download/xyz");
        var bans = TorrentBanIndex.From([MakeBan(downloadUrl: "https://tracker/download/abc")]);

        var sorted = ScoringTorrent.ScoreAndSort(volume, issue, [banned, mediocre], bans);

        Assert.Equal(mediocre.DownloadUrl, sorted[0].Result.DownloadUrl);
        Assert.True(sorted[^1].Banned);
        Assert.Equal(0f, sorted[^1].Score);
    }

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
    public void ScoringIndexerResult_AuteurDuVolumeDetecteMemeAvecCreditsIssueNonVides()
    {
        // L'issue a des credits, mais sans rapport avec le nom présent dans le titre : l'union
        // avec volume.Authors doit quand même permettre de détecter "Benoît".
        var volume = MakeVolume([new VolumeAuthor("Bertrand Benoît", "colorist")]);
        var issue = MakeIssue(issueNumber: 27, year: null, authors: [new VolumeAuthor("Quelqu'un Dautre", "writer")]);

        var withVolumeAuthor = MakeResult("Jarry - Benoît - Elfes - Tome 27 [aATAa] [cbr]", 30 * Mb);
        var withoutAuthor = MakeResult("Jarry - Elfes - Tome 27 [aATAa] [cbr]", 30 * Mb);

        var scoreWith = ScoringTorrent.ScoringIndexerResult(volume, issue, withVolumeAuthor).Score;
        var scoreWithout = ScoringTorrent.ScoringIndexerResult(volume, issue, withoutAuthor).Score;

        Assert.True(scoreWith > scoreWithout,
            $"Score avec auteur du volume ({scoreWith}) devrait être supérieur au score sans ({scoreWithout})");
    }

    [Fact]
    public void ScoringIndexerResult_EditeurDuVolumePresent_ScoreHautQueSansEditeur()
    {
        var volume = MakeVolume([], publisher: "Soleil");
        var issue = MakeIssue(issueNumber: 27, year: null, authors: []);

        var withPublisher = MakeResult("Jarry - Elfes - Tome 27 Soleil [aATAa] [cbr]", 30 * Mb);
        var withoutPublisher = MakeResult("Jarry - Elfes - Tome 27 [aATAa] [cbr]", 30 * Mb);

        var scoreWith = ScoringTorrent.ScoringIndexerResult(volume, issue, withPublisher).Score;
        var scoreWithout = ScoringTorrent.ScoringIndexerResult(volume, issue, withoutPublisher).Score;

        Assert.True(scoreWith > scoreWithout,
            $"Score avec éditeur ({scoreWith}) devrait être supérieur au score sans éditeur ({scoreWithout})");
    }

    [Fact]
    public void ScoringIndexerResult_PackAvecAnneeDansLaFenetreDuVolume_ScoreHautQueSansAnnee()
    {
        // Une compilation affiche typiquement l'année du scan (ici 2024), pas l'année de l'issue (2012) :
        // la tolérance PACK doit quand même en tenir compte.
        var volume = MakeVolume([], title: "Sillage", year: 1996);
        var issue = MakeIssue(issueNumber: 15, year: 2012, authors: []);

        var withYear = MakeResult("Sillage.Tomes.01.a.24.FRENCH.CBZ-NoTAG.2024", 900L * Mb);
        var withoutYear = MakeResult("Sillage.Tomes.01.a.24.FRENCH.CBZ-NoTAG", 900L * Mb);

        var scoredWith = ScoringTorrent.ScoringIndexerResult(volume, issue, withYear);
        var scoredWithout = ScoringTorrent.ScoringIndexerResult(volume, issue, withoutYear);

        Assert.Equal("PACK", scoredWith.Analysis.Type);
        Assert.True(scoredWith.Score > scoredWithout.Score,
            $"Score PACK avec année dans la fenêtre du volume ({scoredWith.Score}) devrait être supérieur au score sans année ({scoredWithout.Score})");
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

    // ── Pénalité « aucun seeder » ──────────────────────────────────────────

    [Fact]
    public void ScoringIndexerResult_SingleParfaitSansSeeder_ScoreSousLeSeuilParDefaut()
    {
        // Sans pénalité, ce SINGLE au numéro confirmé atteint 100 : ApplyTypeAdjustment écrase le
        // baseScore (et donc ScoreSeeders) par 70 + bonus taille + réassurance.
        var (volume, issue) = MakePerfectSingleContext();

        var seeded   = MakeResult(PerfectSingleTitle, 200 * Mb, seeders: 10);
        var noSeeder = MakeResult(PerfectSingleTitle, 200 * Mb, seeders: 0);

        var scoreSeeded   = ScoringTorrent.ScoringIndexerResult(volume, issue, seeded).Score;
        var scoreNoSeeder = ScoringTorrent.ScoringIndexerResult(volume, issue, noSeeder).Score;

        Assert.True(scoreNoSeeder < 70f,
            $"Un torrent sans seeder doit passer sous AutoSearchMinScore (défaut 70), obtenu {scoreNoSeeder}");
        Assert.Equal(scoreSeeded - 40f, scoreNoSeeder, precision: 3);
    }

    [Fact]
    public void ScoringIndexerResult_UsenetSansSeeder_NonPenalise()
    {
        // L'usenet n'a pas de seeders : la pénalité ne doit jamais s'y appliquer.
        var (volume, issue) = MakePerfectSingleContext();

        var usenet = MakeResult(PerfectSingleTitle, 200 * Mb, seeders: 0, protocol: "usenet");
        var torrent = MakeResult(PerfectSingleTitle, 200 * Mb, seeders: 10);

        var scoreUsenet  = ScoringTorrent.ScoringIndexerResult(volume, issue, usenet).Score;
        var scoreTorrent = ScoringTorrent.ScoringIndexerResult(volume, issue, torrent).Score;

        Assert.Equal(scoreTorrent, scoreUsenet, precision: 3);
    }

    [Fact]
    public void ScoringIndexerResult_SeedersFaiblesMaisNonNuls_NonPenalises()
    {
        var (volume, issue) = MakePerfectSingleContext();

        var oneSeeder = MakeResult(PerfectSingleTitle, 200 * Mb, seeders: 1);
        var noSeeder  = MakeResult(PerfectSingleTitle, 200 * Mb, seeders: 0);

        var scoreOne = ScoringTorrent.ScoringIndexerResult(volume, issue, oneSeeder).Score;
        var scoreNone = ScoringTorrent.ScoringIndexerResult(volume, issue, noSeeder).Score;

        Assert.True(scoreOne - scoreNone >= 39f,
            $"Un seul seeder ({scoreOne}) doit rester nettement au-dessus de zéro seeder ({scoreNone})");
    }
}

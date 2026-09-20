using Inkhound.Core.Models;
using Inkhound.Core.Prowlarr;
using Inkhound.Core.Scoring;

namespace Inkhound.Core.Tests.Scoring;

public class ScoringVolumePackTests
{
    private const long Mb = 1_048_576L;

    private static Volume MakeVolume(int? year = null, int countOfIssues = 24)
        => new() { Title = "Sillage", Year = year, CountOfIssues = countOfIssues };

    private static List<Issue> MakeMissingIssues(int count)
        => Enumerable.Range(1, count).Select(n => new Issue { Id = Guid.NewGuid(), IssueNumber = n }).ToList();

    private static ProwlarrSearchResult MakeResult(string title, long sizeBytes)
        => new(title, null, sizeBytes, 10, 2, 0, "guid", 1, "Indexer", "torrent", null, null);

    private static ProwlarrSearchResult MakeResult(string title, long sizeBytes, int seeders)
        => new(title, null, sizeBytes, seeders, 2, 0, "guid", 1, "Indexer", "torrent", null, null);

    private static ProwlarrSearchResult MakeResult(string title, long sizeBytes, string? downloadUrl)
        => new(title, null, sizeBytes, 10, 2, 0, "guid", 1, "Indexer", "torrent", downloadUrl, null);

    [Fact]
    public void ScoringIndexerResult_PackBanniPourUneIssueDuVolume_ScoreZero()
    {
        var volume = MakeVolume(year: 1996);
        var missingIssues = MakeMissingIssues(24);
        var pack = MakeResult("Sillage.Tomes.01.a.24.FRENCH.CBZ-NoTAG", 900L * Mb, "https://tracker/download/abc");

        // Ban posé par une seule issue du volume : il écarte le pack entier.
        var bans = TorrentBanIndex.From([new IssueTorrentBan
        {
            Id = Guid.NewGuid(),
            IssueId = missingIssues[3].Id,
            DownloadUrl = "https://tracker/download/abc",
            CreatedAt = DateTime.UtcNow
        }]);

        var scored = ScoringVolumePack.ScoringIndexerResult(volume, missingIssues, pack, bans);

        Assert.True(scored.Banned);
        Assert.Equal(0f, scored.Score);
        Assert.True(ScoringVolumePack.ScoringIndexerResult(volume, missingIssues, pack).Score > 0f);
    }

    [Fact]
    public void ScoringIndexerResult_PackCouvrantPlusDIssues_ScoreHautQueSingle()
    {
        var volume = MakeVolume(year: 1996);
        var missingIssues = MakeMissingIssues(24);

        var pack = MakeResult("Sillage.Tomes.01.a.24.FRENCH.CBZ-NoTAG", 900L * Mb);
        var single = MakeResult("Sillage T15 (2012) [cbz]", 100L * Mb);

        var scoredPack = ScoringVolumePack.ScoringIndexerResult(volume, missingIssues, pack);
        var scoredSingle = ScoringVolumePack.ScoringIndexerResult(volume, missingIssues, single);

        Assert.Equal("PACK", scoredPack.Analysis.Type);
        Assert.Equal(24, scoredPack.CoveredIssueCount);
        Assert.Equal(1, scoredSingle.CoveredIssueCount);
        Assert.True(scoredPack.Score > scoredSingle.Score,
            $"Score du PACK ({scoredPack.Score}) devrait être supérieur à celui du SINGLE ({scoredSingle.Score})");
    }

    [Fact]
    public void ScoringIndexerResult_PackSansPlageVerifiee_CouvertureReduiteParRapportAPlageVerifiee()
    {
        var volume = MakeVolume(year: 1996);
        var missingIssues = MakeMissingIssues(24);

        var verifiedRange = MakeResult("Sillage.Tomes.01.a.24.FRENCH.CBZ-NoTAG", 900L * Mb);
        var fullKeyword = MakeResult("Sillage Intégrale FRENCH CBZ", 900L * Mb);

        var scoredVerified = ScoringVolumePack.ScoringIndexerResult(volume, missingIssues, verifiedRange);
        var scoredFull = ScoringVolumePack.ScoringIndexerResult(volume, missingIssues, fullKeyword);

        Assert.Equal("Full", scoredFull.Analysis.Label);
        Assert.Equal(24, scoredVerified.CoveredIssueCount);
        Assert.Equal(17, scoredFull.CoveredIssueCount); // round(24 * 0.7)
        Assert.True(scoredVerified.Score > scoredFull.Score,
            $"Score avec plage vérifiée ({scoredVerified.Score}) devrait être supérieur à celui sans plage vérifiée ({scoredFull.Score})");
    }

    [Fact]
    public void ScoreYearForPack_EstPartageEntreScoringTorrentEtScoringVolumePack()
    {
        var volume = MakeVolume(year: 1996);
        var missingIssues = MakeMissingIssues(24);
        var issue = new Issue { IssueNumber = 1, Year = 2030 }; // sans incidence : la branche PACK ignore issue.Year

        var result = MakeResult("Sillage.Tomes.01.a.24.FRENCH.CBZ-NoTAG.2010", 900L * Mb);

        var fromTorrent = ScoringTorrent.ScoringIndexerResult(volume, issue, result);
        var fromVolume = ScoringVolumePack.ScoringIndexerResult(volume, missingIssues, result);

        Assert.Equal("PACK", fromTorrent.Analysis.Type);
        Assert.Equal(fromTorrent.Details.YearMatch, fromVolume.Details.YearMatch);
    }

    [Fact]
    public void ScoringIndexerResult_PackSansSeeder_ScoreReduitDe40()
    {
        var volume = MakeVolume(year: 1996);
        var missingIssues = MakeMissingIssues(24);

        var seeded   = MakeResult("Sillage.Tomes.01.a.24.FRENCH.CBZ-NoTAG", 900L * Mb, seeders: 10);
        var noSeeder = MakeResult("Sillage.Tomes.01.a.24.FRENCH.CBZ-NoTAG", 900L * Mb, seeders: 0);

        var scoreSeeded   = ScoringVolumePack.ScoringIndexerResult(volume, missingIssues, seeded).Score;
        var scoreNoSeeder = ScoringVolumePack.ScoringIndexerResult(volume, missingIssues, noSeeder).Score;

        Assert.True(scoreNoSeeder < 70f,
            $"Un PACK sans seeder doit passer sous AutoSearchMinScore (défaut 70), obtenu {scoreNoSeeder}");
        Assert.Equal(Math.Max(0f, scoreSeeded - 40f), scoreNoSeeder, precision: 3);
    }
}

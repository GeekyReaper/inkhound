using Inkhound.Core.Models;

namespace Inkhound.Core.Tests;

public class MostWantedRankerTests
{
    private static Volume MakeVolume(string title, int owned, int total)
        => new()
        {
            Id = Guid.NewGuid(),
            LibraryId = Guid.NewGuid(),
            Title = title,
            CountOfDownloadedIssues = owned,
            CountOfIssues = total,
        };

    private static Issue MakeIssue(Volume volume, int issueNumber)
        => new() { Id = Guid.NewGuid(), VolumeId = volume.Id, IssueNumber = issueNumber };

    private static (Issue, Volume, int) Candidate(Volume volume, int issueNumber, int volumeMissingCount)
        => (MakeIssue(volume, issueNumber), volume, volumeMissingCount);

    [Fact]
    public void RankMostWanted_TriePar_MoinsDeManquantsPuisPlusGrandVolume()
    {
        var a = MakeVolume("A", owned: 1, total: 2);    // 1 manquante, petit volume
        var b = MakeVolume("B", owned: 19, total: 20);  // 1 manquante, gros volume
        var c = MakeVolume("C", owned: 8, total: 10);   // 2 manquantes

        var rows = InkhoundManager.RankMostWanted(
            [
                Candidate(a, 2, volumeMissingCount: 1),
                Candidate(c, 9, volumeMissingCount: 2),
                Candidate(b, 20, volumeMissingCount: 1),
                Candidate(c, 3, volumeMissingCount: 2),
            ],
            limit: 10);

        // 1 manquante d'abord (B avant A car 20 tomes > 2), puis les 2 manquantes de C par n° d'issue.
        Assert.Equal(new[] { "B", "A", "C", "C" }, rows.Select(r => r.VolumeTitle).ToArray());
        Assert.Equal(new[] { 3, 9 }, rows.Where(r => r.VolumeTitle == "C").Select(r => r.IssueNumber).ToArray());
    }

    [Fact]
    public void RankMostWanted_CalculeLesPourcentagesArrondis()
    {
        var b = MakeVolume("B", owned: 19, total: 20);

        var row = Assert.Single(InkhoundManager.RankMostWanted([Candidate(b, 20, 1)], limit: 10));

        Assert.Equal(95, row.CurrentCompletionPercent);
        Assert.Equal(100, row.ProjectedCompletionPercent);
        Assert.Equal(1, row.MissingCount);
        Assert.Equal(19, row.OwnedCount);
        Assert.Equal(20, row.TotalCount);
    }

    [Fact]
    public void RankMostWanted_RespecteLaLimite()
    {
        var volumes = Enumerable.Range(0, 6)
            .Select(i => MakeVolume($"V{i}", owned: 4, total: 5))
            .ToList();

        var rows = InkhoundManager.RankMostWanted(
            volumes.Select(v => Candidate(v, 5, volumeMissingCount: 1)),
            limit: 3);

        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void RankMostWanted_PrefereLaCoverDeLIssueQuandDisponible()
    {
        var volume = MakeVolume("A", owned: 1, total: 2);
        volume.Image = new VolumeImage(null, null, null, null, "volume-small", null, null, null, null, null);
        var issue = MakeIssue(volume, 2);
        issue.Image = new VolumeImage(null, null, null, null, "issue-small", null, null, null, null, null);

        var row = Assert.Single(InkhoundManager.RankMostWanted([(issue, volume, 1)], limit: 10));

        Assert.Equal("issue-small", row.Image?.SmallUrl);
    }

    [Fact]
    public void RankMostWanted_UtiliseLaCoverDuVolumeSiLIssueNenAPas()
    {
        var volume = MakeVolume("A", owned: 1, total: 2);
        volume.Image = new VolumeImage(null, null, null, null, "volume-small", null, null, null, null, null);
        var issue = MakeIssue(volume, 2); // pas d'image

        var row = Assert.Single(InkhoundManager.RankMostWanted([(issue, volume, 1)], limit: 10));

        Assert.Equal("volume-small", row.Image?.SmallUrl);
    }
}

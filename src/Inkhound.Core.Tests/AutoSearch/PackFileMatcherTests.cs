using Inkhound.Core.Models;
using Inkhound.Core.QBittorrent;

namespace Inkhound.Core.Tests.AutoSearch;

public class PackFileMatcherTests
{
    private static Issue Missing(int number) => new()
    {
        Id = Guid.NewGuid(), VolumeId = Guid.NewGuid(), IssueNumber = number,
        Category = IssueCategory.Standard, Status = IssueStatus.MISSING
    };

    private static QBittorrentTorrentFile File(int index, string name)
        => new(index, name, 50_000_000, 0, 1);

    [Fact]
    public void ApparieUnFichierParNumeroDeTome()
    {
        var issue3 = Missing(3);
        var files = new[] { File(0, "Serie/Serie - T03.cbz") };

        var matches = InkhoundManager.MatchPackFilesToMissingIssues(files, [Missing(1), issue3]);

        var match = Assert.Single(matches);
        Assert.Equal(0, match.File.Index);
        Assert.Same(issue3, match.Issue);
    }

    [Fact]
    public void IgnoreLesFichiersNonArchive()
    {
        var files = new[] { File(0, "T01.txt"), File(1, "T01.jpg"), File(2, "cover T01.nfo") };

        var matches = InkhoundManager.MatchPackFilesToMissingIssues(files, [Missing(1)]);

        Assert.Empty(matches);
    }

    [Fact]
    public void IgnoreLesNumerosNonManquants()
    {
        var files = new[] { File(0, "T02.cbz"), File(1, "T05.cbr") };

        var matches = InkhoundManager.MatchPackFilesToMissingIssues(files, [Missing(1), Missing(3)]);

        Assert.Empty(matches);
    }

    [Fact]
    public void NeRetientQuUnFichierParIssue()
    {
        var issue = Missing(4);
        var files = new[] { File(0, "T04.cbz"), File(1, "T04 (scan alt).cbz") };

        var matches = InkhoundManager.MatchPackFilesToMissingIssues(files, [issue]);

        var match = Assert.Single(matches);
        Assert.Equal(0, match.File.Index);
    }

    [Fact]
    public void ApparieToutesLesIssuesCouvertes()
    {
        var files = new[] { File(0, "T01.cbz"), File(1, "T02.cbz"), File(2, "T03.cbz"), File(3, "T04.cbz") };

        var matches = InkhoundManager.MatchPackFilesToMissingIssues(files, [Missing(2), Missing(4)]);

        Assert.Equal(2, matches.Count);
        Assert.Equal([1, 3], matches.Select(m => m.File.Index).ToArray());
    }

    [Fact]
    public void ListeVideSansCorrespondance()
    {
        var matches = InkhoundManager.MatchPackFilesToMissingIssues([], [Missing(1)]);

        Assert.Empty(matches);
    }
}

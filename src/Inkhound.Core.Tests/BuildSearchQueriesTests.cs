using Inkhound.Core.Models;

namespace Inkhound.Core.Tests;

public class BuildSearchQueriesTests
{
    private static Volume MakeVolume(string? publisher, int? year)
        => new() { Title = "Sillage", Publisher = publisher, Year = year };

    private static Issue MakeIssue(int issueNumber, int? year, string? title)
        => new() { IssueNumber = issueNumber, Year = year, Title = title };

    [Fact]
    public void BuildSearchQueries_NumeroSansDiese_DansTousLesNiveaux()
    {
        var volume = MakeVolume(publisher: "Delcourt", year: 2012);
        var issue = MakeIssue(issueNumber: 15, year: 2012, title: "Chasse gardée");

        var queries = InkhoundManager.BuildSearchQueries(volume, issue);

        Assert.All(queries.Where(q => q.Contains("15")), q => Assert.DoesNotContain('#', q));
    }

    [Fact]
    public void BuildSearchQueries_JeuComplet_GenereLes5NiveauxDansLOrdre()
    {
        var volume = MakeVolume(publisher: "Delcourt", year: 2012);
        var issue = MakeIssue(issueNumber: 15, year: 2012, title: "Chasse gardée");

        var queries = InkhoundManager.BuildSearchQueries(volume, issue);

        Assert.Equal(
        [
            "Sillage 15 Delcourt (2012) Chasse gardée",
            "Sillage 15 Delcourt (2012)",
            "Sillage 15 (2012)",
            "Sillage 15",
            "Sillage",
        ], queries);
    }

    [Fact]
    public void BuildSearchQueries_JeuMinimal_NeGenereQueLesNiveauxTitreEtNumero()
    {
        var volume = MakeVolume(publisher: null, year: null);
        var issue = MakeIssue(issueNumber: 15, year: null, title: null);

        var queries = InkhoundManager.BuildSearchQueries(volume, issue);

        Assert.Equal(["Sillage 15", "Sillage"], queries);
    }
}

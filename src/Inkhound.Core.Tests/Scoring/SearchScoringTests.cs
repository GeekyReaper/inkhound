using Inkhound.Core.Scoring;

namespace Inkhound.Core.Tests.Scoring;

public class SearchScoringTests
{
    private const string Query = "Les trois fantômes de tesla";

    [Fact]
    public void ScoreTitleMatch_ArticleDeplaceEnFinBedetheque_ScoreExact()
    {
        var score = SearchScoring.ScoreTitleMatch(Query, "Trois fantômes de Tesla (Les)");
        Assert.Equal(100, score);
    }

    [Fact]
    public void ScoreTitleMatch_ArticleEnTeteComicVine_ScoreExact()
    {
        var score = SearchScoring.ScoreTitleMatch(Query, "Les trois fantômes de Tesla");
        Assert.Equal(100, score);
    }

    [Fact]
    public void ScoreTitleMatch_LangueAligneeAvecPreference_BonusDeDix()
    {
        var withBonus = SearchScoring.ScoreTitleMatch(Query, "Trois fantômes de Tesla (Les)", language: "Français", preferredLanguage: "Français");
        var noPreference = SearchScoring.ScoreTitleMatch(Query, "Trois fantômes de Tesla (Les)", language: "Français", preferredLanguage: null);
        var otherLanguage = SearchScoring.ScoreTitleMatch(Query, "Trois fantômes de Tesla (Les)", language: "Anglais", preferredLanguage: "Français");

        Assert.Equal(110, withBonus);
        Assert.Equal(100, noPreference);
        Assert.Equal(100, otherLanguage);
    }

    [Fact]
    public void ScoreTitleMatch_LangueInconnue_PasDeBonusMemeAvecPreference()
    {
        var score = SearchScoring.ScoreTitleMatch(Query, "Les trois fantômes de Tesla", language: null, preferredLanguage: "Français");
        Assert.Equal(100, score);
    }

    [Fact]
    public void ScoreTitleMatch_ArticleInterneConserve()
    {
        Assert.Equal(100, SearchScoring.ScoreTitleMatch("Le Chat du Rabbin", "Chat du Rabbin (Le)"));
        Assert.NotEqual(100, SearchScoring.ScoreTitleMatch("Chat", "Le Chat du Rabbin"));
    }

    [Fact]
    public void NormalizeTitle_TitreReduitAUnArticle_Conserve()
    {
        Assert.Equal("les", SearchScoring.NormalizeTitle("Les"));
        Assert.Equal("the les", SearchScoring.NormalizeTitle("The (Les)"));
    }

    [Fact]
    public void ScoreTitleMatch_BedethequeAligneDevanceComicVineMalgreBonusTomes()
    {
        var bedetheque = SearchScoring.ScoreTitleMatch(Query, "Trois fantômes de Tesla (Les)", countOfIssues: 3, language: "Français", preferredLanguage: "Français");
        var comicVine  = SearchScoring.ScoreTitleMatch(Query, "Les trois fantômes de Tesla", countOfIssues: 200, language: null, preferredLanguage: "Français");

        Assert.True(bedetheque > comicVine, $"Bedetheque {bedetheque} devrait devancer ComicVine {comicVine}");
    }
}

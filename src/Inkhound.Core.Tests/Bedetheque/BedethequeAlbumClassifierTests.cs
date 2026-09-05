using Inkhound.Core.Bedetheque;

namespace Inkhound.Core.Tests.Bedetheque;

public class BedethequeAlbumClassifierTests
{
    [Fact]
    public void NormalizeSingleAlbumSeries_SerieAAlbumUniqueDansLeRepli_DevientStandard1()
    {
        // Cas exact rapporté : one-shot Bedetheque sans préfixe de numérotation (ex. "Jean Doux et
        // le mystère de la disquette molle") — Classify le range en SpecialEdition faute de préfixe.
        var classified = new List<(string Category, int? Idx)> { ("SpecialEdition", 1) };

        BedethequeAlbumClassifier.NormalizeSingleAlbumSeries(classified);

        Assert.Equal(("Standard", (int?)1), classified[0]);
    }

    [Fact]
    public void NormalizeSingleAlbumSeries_AlbumUniqueSansIdx_DevientStandard1()
    {
        var classified = new List<(string Category, int? Idx)> { ("SpecialEdition", null) };

        BedethequeAlbumClassifier.NormalizeSingleAlbumSeries(classified);

        Assert.Equal(("Standard", (int?)1), classified[0]);
    }

    [Theory]
    [InlineData("Special")]   // hors-série explicite (préfixe HS*)
    [InlineData("Omnibus")]   // intégrale explicite (préfixe INT* / titre "intégrale")
    [InlineData("Roman")]
    [InlineData("BestOf")]
    [InlineData("Standard")]
    public void NormalizeSingleAlbumSeries_AlbumUniqueCatalogueExplicitement_Inchange(string category)
    {
        var classified = new List<(string Category, int? Idx)> { (category, 1) };

        BedethequeAlbumClassifier.NormalizeSingleAlbumSeries(classified);

        Assert.Equal(category, classified[0].Category);
    }

    [Fact]
    public void NormalizeSingleAlbumSeries_PlusieursAlbums_Inchange()
    {
        // Dès qu'il y a plusieurs tomes, le repli SpecialEdition reste (l'ambiguïté est réelle).
        var classified = new List<(string Category, int? Idx)>
        {
            ("SpecialEdition", 1),
            ("SpecialEdition", 2),
        };

        BedethequeAlbumClassifier.NormalizeSingleAlbumSeries(classified);

        Assert.All(classified, c => Assert.Equal("SpecialEdition", c.Category));
    }
}

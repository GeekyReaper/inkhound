using Inkhound.Core.Bedetheque.Catalog;

namespace Inkhound.Core.Tests.Bedetheque;

public class BedethequeTitleNormalizerTests
{
    [Theory]
    [InlineData("Schtroumpfs (Les)", "Les Schtroumpfs")]
    [InlineData("Trois fantômes de Tesla (Les)", "Les Trois fantômes de Tesla")]
    [InlineData("Titre (A) (B)", "A B Titre")]
    [InlineData("Sans parenthèses", "Sans parenthèses")]
    public void ReorderParentheticalPrefix_ReplaceLesGroupesEnTete(string input, string expected)
        => Assert.Equal(expected, BedethequeTitleNormalizer.ReorderParentheticalPrefix(input));

    [Theory]
    [InlineData("Les Légendaires", "Légendaires")]
    [InlineData("L'Incal", "Incal")]
    [InlineData("L’Incal", "Incal")]      // apostrophe typographique
    [InlineData("Une aventure", "aventure")]
    [InlineData("Lester", "Lester")]      // "les" n'est pas suivi d'un espace → conservé
    public void StripLeadingArticle_RetireUnArticleDeTete(string input, string expected)
        => Assert.Equal(expected, BedethequeTitleNormalizer.StripLeadingArticle(input));

    [Theory]
    [InlineData("Les Schtroumpfs", "schtroumpfs")]
    [InlineData("Légendaires", "legendaires")]                 // accent précomposé
    [InlineData("Cœur de pierre", "coeur de pierre")]          // ligature
    [InlineData("Astérix & Obélix", "asterix et obelix")]      // & → et
    [InlineData("I.R.$.", "irs")]                              // points supprimés, $ → s
    [InlineData("Blake  et   Mortimer !!", "blake et mortimer")] // blancs/ponctuation fusionnés
    [InlineData("", "")]
    public void NormalizeForSearch_ProduitUneFormeCanonique(string input, string expected)
        => Assert.Equal(expected, BedethequeTitleNormalizer.NormalizeForSearch(input));

    [Fact]
    public void Tokenize_CanonicaliseLesSynonymes()
    {
        Assert.Equal(["4", "fantastiques"], BedethequeTitleNormalizer.Tokenize("Les quatre Fantastiques"));
        Assert.Equal(["tom", "et", "jerry"], BedethequeTitleNormalizer.Tokenize("Tom and Jerry"));
        Assert.Equal(["7", "vies"], BedethequeTitleNormalizer.Tokenize("seven vies"));
    }

    [Fact]
    public void Tokenize_NeConvertitPasUnUneOne()
    {
        // "un"/"une" sont retirés en tête (article), mais jamais convertis en "1" ailleurs.
        Assert.Equal(["homme", "et", "une", "femme"], BedethequeTitleNormalizer.Tokenize("Un homme et une femme"));
        Assert.Equal(["one", "piece"], BedethequeTitleNormalizer.Tokenize("One Piece"));
    }

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "abc", 0)]
    public void Levenshtein_DistanceClassique(string a, string b, int expected)
        => Assert.Equal(expected, BedethequeTitleNormalizer.Levenshtein(a, b));

    [Fact]
    public void Levenshtein_SortieAnticipeeAuDelaDuMax()
    {
        Assert.Equal(2, BedethequeTitleNormalizer.Levenshtein("kitten", "sitting", max: 1));
        Assert.Equal(2, BedethequeTitleNormalizer.Levenshtein("a", "abcdef", max: 1)); // écart de longueur
    }
}

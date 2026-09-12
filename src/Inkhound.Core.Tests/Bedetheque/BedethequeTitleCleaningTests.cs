using Inkhound.Core.Bedetheque;

namespace Inkhound.Core.Tests.Bedetheque;

public class BedethequeTitleCleaningTests
{
    [Fact]
    public void CleanScrapedText_IndentationHtmlDuInnerText_EstReduiteAUnEspace()
    {
        // Cas exact rapporté : le <h2> de la page album restitue l'indentation du HTML source, et
        // ce titre était stocké tel quel puis recopié dans le nom du fichier CBZ.
        var raw = "INT2.        \n                                        L'Intégrale - Tomes 4 à 6";

        Assert.Equal("INT2. L'Intégrale - Tomes 4 à 6", BedethequeSourceService.CleanScrapedText(raw));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Aria&nbsp;&amp;&nbsp;co", "Aria & co")]
    public void CleanScrapedText_CasLimites(string? raw, string expected)
        => Assert.Equal(expected, BedethequeSourceService.CleanScrapedText(raw));

    [Theory]
    // Préfixes accolés au titre : chiffre seul, mais aussi codes alphanumériques que l'ancienne
    // expression (ancrée sur ^\d+) laissait passer entièrement.
    [InlineData("1. Le Voyage", "Le Voyage")]
    [InlineData("INT2.        \n            L'Intégrale - Tomes 4 à 6", "L'Intégrale - Tomes 4 à 6")]
    [InlineData("INT01. Intégrale 1", "Intégrale 1")]
    [InlineData("HS1. Le Hors-série", "Le Hors-série")]
    // Préfixe contenant un espace : coupé sur le " . " littéral.
    [InlineData("INT FL . La voie fiscale", "La voie fiscale")]
    public void CleanAlbumTitle_PrefixeDeNumerotation_EstRetire(string raw, string expected)
        => Assert.Equal(expected, BedethequeSourceService.CleanAlbumTitle(raw));

    [Theory]
    // Aucun préfixe à retirer : le titre doit sortir intact. Le tiret interne ne doit jamais servir
    // de point de coupe, sinon "L'Intégrale - Tomes 4 à 6" serait tronqué en "Tomes 4 à 6".
    [InlineData("L'Intégrale - Tomes 4 à 6")]
    [InlineData("Le Cycle d'Anathos : L'Alystory")]
    [InlineData("Boing ! Boing ! Bunk !")]
    [InlineData("Tomes 4 à 6")]
    public void CleanAlbumTitle_TitreSansPrefixe_EstInchange(string raw)
        => Assert.Equal(raw, BedethequeSourceService.CleanAlbumTitle(raw));
}

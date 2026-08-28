using Inkhound.Core.Analysis;

namespace Inkhound.Core.Tests.Analysis;

public class TorrentTypeAnalyzerTests
{
    private const long OneHundredMB = 100L * 1_048_576L;

    [Fact]
    public void Analyze_TitreAvecAnneeApresLeTiret_EstClasseSingle()
    {
        // Cas exact rapporté : "Tome 27 - 2020" ne doit pas être interprété comme une plage 27...202
        var result = TorrentTypeAnalyzer.Analyze(
            "[BD] Jarry - Benoît - Elfes - Tome 27 - 2020 [aATAa] [cbr]", OneHundredMB);

        Assert.Equal("SINGLE", result.Type);
        Assert.Equal("#27", result.Label);
    }

    [Theory]
    [InlineData("Elfes T1 à T41")]
    [InlineData("Elfes [T01.T41]")]
    [InlineData("Elfes T01-T12")]
    public void Analyze_PlageLegitime_EstToujoursClasseePack(string title)
    {
        var result = TorrentTypeAnalyzer.Analyze(title, OneHundredMB);

        Assert.Equal("PACK", result.Type);
    }

    [Fact]
    public void Analyze_MotCleIntegrale_EstClassePackFull()
    {
        var result = TorrentTypeAnalyzer.Analyze("Elfes Intégrale", OneHundredMB);

        Assert.Equal("PACK", result.Type);
        Assert.Equal("Full", result.Label);
    }

    [Fact]
    public void Analyze_PlageDontLaFinDepasseLeNombreDeTomesConnus_NEstPasClasseePack()
    {
        // Elfes ne compte que 30 tomes : "27 - 2020" ne doit pas être retenu comme plage même
        // si le garde-fou de plausibilité était le seul rempart (regex boundary mise à part).
        var result = TorrentTypeAnalyzer.Analyze(
            "[BD] Jarry - Benoît - Elfes - Tome 27 - 2020 [aATAa] [cbr]", OneHundredMB, maxIssueNumber: 30);

        Assert.Equal("SINGLE", result.Type);
        Assert.Equal("#27", result.Label);
    }

    [Fact]
    public void Analyze_PlageDansLesLimitesDuVolume_RestePack()
    {
        var result = TorrentTypeAnalyzer.Analyze("Elfes T01-T12", OneHundredMB, maxIssueNumber: 30);

        Assert.Equal("PACK", result.Type);
        Assert.Equal("1...12", result.Label);
    }

    [Fact]
    public void Analyze_ConventionTomesPointNumeroPointAPointNumero_EstClasseePackAvecPlagePrecise()
    {
        // Convention scène observée : "Tomes" au pluriel, points comme séparateurs, "a" sans accent.
        var result = TorrentTypeAnalyzer.Analyze(
            "Sillage.Tomes.01.a.24.FRENCH.CBZ-NoTAG", 800L * 1_048_576L);

        Assert.Equal("PACK", result.Type);
        Assert.Equal("1...24", result.Label);
    }

    [Theory]
    [InlineData("Trolls de Troy - T01 - Histoires trolles.cbz", 1)]
    [InlineData("Lanfeust #12.cbr", 12)]
    [InlineData("Blacksad - 003 - Ame rouge.cbz", 3)]
    [InlineData("Sillage 02.cbz", 2)]
    // Numéro hors-série / prologue : "0", "00" ou "#0" doivent tous donner 0 (cas rapporté).
    [InlineData("Le Scorpion - 0 - Prologue.cbz", 0)]
    [InlineData("Thorgal - 00 - La jeunesse.cbz", 0)]
    [InlineData("Gunnm #0.cbz", 0)]
    // Un petit nombre non zéro-paddé ne doit PAS être capturé à la place du vrai numéro paddé.
    [InlineData("Batman (Vol. 3) 027.cbz", 27)]
    public void ExtractIssueNumber_NomDeFichier_RetourneLeNumeroAttendu(string fileName, int expected)
    {
        Assert.Equal(expected, TorrentTypeAnalyzer.ExtractIssueNumber(fileName));
    }

    [Theory]
    [InlineData("Corto Maltese - La ballade de la mer salee.cbz")]
    // Une année contenant des zéros internes ne doit pas être prise pour un numéro (pas de \b interne).
    [InlineData("Asterix chez les Bretons (2005).cbz")]
    public void ExtractIssueNumber_NomSansNumero_RetourneNull(string fileName)
    {
        Assert.Null(TorrentTypeAnalyzer.ExtractIssueNumber(fileName));
    }
}

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
}

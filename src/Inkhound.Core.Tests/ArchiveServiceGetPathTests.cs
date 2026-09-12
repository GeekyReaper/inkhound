using Inkhound.Core.ComicArchiveGenerator;
using Inkhound.Core.Models;

namespace Inkhound.Core.Tests;

/// <summary>
/// Noms de fichiers/dossiers calculés depuis les métadonnées (NormalizeTitle, via GetPath).
/// </summary>
public class ArchiveServiceGetPathTests
{
    private static Volume MakeVolume(string title, int? year = null) =>
        new() { Id = Guid.NewGuid(), Title = title, Year = year };

    private static Issue MakeIssue(int number, string? title, IssueCategory category = IssueCategory.Standard) =>
        new() { Id = Guid.NewGuid(), IssueNumber = number, Title = title, Category = category };

    [Theory]
    // Les issues hors catégorie Standard portent un code court entre le titre et le numéro.
    [InlineData(IssueCategory.Omnibus, "Aria - INT - 001 - Integrale 1.cbz")]
    [InlineData(IssueCategory.Special, "Aria - HS - 001 - Integrale 1.cbz")]
    [InlineData(IssueCategory.SpecialEdition, "Aria - SP - 001 - Integrale 1.cbz")]
    [InlineData(IssueCategory.Roman, "Aria - ROM - 001 - Integrale 1.cbz")]
    [InlineData(IssueCategory.BestOf, "Aria - BO - 001 - Integrale 1.cbz")]
    // Standard : aucun segment, le nom historique est conservé.
    [InlineData(IssueCategory.Standard, "Aria - 001 - Integrale 1.cbz")]
    public void GetPath_Categorie_InsereLeCodeCourtSaufPourStandard(IssueCategory category, string expected)
        => Assert.Equal(expected, ArchiveService.GetPath(MakeIssue(1, "Intégrale 1", category), MakeVolume("Aria")));

    [Fact]
    public void GetPath_CategorieNonStandardSansTitre_InsereQuandMemeLeCode()
        => Assert.Equal("Aria - INT - 002.cbz",
            ArchiveService.GetPath(MakeIssue(2, null, IssueCategory.Omnibus), MakeVolume("Aria")));

    [Fact]
    public void GetPath_TriAlphabetique_GroupeLesStandardAvantLesAutresCategories()
    {
        // Objectif du code de catégorie : les tomes classiques ne doivent pas être entrecoupés
        // d'intégrales et de hors-séries dans l'explorateur de fichiers.
        var volume = MakeVolume("Aria");
        var names = new[]
        {
            ArchiveService.GetPath(MakeIssue(1, "Le voyage"), volume),
            ArchiveService.GetPath(MakeIssue(2, "Le retour"), volume),
            ArchiveService.GetPath(MakeIssue(1, "Integrale 1", IssueCategory.Omnibus), volume),
            ArchiveService.GetPath(MakeIssue(1, "Hors serie", IssueCategory.Special), volume)
        };

        var sorted = names.OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(
        [
            "Aria - 001 - Le voyage.cbz",
            "Aria - 002 - Le retour.cbz",
            "Aria - HS - 001 - Hors serie.cbz",
            "Aria - INT - 001 - Integrale 1.cbz"
        ], sorted);
    }

    [Theory]
    // Cas rapporté : le titre d'issue contenait l'indentation HTML de la page source.
    [InlineData("INT01.                          Integrale 1", "Aria - 001 - INT01 Integrale 1.cbz")]
    // La ponctuation produisait autant d'espaces qu'elle compte de caractères.
    [InlineData("Boing ! Boing ! Bunk !", "Aria - 001 - Boing Boing Bunk.cbz")]
    [InlineData("Le Cycle d'Anathos : L'Alystory", "Aria - 001 - Le Cycle d Anathos L Alystory.cbz")]
    public void GetPath_TitreDIssueAvecBlancsMultiples_LesFusionne(string issueTitle, string expected)
        => Assert.Equal(expected, ArchiveService.GetPath(MakeIssue(1, issueTitle), MakeVolume("Aria")));

    [Theory]
    [InlineData("I.R. - All Watcher", null, "I R - All Watcher")]
    [InlineData("I.S.S. Snipers", 2021, "I S S Snipers (2021)")]
    [InlineData("Aria", 2001, "Aria (2001)")]
    public void GetPath_TitreDeVolume_DonneUnNomDeDossierSansEspacesMultiples(
        string title, int? year, string expected)
        => Assert.Equal(expected, ArchiveService.GetPath(MakeVolume(title, year)));

    [Fact]
    public void GetPath_IssueSansTitre_OmetLeSegmentDeTitre()
        => Assert.Equal("Aria - 007.cbz", ArchiveService.GetPath(MakeIssue(7, null), MakeVolume("Aria")));

    [Fact]
    public void GetPath_AccentsEtDiacritiques_SontSupprimes()
        => Assert.Equal("Aria - 001 - L Integrale.cbz",
            ArchiveService.GetPath(MakeIssue(1, "L'Intégrale"), MakeVolume("Aria")));
}

using Inkhound.Core.ComicArchiveGenerator;
using Inkhound.Core.Models;

namespace Inkhound.Core.Tests;

/// <summary>
/// Reprise des dossiers créés avant que NormalizeTitle ne fusionne les espaces. Sans elle, ces
/// volumes seraient vus comme n'ayant plus aucun fichier (issues repassées MISSING au Check files).
/// </summary>
public sealed class ArchiveServiceFindExistingVolumeDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "inkhound-tests", Path.GetRandomFileName());

    public ArchiveServiceFindExistingVolumeDirectoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private Library MakeLibrary() => new() { Id = Guid.NewGuid(), Name = "Test", Path = _root };

    private static Volume MakeVolume(string title, int? year = null) =>
        new() { Id = Guid.NewGuid(), Title = title, Year = year };

    [Fact]
    public void DossierAuNomCourant_EstRetourneTelQuel()
    {
        var expected = Directory.CreateDirectory(Path.Combine(_root, "Aria (1982)")).FullName;

        Assert.Equal(expected, ArchiveService.FindExistingVolumeDirectory(MakeVolume("Aria", 1982), MakeLibrary()));
    }

    [Theory]
    // Noms réellement présents sur disque (créés par l'ancienne normalisation) ↔ titre en base.
    [InlineData("I R    - All Watcher (2009)", "I.R. - All Watcher", 2009)]
    [InlineData("I S S  Snipers (2021)", "I.S.S. Snipers", 2021)]
    [InlineData("Bunker  Betbeder Bec (2006)", "Bunker (Betbeder/Bec)", 2006)]
    public void DossierHerite_NeDifferantQueParLesBlancs_EstRetrouve(string onDisk, string title, int year)
    {
        var legacy = Directory.CreateDirectory(Path.Combine(_root, onDisk)).FullName;

        Assert.Equal(legacy, ArchiveService.FindExistingVolumeDirectory(MakeVolume(title, year), MakeLibrary()));
    }

    [Fact]
    public void AucunDossierCorrespondant_RetourneNull()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Sillage (1998)"));

        Assert.Null(ArchiveService.FindExistingVolumeDirectory(MakeVolume("Aria", 1982), MakeLibrary()));
    }

    [Fact]
    public void LibrairieInexistante_RetourneNull()
    {
        var library = new Library { Id = Guid.NewGuid(), Name = "Absente", Path = Path.Combine(_root, "nope") };

        Assert.Null(ArchiveService.FindExistingVolumeDirectory(MakeVolume("Aria", 1982), library));
    }

    [Fact]
    public void TitreSansCaractereRetenu_NeRenvoieJamaisLaRacine()
    {
        // Garde-fou symétrique de TryGetVolumeDirectory : un nom vide ne doit pas matcher la racine.
        Assert.Null(ArchiveService.FindExistingVolumeDirectory(MakeVolume("???"), MakeLibrary()));
    }
}

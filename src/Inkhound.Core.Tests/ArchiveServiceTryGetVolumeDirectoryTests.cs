using Inkhound.Core.ComicArchiveGenerator;
using Inkhound.Core.Models;

namespace Inkhound.Core.Tests;

/// <summary>
/// Garde-fou de la suppression récursive du répertoire d'un volume (`DeleteVolumeAsync(deleteFiles: true)`).
/// Un faux négatif ici effacerait la librairie entière : ces cas doivent rester verts.
/// </summary>
public class ArchiveServiceTryGetVolumeDirectoryTests
{
    private static readonly string LibraryRoot = Path.Combine(Path.GetTempPath(), "inkhound-tests", "library");

    private static Library MakeLibrary() => new() { Id = Guid.NewGuid(), Name = "Test", Path = LibraryRoot };

    private static Volume MakeVolume(string title, int? year = null) =>
        new() { Id = Guid.NewGuid(), Title = title, Year = year };

    [Theory]
    [InlineData("Aria", null, "Aria")]
    [InlineData("Aria", 2001, "Aria (2001)")]
    [InlineData("Les Cités obscures", 1983, "Les Cites obscures (1983)")]
    public void TryGetVolumeDirectory_TitreValide_RetourneUnSousDossierDeLaLibrairie(
        string title, int? year, string expectedFolderName)
    {
        var ok = ArchiveService.TryGetVolumeDirectory(MakeVolume(title, year), MakeLibrary(), out var path, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(Path.Combine(LibraryRoot, expectedFolderName), path);
    }

    [Theory]
    [InlineData("???")]      // aucun caractère alphanumérique : nom de dossier vide
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("/")]
    public void TryGetVolumeDirectory_TitreSansCaractereRetenu_EstRefuse(string title)
    {
        // Sans ce refus, le chemin résolu serait la racine de la librairie elle-même,
        // et une suppression récursive détruirait toute la librairie.
        var ok = ArchiveService.TryGetVolumeDirectory(MakeVolume(title), MakeLibrary(), out var path, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Equal(string.Empty, path);
    }

    [Fact]
    public void TryGetVolumeDirectory_CheminResolu_NeVautJamaisLaRacineDeLaLibrairie()
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(LibraryRoot));

        foreach (var title in new[] { "???", "   ", "-", "..", "." })
        {
            var ok = ArchiveService.TryGetVolumeDirectory(MakeVolume(title), MakeLibrary(), out var path, out _);
            Assert.False(ok && string.Equals(path, root, StringComparison.OrdinalIgnoreCase),
                $"Le titre \"{title}\" résout vers la racine de la librairie.");
        }
    }
}

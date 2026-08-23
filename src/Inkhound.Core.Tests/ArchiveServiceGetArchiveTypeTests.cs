using Inkhound.Core.ComicArchiveGenerator;

namespace Inkhound.Core.Tests;

public class ArchiveServiceGetArchiveTypeTests
{
    // Copié dans le répertoire de sortie via l'ItemGroup <None Include="data\**\*" ... /> du csproj.
    private static readonly string ImportPath = Path.Combine(AppContext.BaseDirectory, "data", "import");

    private static readonly Dictionary<string, EArchiveType> ExpectedTypeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = EArchiveType.PDF,
        [".cbr"] = EArchiveType.CBR,
        [".cbz"] = EArchiveType.CBZ,
    };

    public static IEnumerable<object[]> ImportFiles()
    {
        if (!Directory.Exists(ImportPath))
            yield break;

        foreach (var file in Directory.GetFiles(ImportPath))
            yield return [file];
    }

    [Theory]
    [MemberData(nameof(ImportFiles))]
    public async Task GetArchiveType_FichierDuDossierImport_CorrespondALExtensionDuFichier(string filePath)
    {
        // Arrange
        var extension = Path.GetExtension(filePath);
        Assert.True(
            ExpectedTypeByExtension.TryGetValue(extension, out var expectedType),
            $"Extension non gérée par le test : \"{extension}\" (fichier : {Path.GetFileName(filePath)})");

        var archiveService = new ArchiveService();

        // Act
        var actualType = await archiveService.GetArchiveType(filePath);

        // Assert
        Assert.Equal(expectedType, actualType);
    }

    [Fact]
    public void ImportFiles_DossierImport_ContientAuMoinsUnFichier()
    {
        // Garde-fou : un Theory alimenté par un MemberData vide s'exécute silencieusement sans échouer,
        // ce qui donnerait l'illusion que le test ci-dessus passe alors qu'il ne teste rien.
        Assert.True(Directory.Exists(ImportPath), $"Dossier introuvable : {ImportPath}");
        Assert.NotEmpty(Directory.GetFiles(ImportPath));
    }
}

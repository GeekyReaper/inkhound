using Inkhound.Core.Bedetheque.Catalog;

namespace Inkhound.Core.Tests.Bedetheque;

public class BedethequeCatalogIndexTests
{
    private static BedethequeCatalogEntry Entry(int id, string title, string? language = "Français")
        => new() { Id = id, Title = title, Language = language, Letter = title[..1].ToUpperInvariant() };

    private static BedethequeCatalogIndex Build(params BedethequeCatalogEntry[] entries)
    {
        var index = new BedethequeCatalogIndex();
        index.Load(entries);
        return index;
    }

    [Fact]
    public void Index_Vide_NEstPasCharge()
    {
        var index = new BedethequeCatalogIndex();
        Assert.False(index.IsLoaded);
        Assert.Equal(0, index.Count);
        Assert.Empty(index.Search("schtroumpfs", null, 10, 1));
    }

    [Fact]
    public void Load_IgnoreLesEntreesInvalides()
    {
        var index = Build(Entry(1, "Valide"), Entry(0, "Id nul"), Entry(2, "   "));
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void Search_TitreIdentique_Score1000_MemeAvecArticleEnSuffixe()
    {
        var index = Build(Entry(1, "Schtroumpfs (Les)"));

        var single = Assert.Single(index.Search("les schtroumpfs", null, 10, 1));
        Assert.Equal(1000, single.Score);

        single = Assert.Single(index.Search("Schtroumpfs", null, 10, 1));
        Assert.Equal(1000, single.Score);
    }

    [Fact]
    public void Search_PaliersDeScore_ExactPuisMotEntierPuisSousChainePuisFlou()
    {
        var index = Build(
            Entry(1, "Tintin"),
            Entry(2, "Aventures de Tintin (Les)"),      // "tintin" est un token entier
            Entry(3, "Tintinologie"),                   // sous-chaîne
            Entry(4, "Tintan et compagnie"));           // flou (1 substitution)

        var results = index.Search("tintin", null, 10, 1);

        Assert.Equal([1, 2, 3, 4], results.Select(r => r.Entry.Id));
        Assert.Equal(1000, results[0].Score);
        Assert.InRange(results[1].Score, 801, 900);
        Assert.InRange(results[2].Score, 600, 800);
        Assert.InRange(results[3].Score, 1, 500);
    }

    [Fact]
    public void Search_TokenSansCorrespondance_Rejete()
    {
        var index = Build(Entry(1, "Blake et Mortimer"));

        // "xyzxyz" ne ressemble à aucun token du titre → série rejetée malgré "blake" exact.
        Assert.Empty(index.Search("blake xyzxyz", null, 10, 1));
    }

    [Fact]
    public void Search_ToleranceAuxFautesEtAccents()
    {
        var index = Build(Entry(1, "Légendaires (Les)"), Entry(2, "Astérix"));

        Assert.Equal(1, Assert.Single(index.Search("legendaire", null, 10, 1)).Entry.Id);
        Assert.Equal(2, Assert.Single(index.Search("asterix", null, 10, 1)).Entry.Id);
    }

    [Fact]
    public void Search_FiltreLangue_Exact()
    {
        var index = Build(Entry(1, "Naruto", "Français"), Entry(2, "Naruto", "Japonais"));

        Assert.Equal(1, Assert.Single(index.Search("naruto", "Français", 10, 1)).Entry.Id);
        Assert.Equal(2, index.Search("naruto", null, 10, 1).Count);
    }

    [Fact]
    public void Search_MinScoreEtMaxResults()
    {
        var index = Build(Entry(1, "Tintin"), Entry(2, "Tintinologie"), Entry(3, "Tintan et compagnie"));

        // Seuil 600 : seuls l'exact (1000) et la sous-chaîne (≥ 600) passent.
        var filtered = index.Search("tintin", null, 10, 600);
        Assert.Equal([1, 2], filtered.Select(r => r.Entry.Id));

        Assert.Single(index.Search("tintin", null, 1, 1));
    }

    [Fact]
    public void Search_TriParScorePuisTitre()
    {
        var index = Build(Entry(2, "Zorro"), Entry(1, "Alpha"), Entry(3, "Zorro (Le)"));

        var results = index.Search("zorro", null, 10, 1);

        // 2 et 3 sont exacts (1000) → départagés par titre ; 1 n'apparaît pas.
        Assert.Equal([2, 3], results.Select(r => r.Entry.Id));
    }
}

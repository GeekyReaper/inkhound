using HtmlAgilityPack;
using Inkhound.Core.Bedetheque;

namespace Inkhound.Core.Tests.Bedetheque;

// Extraits au format du template Bedetheque refait en ~10/2026 (bdt-*), reconstitués d'après les
// sélecteurs validés sur pages live dans bdguest-scrapper (commit 98b1e85).
public class BedethequeSerieAlbumParsingTests
{
    private const string SerieHtml = """
        <html><head><meta property="og:image" content="https://www.bedetheque.com/cache/thb_couv/Couv_2830.jpg"></head><body>
        <section class="bdt-ah bdt-ah--serie">
          <div class="bdt-ah-top"><h1><a href="https://www.bedetheque.com/serie-192-BD-Petit-Spirou.html">Le petit Spirou</a></h1></div>
          <div class="bdt-ah-by">
            <span class="bdt-sh-genres"><a href="#">Humour</a>, <a href="#">Jeunesse</a></span>
            · <b class="bdt-sh-parution">Série en cours</b> · 1990 - 2024
          </div>
          <ul class="bdt-ah-pastilles">
            <li title="Origine"> Europe </li>
            <li title="Langue"><img src="https://www.bedetheque.com/media/flags/France.png"></li>
          </ul>
          <div class="bdt-ah-credit">© Dupuis - 1990</div>
        </section>
        <nav class="bdt-tabs"><a href="https://www.bedetheque.com/albums-192-BD-x.html">Albums <small>(36)</small></a></nav>
        <p class="bdt-sh-resume">Il y avait déjà LE GRAND SPIROU.</p>
        <article class="bdt-edition" itemscope>
          <a name="2830"></a>
          <a itemprop="url" href="https://www.bedetheque.com/BD-Petit-Spirou-Tome-1-Dis-bonjour-2830.html"></a>
          <img itemprop="image" src="https://www.bedetheque.com/cache/thb_couv/Couv_2830.jpg">
          <span itemprop="name">1. Dis bonjour à la dame !</span>
          <span itemprop="publisher">Dupuis</span>
          <meta itemprop="datePublished" content="1990-01-01">
        </article>
        <article class="bdt-edition" itemscope>
          <a name="9999"></a>
          <a itemprop="url" href="https://www.bedetheque.com/BD-Petit-Spirou-HC-9999.html"></a>
          <span itemprop="name">HC. L'épilogue</span>
          <span itemprop="publisher">Dupuis</span>
          <meta itemprop="datePublished" content="2024-05-01">
        </article>
        </body></html>
        """;

    private static HtmlDocument Load(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }

    [Fact]
    public void ParseSerie_ExtraitAnneesTitreEtMetadonnees()
    {
        var serie = BedethequeSourceService.ParseSerie(Load(SerieHtml), 192, "https://www.bedetheque.com/serie-192-BD-x.html");

        Assert.NotNull(serie);
        Assert.Equal("Le petit Spirou", serie.Titre);
        // Régression : l'année vide faisait recalculer (donc renommer) le dossier du volume.
        Assert.Equal("1990", serie.AnneeDebut);
        Assert.Equal("2024", serie.AnneeFin);
        Assert.Equal("Humour", serie.Genre);
        Assert.Equal("Série en cours", serie.Parution);
        Assert.Equal("Europe", serie.Origine);
        Assert.Equal("Français", serie.Langue);
        Assert.Equal(36, serie.NombreAlbums);
        Assert.Equal("Dupuis", serie.Editeur);
        Assert.Equal("Il y avait déjà LE GRAND SPIROU.", serie.Description);
        Assert.Equal(2, serie.Albums.Count);
    }

    [Fact]
    public void Mapper_SerieAvecAnnee_RenseigneVolumeYear()
    {
        var serie = BedethequeSourceService.ParseSerie(Load(SerieHtml), 192, "u")!;

        Assert.Equal(1990, Mapper.Map(serie).Year);
    }

    [Fact]
    public void ParseAlbumList_LitNumeroTitreCategorieEtAnnee()
    {
        var albums = BedethequeSourceService.ParseAlbumList(Load(SerieHtml));

        Assert.Equal(2, albums.Count);
        Assert.Equal(2830, albums[0].Id);
        Assert.Equal("1", albums[0].NumeroAlbum);
        Assert.Equal("Dis bonjour à la dame !", albums[0].Titre);
        Assert.Equal("1990", albums[0].Annee);
        Assert.Equal("Dupuis", albums[0].Editeur);

        // Préfixe collé au point, sans espace avant : "HC. L'épilogue"
        Assert.Equal("HC", albums[1].NumeroAlbum);
        Assert.Equal("L'épilogue", albums[1].Titre);
    }

    [Theory]
    [InlineData("Une BD de Franquin · Dupuis · 1974", "1974", "1974")]
    [InlineData("Série en cours · 1990 - 2024", "1990", "2024")]
    [InlineData("Série en cours · 1990 – 2024", "1990", "2024")]
    [InlineData("Aucune année ici", null, null)]
    [InlineData(null, null, null)]
    public void ParseYearRange_PlageAnneeSeuleOuAbsente(string? text, string? debut, string? fin)
    {
        Assert.Equal((debut, fin), BedethequeSourceService.ParseYearRange(text));
    }

    [Theory]
    [InlineData("HC. L'épilogue", "L'épilogue")]
    [InlineData("1. Chinook", "Chinook")]
    [InlineData("INT FL . La voie fiscale", "La voie fiscale")]
    [InlineData("Dr. Stone", "Dr. Stone")]   // pas de troncature d'un vrai mot
    public void CleanAlbumTitle_RetirePrefixeSansToucherAuxTitres(string raw, string expected)
    {
        Assert.Equal(expected, BedethequeSourceService.CleanAlbumTitle(raw));
    }
}

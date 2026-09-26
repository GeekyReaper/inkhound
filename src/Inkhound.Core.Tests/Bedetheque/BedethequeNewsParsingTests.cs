using HtmlAgilityPack;
using Inkhound.Core.Bedetheque;
using Inkhound.Core.News;

namespace Inkhound.Core.Tests.Bedetheque;

// Extraits réels (septembre 2026) de bdgest.com/top/ventes et bedetheque.com/nouveautes, réduits à
// quelques items représentatifs.
public class BedethequeNewsParsingTests
{
    private const string TopSalesHtml = """
        <html><body>
        <h1><i class="icon-bolt icon-2x"></i> Top des ventes - <span class="orange">Semaine du 14/09/2026</span></h1>
        <ol class="top-ventes">
          <li>
            <div class="place">n°1</div>
            <a href="https://www.bedetheque.com/BD-Nestor-Burma-Tome-14-Nestor-Burma-dans-l-ile-543160.html" class="couv" title="Nestor Burma"><img src="https://www.bedetheque.com/cache/thb_couv/Couv_543160_b5a8ef.jpg" class="fadeover"></a>
            <div class="evolution">
              <i class="icon-star icon-large orange" title="Entrée dans le classement"></i>
            </div>
            <div class="main">
              <h3>
                <a href="https://www.bedetheque.com/BD-Nestor-Burma-Tome-14-Nestor-Burma-dans-l-ile-543160.html">Nestor Burma</a><br>
                14.                                         Nestor Burma dans l'ïle                                </h3>
              <div class="infos">
                <i class="icon-building"></i> <span class="orange">Casterman</span>
                <i class="icon-calendar"></i> Parution: <span class="orange">18/09/2026</span>
                <i class="icon-time"></i> <span class="orange">1ème semaine</span>
              </div>
              <p>
                Après le XXe arrondissement, nouvel album de Nestor Burma en Bretagne ![…]                                </p>
            </div>
          </li>
          <li>
            <div class="place">n°20</div>
            <a href="https://www.bedetheque.com/BD-Ezechiel-Tome-1-Le-pays-de-la-nuit-541192.html" class="couv" title="Ezechiel"><img src="https://www.bedetheque.com/cache/thb_couv/Couv_541192_f12293.jpg" class="fadeover"></a>
            <div class="evolution">
              <span class="moins"><i class="icon-arrow-down icon-large" title="-9 places par rapport à la semaine précedente"></i>-9</span>
            </div>
            <div class="main">
              <h3>
                <a href="https://www.bedetheque.com/BD-Ezechiel-Tome-1-Le-pays-de-la-nuit-541192.html">Ezechiel</a><br>
                1.                                         Le pays de la nuit                                </h3>
              <div class="infos">
                <i class="icon-building"></i> <span class="orange">Delcourt</span>
                <i class="icon-calendar"></i> Parution: <span class="orange">27/08/2026</span>
                <i class="icon-time"></i> <span class="orange">4ème semaine</span>
              </div>
              <p>Résumé.</p>
            </div>
          </li>
        </ol>
        </body></html>
        """;

    private const string ReleasesListHtml = """
        <html><body>
        <div class="widget-line-title"><h3>Les nouveautés de octobre 2026 (36)</h3></div>
        <div class="block-big block-big-last">
          <ul class="nouveautes-list">
            <li class="">
              <span class="ico"><a href="https://www.bedetheque.com/nouveautes?Origine=1&amp;Editeur=404+%C3%A9ditions&amp;Affichage=Liste" title="Afficher les sorties Franco-belge de 404 éditions"><img src="https://www.bdgest.com/skin/flags/europe.png"></a></span>
              <a class="editeur" href="https://www.bedetheque.com/nouveautes?Editeur=404+%C3%A9ditions&amp;Affichage=Liste">
                <span>404 éditions</span>
              </a>
              -
              <a title="tooltip" class="image-tooltip serie" rel="https://www.bedetheque.com/cache/thb_couv/Couv_543915.jpg" href="https://www.bedetheque.com/BD-Mawrth-Valliis-Tome-2-543915.html">
                <span class="serie">Mawrth Valliis</span>
                <span class="num"> -2- </span>
                <span class="numa"></span>
                <span class="titre">Tome 2</span>
              </a>
            </li>
            <li class="sep"><hr></li>
            <li class="">
              <span class="ico"><a href="https://www.bedetheque.com/nouveautes?Origine=1&amp;Editeur=Delcourt&amp;Affichage=Liste"><img src="https://www.bdgest.com/skin/flags/europe.png"></a></span>
              <a class="editeur" href="https://www.bedetheque.com/nouveautes?Editeur=Delcourt&amp;Affichage=Liste"><span>Delcourt</span></a>
              -
              <a title="tooltip" class="image-tooltip serie" rel="https://www.bedetheque.com/cache/thb_couv/Couv_543945_998c1d.jpg" href="https://www.bedetheque.com/BD-A-tout-jamais-543945.html">
                <span class="serie">À tout jamais</span>
                <span class="num"></span>
                <span class="numa"></span>
                <span class="titre"></span>
              </a>
            </li>
          </ul>
        </div>
        <div class="widget-line-title"><h3>Les nouveautés de aout 2026 (401)</h3></div>
        <div class="block-big block-big-last">
          <ul class="nouveautes-list">
            <li class="">
              <span class="ico"><a href="https://www.bedetheque.com/nouveautes?Origine=2&amp;Editeur=Delcourt&amp;Affichage=Liste"><img src="https://www.bdgest.com/skin/flags/manga.png"></a></span>
              <a class="editeur" href="#"><span>Delcourt</span></a>
              -
              <a title="tooltip" class="image-tooltip serie" rel="https://www.bedetheque.com/cache/thb_couv/Couv_543479.jpg" href="https://www.bedetheque.com/BD-Beginning-After-the-End-Tome-12-543479.html">
                <span class="serie">Beginning After the End (The)</span>
                <span class="num"></span>
                <span class="numa"> -INT03-</span>
                <span class="titre">Intégrale</span>
              </a>
            </li>
            <li class="invalide">
              <span class="ico"><a href="https://www.bedetheque.com/nouveautes?Origine=3&amp;Editeur=Urban&amp;Affichage=Liste"><img src="https://www.bdgest.com/skin/flags/comics.png"></a></span>
              <a class="editeur" href="#"><span>Urban Comics</span></a>
              -
              <a title="tooltip" class="image-tooltip serie" rel="" href="https://www.bedetheque.com/BD-Batman-Tome-3-540001.html">
                <span class="serie">Batman</span>
                <span class="num"> -3- </span>
                <span class="numa"></span>
                <span class="titre">Tome 3</span>
              </a>
            </li>
          </ul>
        </div>
        </body></html>
        """;

    private const string ReleasesCoverHtml = """
        <html><body>
        <ul class="gallery-couv-large">
          <li>
            <a href="https://www.bedetheque.com/BD-Mawrth-Valliis-Tome-2-543915.html" title="404 éditions - Mawrth Valliis -2- Tome 2 - Parution le : 08/10/2026">
              <img src="https://www.bedetheque.com/cache/thb_couv/Couv_543915.jpg" class="fadeover couv libre">
            </a>
          </li>
          <li>
            <a href="https://www.bedetheque.com/BD-Beginning-After-the-End-Tome-12-543479.html" title="Delcourt - Beginning After the End (The) -12- Tome 12 - Parution le : 20/08/2026">
              <img src="https://www.bedetheque.com/cache/thb_couv/Couv_543479.jpg" class="fadeover couv libre">
            </a>
          </li>
        </ul>
        </body></html>
        """;

    private const string AlbumHtml = """
        <html><body>
        <ul class="liste-albums">
          <li class="">
            <a name="543160"></a>
            <div class="album-side">
              <div class="couv">
                <a href="https://www.bedetheque.com/media/Couvertures/Couv_543160_b5a8ef.jpg" class="colorbox cboxElement"><img src="https://www.bdgest.com/skin/corner.top.left.png" class="corner-top-left"></a>
                <a class="zoom-format-icon browse-couvertures cboxElement" href="https://www.bedetheque.com/media/Couvertures/Couv_543160_b5a8ef.jpg">
                  <img src="https://www.bedetheque.com/cache/thb_couv/Couv_543160_b5a8ef.jpg" class="fadeover">
                </a>
              </div>
              <div class="sous-couv">
                <a class="zoom-format-icon colorbox cboxElement" href="https://www.bedetheque.com/media/Couvertures/Couv_543160_b5a8ef.jpg">
                  <img src="https://www.bedetheque.com/cache/thb_couv/Couv_543160_b5a8ef.jpg" style="height:63px;" class="fadeover">
                </a>
                <a class="zoom-format-icon browse-planches cboxElement" href="https://www.bedetheque.com/media/Planches/PlancheA_543160_16a237.jpg">
                  <img src="https://www.bedetheque.com/cache/thb_planches/PlancheA_543160_16a237.jpg" style="height:63px;" class="fadeover">
                </a>
                <a class="zoom-format-icon browse-versos cboxElement" href="https://www.bedetheque.com/media/Versos/Verso_543160_0cdae3.jpg">
                  <img src="https://www.bedetheque.com/cache/thb_versos/Verso_543160_0cdae3.jpg" style="height:63px;" class="fadeover">
                </a>
              </div>
            </div>
          </li>
          <li class="">
            <a name="544478"></a>
            <div class="sous-couv">
              <a class="zoom-format-icon browse-planches cboxElement" href="https://www.bedetheque.com/media/Planches/PlancheA_544478.jpg">
                <img src="https://www.bedetheque.com/cache/thb_planches/PlancheA_544478.jpg">
              </a>
            </div>
          </li>
        </ul>
        </body></html>
        """;

    private static HtmlDocument Load(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }

    [Fact]
    public void ParseTopSales_ExtraitSemaineRangEvolutionEtInfos()
    {
        var snapshot = BedethequeNewsProvider.ParseTopSales(Load(TopSalesHtml));

        Assert.NotNull(snapshot);
        Assert.Equal(new DateOnly(2026, 9, 14), snapshot.Week);
        Assert.Equal(2, snapshot.Entries.Count);

        var first = snapshot.Entries[0];
        Assert.Equal("543160", first.AlbumId);
        Assert.Equal(1, first.Rank);
        Assert.Equal("Nestor Burma", first.SeriesTitle);
        Assert.Equal("14", first.AlbumNumber);
        Assert.Equal("Nestor Burma dans l'ïle", first.AlbumTitle);
        Assert.Equal("Casterman", first.Publisher);
        Assert.Equal(new DateOnly(2026, 9, 18), first.ReleaseDate);
        Assert.Equal(1, first.WeeksInChart);
        Assert.Equal("New", first.Evolution);
        Assert.Null(first.EvolutionDelta);
        Assert.StartsWith("Après le XXe arrondissement", first.ShortDescription);
        Assert.Equal("https://www.bedetheque.com/cache/thb_couv/Couv_543160_b5a8ef.jpg", first.CoverUrl);
        Assert.Equal("https://www.bedetheque.com/media/Couvertures/Couv_543160_b5a8ef.jpg", first.CoverLargeUrl);

        var last = snapshot.Entries[1];
        Assert.Equal(20, last.Rank);
        Assert.Equal("Down", last.Evolution);
        Assert.Equal(9, last.EvolutionDelta);
        Assert.Equal(4, last.WeeksInChart);
        Assert.Equal("1", last.AlbumNumber);
    }

    [Fact]
    public void ParseTopSales_SansClassement_RenvoieNull()
        => Assert.Null(BedethequeNewsProvider.ParseTopSales(Load("<html><body></body></html>")));

    [Fact]
    public void ParseReleases_ExtraitItemsCategorieNumeroEtDate()
    {
        var entries = BedethequeNewsProvider.ParseReleases(Load(ReleasesListHtml), Load(ReleasesCoverHtml));

        Assert.Equal(4, entries.Count);

        var mawrth = entries[0];
        Assert.Equal("543915", mawrth.AlbumId);
        Assert.Equal("Mawrth Valliis", mawrth.SeriesTitle);
        Assert.Equal("2", mawrth.AlbumNumber);
        Assert.Equal("Tome 2", mawrth.AlbumTitle);
        Assert.Equal("404 éditions", mawrth.Publisher);
        Assert.Equal(NewsCategory.Bd, mawrth.Category);
        Assert.Equal(new DateOnly(2026, 10, 8), mawrth.ReleaseDate);          // date exacte (vue couverture)
        Assert.Equal("https://www.bedetheque.com/media/Couvertures/Couv_543915.jpg", mawrth.CoverLargeUrl);

        var oneShot = entries[1];
        Assert.Null(oneShot.AlbumNumber);
        Assert.Null(oneShot.AlbumTitle);
        Assert.Equal(new DateOnly(2026, 10, 1), oneShot.ReleaseDate);         // repli : mois de la section

        var manga = entries[2];
        Assert.Equal(NewsCategory.Manga, manga.Category);
        Assert.Equal("INT03", manga.AlbumNumber);
        Assert.Equal(new DateOnly(2026, 8, 20), manga.ReleaseDate);

        var comics = entries[3];
        Assert.Equal(NewsCategory.Comics, comics.Category);
        Assert.Null(comics.CoverUrl);
        Assert.Equal(new DateOnly(2026, 8, 1), comics.ReleaseDate);            // "aout" sans accent
    }

    [Fact]
    public void ParseReleases_SansVueCouverture_UtiliseLeMoisDeSection()
    {
        var entries = BedethequeNewsProvider.ParseReleases(Load(ReleasesListHtml), null);
        Assert.Equal(new DateOnly(2026, 10, 1), entries[0].ReleaseDate);
    }

    [Theory]
    [InlineData("Les nouveautés de février 2027 (12)", 2027, 2)]
    [InlineData("Les nouveautés de décembre 2026 (3)", 2026, 12)]
    [InlineData("Les nouveautés de aout 2026 (401)", 2026, 8)]
    public void ParseFrenchMonth_GereLesAccents(string heading, int year, int month)
        => Assert.Equal(new DateOnly(year, month, 1), BedethequeNewsProvider.ParseFrenchMonth(heading));

    [Fact]
    public void ParseAlbumImages_CirconscritALEditionDemandee()
    {
        var images = BedethequeSourceService.ParseAlbumImages(Load(AlbumHtml), 543160);

        Assert.Equal(3, images.Count);
        Assert.Equal(("Cover", "https://www.bedetheque.com/media/Couvertures/Couv_543160_b5a8ef.jpg"), (images[0].Kind, images[0].Url));
        Assert.Equal("https://www.bedetheque.com/cache/thb_couv/Couv_543160_b5a8ef.jpg", images[0].ThumbUrl);
        Assert.Equal(("Plate", "https://www.bedetheque.com/media/Planches/PlancheA_543160_16a237.jpg"), (images[1].Kind, images[1].Url));
        Assert.Equal(("Back", "https://www.bedetheque.com/media/Versos/Verso_543160_0cdae3.jpg"), (images[2].Kind, images[2].Url));
        Assert.DoesNotContain(images, i => i.Url.Contains("544478"));
    }

    [Fact]
    public void ToLargeCoverUrl_ConvertitLaMiniature()
    {
        Assert.Equal("https://www.bedetheque.com/media/Couvertures/Couv_1_ab.jpg",
            BedethequeSourceService.ToLargeCoverUrl("https://www.bedetheque.com/cache/thb_couv/Couv_1_ab.jpg"));
        Assert.Null(BedethequeSourceService.ToLargeCoverUrl(null));
    }

    [Fact]
    public void MondayOf_RenvoieLeLundi()
        => Assert.Equal(new DateOnly(2026, 9, 14), BedethequeNewsProvider.MondayOf(new DateOnly(2026, 9, 20)));
}

using HtmlAgilityPack;
using Inkhound.Core.Bedetheque;

namespace Inkhound.Core.Tests.Bedetheque;

public class BedethequeCatalogPageParsingTests
{
    private const string SampleHtml = """
        <html><body>
        <ul class="nav-liste">
          <li>
            <span class="ico"><img src="https://www.bedetheque.com/media/flags/France.png" /></span>
            <a href="https://www.bedetheque.com/serie-1234-BD-Schtroumpfs.html">
              <span class="libelle">Schtroumpfs&nbsp;(Les)</span>
            </a>
          </li>
          <li>
            <span class="ico"><img src="https://www.bedetheque.com/media/flags/Japan.png" /></span>
            <a href="https://www.bedetheque.com/serie-99-BD-Naruto.html">
              <span class="libelle">
                  Naruto
              </span>
            </a>
          </li>
          <li><a href="https://www.bedetheque.com/auteur-5-BD-Peyo.html"><span class="libelle">Pas une série</span></a></li>
          <li><a href="https://www.bedetheque.com/serie-77-BD-Vide.html"><span class="libelle"></span></a></li>
        </ul>
        </body></html>
        """;

    [Fact]
    public void ParseCatalogPage_ExtraitIdTitreLangueOrigine()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(SampleHtml);
        var fetchedAt = new DateTime(2026, 9, 19, 8, 0, 0, DateTimeKind.Utc);

        var entries = BedethequeSourceService.ParseCatalogPage(doc, "S", fetchedAt);

        Assert.Equal(2, entries.Count);

        var schtroumpfs = entries[0];
        Assert.Equal(1234, schtroumpfs.Id);
        Assert.Equal("Schtroumpfs (Les)", schtroumpfs.Title);   // entité HTML décodée, blancs fusionnés
        Assert.Equal("Français", schtroumpfs.Language);
        Assert.Equal("Europe", schtroumpfs.Origin);
        Assert.Equal("S", schtroumpfs.Letter);
        Assert.Equal(fetchedAt, schtroumpfs.FetchedAtUtc);

        var naruto = entries[1];
        Assert.Equal(99, naruto.Id);
        Assert.Equal("Naruto", naruto.Title);                   // indentation HTML nettoyée
        Assert.Equal("Japonais", naruto.Language);
        Assert.Equal("Asie", naruto.Origin);
    }

    [Fact]
    public void ParseCatalogPage_SansListe_RenvoieVide()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<html><body><p>Just a moment</p></body></html>");

        Assert.Empty(BedethequeSourceService.ParseCatalogPage(doc, "A", DateTime.UtcNow));
    }
}

using Inkhound.Core.Models;
using Inkhound.Core.Prowlarr;
using Inkhound.Core.Scoring;

namespace Inkhound.Core.Tests.Scoring;

public class TorrentRejectionRulesTests
{
    private static readonly VolumeAuthor[] NoAuthors = [];

    private static ProwlarrSearchResult MakeResult(string title, long size = 100L * 1_048_576L)
        => new(title, null, size, 10, 2, 0, "guid", 1, "Indexer", "torrent", "https://tracker/dl/1", null);

    // ── R2 : mauvaise série ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Amériques", "Isaac.Le.Pirate.T01.Les.Ameriques.Blain.2001.FR.[CBZ]-TONER")]      // titre d'album d'une autre série
    [InlineData("Amériques", "(Comics) Red Sonja - L'Autre Monde - (Chu - Gomez) - Tome 1 - 2018 - CBR")]
    [InlineData("N.É.O.", "Gantz.E.T06.Jin.KAGETSU.Hiroya.OKU.FRENCH.[CBZ]-NEO.RIP.Club")]        // sigle : « neo » n'est pas « gantz e »
    [InlineData("Le pouvoir de M", "Super.A.T01.Les.pouvoirs.Animaux.Moreau.2025.FR.[CBZ]-NRC")]
    [InlineData("Zombies", "[Comics] Kirkman - De Felici - Oblivion Song - Tome 3 - 2020 [cbr]")]
    public void WrongSeries_TitreAvecTomeMaisAutreSerie_EstRejete(string volumeTitle, string torrentTitle)
        => Assert.True(TorrentRejectionRules.IsWrongSeries(volumeTitle, NoAuthors, torrentTitle));

    [Theory]
    [InlineData("Les schtroumpfs", "Les Schtroumpfs - Tome 37 - Les Schtroumpfs et la machine à rêver (2019) CBZ")]
    [InlineData("Le duel", "La.Duelliste.T01.De.Dentelles.Et.De.Feintes.2025.FR.[CBZ]-NRC")]                  // préfixe duel/duelliste
    [InlineData("La tour (Kounen/Ladgham/Mr Fab)", "La.Tour.T02.Ladgham.MrFab.2022.FR.[CBZ]-TONER")]          // qualificatif entre parenthèses
    [InlineData("N.É.O.", "N.E.O.T05.L.Empire.De.La.Mort.L.Hermenier.De.Martino.2023.FR.[CBR]-Rip.Club")]     // sigle
    [InlineData("I.R.$.", "I.R.S.T26.Lart.Du.Profit.2026.FR.[CBZ][PDF]-KIKI")]                                // $ ≠ S, sigle quand même reconnu
    [InlineData("Orcs & Gobelins", "Terres.d'Arran.Orcs.&.Gobelins.Tomes.01.à.30-FR.CBZ")]
    public void WrongSeries_BonneSerie_NEstPasRejete(string volumeTitle, string torrentTitle)
        => Assert.False(TorrentRejectionRules.IsWrongSeries(volumeTitle, NoAuthors, torrentTitle));

    [Theory]
    [InlineData("Les Petits Hommes")]                       // titre nu : aucun marqueur de tome, on ne conclut pas
    [InlineData("PACK.5183.BANDES.DESSINEES.FRENCH.HYBRiD.eBOOK-DDD")]
    [InlineData("Canardo")]
    public void WrongSeries_SansMarqueurDeTome_NeConclutPas(string torrentTitle)
        => Assert.False(TorrentRejectionRules.IsWrongSeries("N.É.O.", NoAuthors, torrentTitle));

    [Fact]
    public void WrongSeries_UnAuteurDuVolumeDansLeTitre_ExempteLeTorrent()
    {
        // Série à titre alternatif : « Conquêtes » est publiée sous « Le Sang du Dragon ».
        VolumeAuthor[] authors = [new("Istin, Jean-Luc", "Scénario"), new("Créty, Stéphane", "Dessin")];

        Assert.False(TorrentRejectionRules.IsWrongSeries(
            "Conquêtes", authors, "[BD] Le Sang du Dragon - Tomes 1 à 12 - Istin - Michel - Crety - 2005-2018 - [cbr]"));
        Assert.True(TorrentRejectionRules.IsWrongSeries(
            "Conquêtes", NoAuthors, "[BD] Le Sang du Dragon - Tomes 1 à 12 - Istin - Michel - Crety - 2005-2018 - [cbr]"));
    }

    [Fact]
    public void WrongSeries_MarqueurEnTete_RetombeSurLeTitreEntier()
        => Assert.False(TorrentRejectionRules.IsWrongSeries("Le duel", NoAuthors, "T01 Le Duel - Pécau"));

    // ── R3 : année anachronique ─────────────────────────────────────────────

    [Theory]
    [InlineData(2021, "Jim Hawkins - (Vastra) - Tome 3 - 2018 [RipClub] - CBR", true)]    // l'album sort 3 ans plus tard
    [InlineData(2025, "Kran.T01.Les.Runes.De.Gartageul.Herenguel.1999.FR.[CBZ]-TONER", true)]
    [InlineData(2019, "Jim Hawkins - (Vastra) - Tome 2 - 2018 [iBooker] - CBR", false)]   // un an d'écart : tolérance
    [InlineData(2018, "Sherman - (Desberg - Griffo) - Tome 8 - 2018 [PRiNTER] - CBZ", false)]
    [InlineData(2026, "Ernest & Rebecca T01 à T09 - 2008-2019 - BDS - FR - CBZ", false)]  // plage : pack, jamais jugé sur l'année
    [InlineData(2021, "Les.Maitres.Inquisiteurs.2015.[INTEGRALE].FR.[CBZ]-NOTAG", false)]  // intégrale
    [InlineData(2021, "Les Chroniques - Intégrale Tome 1 - 2015", false)]                  // intégrale accentuée
    [InlineData(2021, "Titre - Tome 3 - 2018 - CBR", true)]                               // « 3 - 2018 » n'est pas une plage de tomes
    [InlineData(2021, "Titre sans année Tome 3", false)]
    [InlineData(2021, "Titre sans marqueur de tome 2015", false)]
    public void AnachronisticYear_Cas(int issueYear, string torrentTitle, bool expected)
        => Assert.Equal(expected, TorrentRejectionRules.IsAnachronisticYear(issueYear, torrentTitle));

    // ── R4 : format ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Erlendur.Sveinsson.T1.Le.Duel.2014.FRENCH.[EPUB]-NOTAG", true)]
    [InlineData("A. B. Guthrie The Big Sky Tome 1 La captive aux yeux clairs [epub]", true)]
    [InlineData("Les.Schtroumpfs.FRENCH.HYBRiD.COMiC.CBZ.eBook-TONER", false)]            // eBook + CBZ : archive de BD
    [InlineData("Titre T01 [epub] [pdf]", false)]
    [InlineData("Titre T01 cbz", false)]
    public void NonComicFormat_Cas(string torrentTitle, bool expected)
        => Assert.Equal(expected, TorrentRejectionRules.IsNonComicFormat(torrentTitle));

    // ── R5 : langue ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ElfQuest (Le pays de elfes) - intégrale - ordre chronologique - CBR - EN", true)]
    [InlineData("Saga Vol 1 ENGLISH CBZ", true)]
    [InlineData("Pathfinder 2nd Edition - Mega Collection [PDF] EN/FR", false)]         // bilingue
    [InlineData("Titre T01 EN FR CBZ", false)]
    [InlineData("Les.Schtroumpfs.T01.FRENCH.CBZ", false)]
    [InlineData("Un pays en guerre T01 cbz", false)]                                     // « en » minuscule = mot français
    public void EnglishOnly_Cas(string torrentTitle, bool expected)
        => Assert.Equal(expected, TorrentRejectionRules.IsEnglishOnly(torrentTitle));

    // ── R6 : contenu non-BD ─────────────────────────────────────────────────

    [Theory]
    [InlineData("[JDR] Ambre, le jeu de rôle sans dé / Amber RPG / Amberzine (intégrale)", true)]
    [InlineData("[BD][ArtBook] Souillé - Ledroit - L'univers féerique d'Olivier Ledroit - Tome 1 et 2 - 2005 [cbr]", true)]
    [InlineData("Lanfeust de Troy - Film complet DVD", true)]
    [InlineData("Tramp.T15.Les.Naufrages.Du.Lavalette.2026.FR.[CBZ][PDF]-KIKI", false)]
    [InlineData("Les aventures de Rpgman T02 cbz", false)]                                // mot entier uniquement
    [InlineData("Les rois du dvdrip T01 cbz", false)]                                    // mot entier uniquement
    public void NonBdContent_Cas(string torrentTitle, bool expected)
        => Assert.Equal(expected, TorrentRejectionRules.IsNonBdContent(torrentTitle));

    // ── Evaluate ────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_ResultatValide_RetourneNull()
    {
        var volume = new Volume { Title = "Valhalla Hotel", CountOfIssues = 5 };
        var issue = new Issue { IssueNumber = 1, Year = 2021 };

        Assert.Null(TorrentRejectionRules.Evaluate(volume, issue,
            MakeResult("Valhalla.Hotel.T01.Bite.The.Bullet.Perna.Bedouel.2021.FR.[CBZ]-TONER")));
    }

    [Fact]
    public void Evaluate_RetourneLeCodeDeLaRegle()
    {
        var volume = new Volume { Title = "Amériques", CountOfIssues = 3 };
        var issue = new Issue { IssueNumber = 1, Year = 2026 };

        Assert.Equal(TorrentRejection.WrongSeriesCode,
            TorrentRejectionRules.Evaluate(volume, issue, MakeResult("Isaac.Le.Pirate.T01.Les.Ameriques.Blain.2001.FR.[CBZ]"))?.Code);
        Assert.Equal(TorrentRejection.NonComicFormatCode,
            TorrentRejectionRules.Evaluate(volume, issue, MakeResult("Amériques T01 [EPUB]"))?.Code);
    }

    [Fact]
    public void Evaluate_AnneeAnachronique_ExigeUneIssue()
    {
        var volume = new Volume { Title = "Jim Hawkins", CountOfIssues = 5 };
        var result = MakeResult("Jim Hawkins - (Vastra) - Tome 3 - 2018 [RipClub] - CBR");

        Assert.Equal(TorrentRejection.AnachronisticYearCode,
            TorrentRejectionRules.Evaluate(volume, new Issue { IssueNumber = 3, Year = 2021 }, result)?.Code);
        Assert.Null(TorrentRejectionRules.Evaluate(volume, null, result));                    // niveau volume : règle ignorée
        Assert.Null(TorrentRejectionRules.Evaluate(volume, new Issue { IssueNumber = 3, Year = null }, result));
    }

    // ── Intégration au scoring ──────────────────────────────────────────────

    [Fact]
    public void ScoringTorrent_ResultatRejete_ScoreZeroEtMotifExpose()
    {
        var volume = new Volume { Title = "Amériques", CountOfIssues = 3 };
        var issue = new Issue { IssueNumber = 1, Year = 2026 };
        var result = MakeResult("Isaac.Le.Pirate.T01.Les.Ameriques.Blain.2001.FR.[CBZ]-TONER", 80L * 1_048_576L);

        var scored = ScoringTorrent.ScoringIndexerResult(volume, issue, result);

        Assert.Equal(0f, scored.Score);
        Assert.False(scored.Banned);
        Assert.Equal(TorrentRejection.WrongSeriesCode, scored.Rejection?.Code);
    }

    [Fact]
    public void ScoringTorrent_ResultatValide_NEstPasRejete()
    {
        var volume = new Volume { Title = "Valhalla Hotel", CountOfIssues = 5 };
        var issue = new Issue { IssueNumber = 1, Year = 2021 };
        var result = MakeResult("Valhalla.Hotel.T01.Bite.The.Bullet.Perna.Bedouel.2021.FR.[CBZ]-TONER", 80L * 1_048_576L);

        var scored = ScoringTorrent.ScoringIndexerResult(volume, issue, result);

        Assert.Null(scored.Rejection);
        Assert.True(scored.Score >= 70f);
    }

    [Fact]
    public void ScoringVolumePack_ResultatRejete_ScoreZero()
    {
        var volume = new Volume { Title = "Zombies", CountOfIssues = 4 };
        List<Issue> missing = [new Issue { IssueNumber = 1 }, new Issue { IssueNumber = 2 }];
        var result = MakeResult("[Comics] Kirkman - De Felici - Oblivion Song - Tome 1 - 2018 [cbr]");

        var scored = ScoringVolumePack.ScoringIndexerResult(volume, missing, result);

        Assert.Equal(0f, scored.Score);
        Assert.Equal(TorrentRejection.WrongSeriesCode, scored.Rejection?.Code);
    }
}

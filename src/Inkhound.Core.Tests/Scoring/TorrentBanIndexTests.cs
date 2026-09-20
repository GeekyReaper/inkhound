using Inkhound.Core.Models;
using Inkhound.Core.Prowlarr;
using Inkhound.Core.Scoring;

namespace Inkhound.Core.Tests.Scoring;

public class TorrentBanIndexTests
{
    private static IssueTorrentBan MakeBan(
        string downloadUrl = "", string torrentHash = "", string torrentTitle = "")
        => new()
        {
            Id = Guid.NewGuid(),
            IssueId = Guid.NewGuid(),
            DownloadUrl = downloadUrl,
            TorrentHash = torrentHash,
            TorrentTitle = torrentTitle,
            CreatedAt = DateTime.UtcNow
        };

    // (Title, InfoUrl, Size, Seeders, Leechers, ReleaseWeight, Guid, IndexerId, Indexer, Protocol, DownloadUrl, PublishDate)
    private static ProwlarrSearchResult MakeResult(
        string title = "Elfes - Tome 27", string? downloadUrl = null, string guid = "guid")
        => new(title, null, 30L * 1_048_576L, 10, 2, 0, guid, 1, "Indexer", "torrent", downloadUrl, null);

    [Fact]
    public void Empty_NeBannitRien()
    {
        Assert.False(TorrentBanIndex.Empty.IsBanned(MakeResult(downloadUrl: "https://x/t.torrent")));
        Assert.Equal(0, TorrentBanIndex.Empty.Count);
    }

    [Fact]
    public void IsBanned_ParUrl_InsensibleALaCasseEtAuxEspaces()
    {
        var index = TorrentBanIndex.From([MakeBan(downloadUrl: "  https://tracker/Download/ABC  ")]);

        Assert.True(index.IsBanned(MakeResult(downloadUrl: "https://tracker/download/abc")));
        Assert.False(index.IsBanned(MakeResult(downloadUrl: "https://tracker/download/other")));
    }

    [Fact]
    public void IsBanned_ParGuidQuandIlPorteLUrlOuLeHash()
    {
        // Certains indexers exposent l'URL (ou le hash) de la release dans le champ Guid.
        var byUrl = TorrentBanIndex.From([MakeBan(downloadUrl: "https://tracker/download/abc")]);
        Assert.True(byUrl.IsBanned(MakeResult(guid: "https://tracker/download/abc")));

        var byHash = TorrentBanIndex.From([MakeBan(torrentHash: "A1B2C3")]);
        Assert.True(byHash.IsBanned(MakeResult(guid: "a1b2c3")));
    }

    [Fact]
    public void IsBanned_ParTitreNormalise_AccentsCasseEtPonctuationIgnores()
    {
        var index = TorrentBanIndex.From([MakeBan(torrentTitle: "Elfes - Tome 27 (2020) [CBZ]")]);

        Assert.True(index.IsBanned(MakeResult("elfes   tome 27  2020  cbz")));
        Assert.False(index.IsBanned(MakeResult("Elfes - Tome 28 (2021) [CBZ]")));
    }

    [Fact]
    public void IsBanned_ClesVides_NeMatchentJamais()
    {
        // Un ban sans URL ne doit pas bannir tous les résultats d'un indexer qui n'en fournit pas.
        var index = TorrentBanIndex.From([MakeBan(downloadUrl: "", torrentHash: "", torrentTitle: "")]);

        Assert.False(index.IsBanned(MakeResult(title: "", downloadUrl: null, guid: "")));
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void IsBanned_UrlDifferenteMaisMemeTitre_BanniParRepli()
    {
        // L'indexer a régénéré ses liens : l'URL ne matche plus, le titre reste la clé de repli.
        var index = TorrentBanIndex.From([MakeBan(
            downloadUrl: "https://tracker/download/old",
            torrentTitle: "Elfes - Tome 27")]);

        Assert.True(index.IsBanned(MakeResult("Elfes - Tome 27", downloadUrl: "https://tracker/download/new")));
    }
}

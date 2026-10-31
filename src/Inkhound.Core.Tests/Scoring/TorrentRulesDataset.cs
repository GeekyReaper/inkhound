using System.Text.Json;
using Inkhound.Core.Models;
using Inkhound.Core.Prowlarr;

namespace Inkhound.Core.Tests.Scoring;

/// <summary>
/// Une ligne du jeu de données <c>Fixtures/torrent-rules-dataset.json</c>, extrait d'une copie de la base de
/// production : un torrent que l'utilisateur a <b>banni</b> (<c>banned</c>) ou <b>conservé</b> (<c>kept</c>, un
/// download terminé) pour une issue d'un volume. Titres et métadonnées uniquement — ni hash, ni URL.
/// </summary>
public sealed record TorrentRulesRow(
    string Kind,
    string TorrentTitle,
    string? Tracker,
    string VolumeId,
    string VolumeTitle,
    int? VolumeYear,
    int VolumeCount,
    string? Publisher,
    List<string> AuthorNames,
    int IssueNumber,
    int? IssueYear,
    string? IssueCategory,
    int BanOrder)
{
    public bool IsBanned => Kind == "banned";

    public Volume ToVolume() => new()
    {
        Title = VolumeTitle,
        Year = VolumeYear,
        CountOfIssues = VolumeCount,
        Publisher = Publisher,
        Authors = [.. AuthorNames.Select(n => new VolumeAuthor(n, string.Empty))]
    };

    public Issue ToIssue() => new() { IssueNumber = IssueNumber, Year = IssueYear, Authors = [] };

    public ProwlarrSearchResult ToResult()
        => new(TorrentTitle, null, 100L * 1_048_576L, 10, 2, 0, "guid", 1, "Indexer", "torrent", null, null);
}

public static class TorrentRulesDataset
{
    private static readonly Lazy<List<TorrentRulesRow>> Rows = new(Load);

    public static IReadOnlyList<TorrentRulesRow> All => Rows.Value;
    public static IEnumerable<TorrentRulesRow> Banned => All.Where(r => r.IsBanned);
    public static IEnumerable<TorrentRulesRow> Kept => All.Where(r => !r.IsBanned);

    private static List<TorrentRulesRow> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Scoring", "Fixtures", "torrent-rules-dataset.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<TorrentRulesRow>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException("Empty dataset.");
    }
}

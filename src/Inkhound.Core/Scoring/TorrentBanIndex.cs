using Inkhound.Core.Models;
using Inkhound.Core.Prowlarr;

namespace Inkhound.Core.Scoring;

/// <summary>
/// Index mémoire des torrents bannis pour un périmètre déjà choisi par l'appelant (les bans d'une
/// issue pour une recherche par issue, ceux de toutes les issues d'un volume pour une recherche par
/// volume) : le scoring n'a plus qu'une question à poser, <see cref="IsBanned"/>.
/// Rapprochement par URL de téléchargement, puis par hash, puis par titre normalisé — un résultat
/// Prowlarr ne porte pas de hash (il n'existe qu'après l'ajout à qBittorrent), c'est donc l'URL qui
/// tranche en pratique, le titre servant de repli quand l'indexer régénère ses liens.
/// </summary>
public sealed class TorrentBanIndex
{
    /// <summary>Index vide — ne bannit rien.</summary>
    public static readonly TorrentBanIndex Empty = new([], [], []);

    private readonly HashSet<string> _urls;
    private readonly HashSet<string> _hashes;
    private readonly HashSet<string> _titles;

    private TorrentBanIndex(HashSet<string> urls, HashSet<string> hashes, HashSet<string> titles)
    {
        _urls = urls;
        _hashes = hashes;
        _titles = titles;
    }

    /// <summary>Nombre de bans indexés (clés distinctes confondues).</summary>
    public int Count => _urls.Count + _hashes.Count + _titles.Count;

    public static TorrentBanIndex From(IEnumerable<IssueTorrentBan> bans)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var titles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var ban in bans)
        {
            AddIfNotEmpty(urls, ban.DownloadUrl?.Trim());
            AddIfNotEmpty(hashes, ban.TorrentHash?.Trim());
            AddIfNotEmpty(titles, NormalizeTitle(ban.TorrentTitle));
        }

        return new TorrentBanIndex(urls, hashes, titles);
    }

    /// <summary>Ce résultat correspond-il à un torrent banni dans le périmètre indexé ?</summary>
    public bool IsBanned(ProwlarrSearchResult result)
    {
        // Une clé vide ne matche jamais : sans ça, un ban sans URL bannirait tous les résultats
        // d'un indexer qui n'en fournit pas.
        var url = result.DownloadUrl?.Trim();
        if (!string.IsNullOrEmpty(url) && _urls.Contains(url)) return true;

        var guid = result.Guid?.Trim();
        if (!string.IsNullOrEmpty(guid) && (_urls.Contains(guid) || _hashes.Contains(guid))) return true;

        var title = NormalizeTitle(result.Title);
        return !string.IsNullOrEmpty(title) && _titles.Contains(title);
    }

    private static void AddIfNotEmpty(HashSet<string> set, string? value)
    {
        if (!string.IsNullOrEmpty(value)) set.Add(value);
    }

    // Même normalisation que le scoring de titres (accents/casse/ponctuation), blancs réduits :
    // deux releases identiques réencodées par des trackers différents se rapprochent quand même.
    private static string NormalizeTitle(string? title)
        => string.IsNullOrWhiteSpace(title) ? string.Empty : TextSimilarity.Normalize(title);
}

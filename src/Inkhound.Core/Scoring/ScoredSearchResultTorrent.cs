using Inkhound.Core.Analysis;
using Inkhound.Core.Prowlarr;

namespace Inkhound.Core.Scoring;

public record ScoreDetailsTorrent(
    float TitleMatch,
    float IssueNumberMatch,
    float YearMatch,
    float AuthorMatch,
    float PublisherMatch,
    float SizePlausibility,
    float SeederScore,
    float FormatScore);

public record ScoredSearchResultTorrent(
    ProwlarrSearchResult Result,
    float Score,
    ScoreDetailsTorrent Details,
    TorrentAnalysis Analysis,
    // Le torrent est banni pour l'issue recherchée (voir TorrentBanIndex) : Score vaut alors 0 et
    // l'UI l'indique explicitement plutôt que de laisser croire à un simple mauvais résultat.
    bool Banned = false);

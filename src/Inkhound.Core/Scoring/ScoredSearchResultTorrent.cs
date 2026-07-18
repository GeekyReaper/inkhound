using Inkhound.Core.Analysis;
using Inkhound.Core.Prowlarr;

namespace Inkhound.Core.Scoring;

public record ScoreDetailsTorrent(
    float TitleMatch,
    float IssueNumberMatch,
    float YearMatch,
    float SizePlausibility,
    float SeederScore,
    float FormatScore);

public record ScoredSearchResultTorrent(
    ProwlarrSearchResult Result,
    float Score,
    ScoreDetailsTorrent Details,
    TorrentAnalysis Analysis);

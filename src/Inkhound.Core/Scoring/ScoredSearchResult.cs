using Inkhound.Core.Analysis;
using Inkhound.Core.Prowlarr;

namespace Inkhound.Core.Scoring;

public record ScoreDetails(
    float TitleMatch,
    float IssueNumberMatch,
    float YearMatch,
    float SizePlausibility,
    float SeederScore,
    float FormatScore);

public record ScoredSearchResult(
    ProwlarrSearchResult Result,
    float Score,
    ScoreDetails Details,
    TorrentAnalysis Analysis);

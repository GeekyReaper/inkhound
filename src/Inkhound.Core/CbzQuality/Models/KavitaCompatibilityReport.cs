namespace Inkhound.Core.CbzQuality.Models;

public sealed record KavitaCompatibilityReport
{
    public required int Score { get; init; }
    public required string ScoreBand { get; init; }
    public required IReadOnlyList<KavitaIssue> Issues { get; init; }
    public required int ErrorCount { get; init; }
    public required int WarningCount { get; init; }
    public required int InfoCount { get; init; }
}

namespace Inkhound.Core.CbzQuality.Models;

/// <summary>
/// Maps a minimum score (inclusive) to a human-readable band label. Evaluated in order, first match
/// wins, so a <see cref="ScoringSettings.ScoreBands"/> list must stay sorted by descending <see cref="Min"/>.
/// </summary>
public sealed record ScoreBandDefinition
{
    public required int Min { get; init; }
    public required string Band { get; init; }
}

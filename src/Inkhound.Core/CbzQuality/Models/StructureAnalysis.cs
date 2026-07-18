namespace Inkhound.Core.CbzQuality.Models;

public sealed record StructureAnalysis
{
    public required bool IsFlat { get; init; }
    public required int MaxFolderDepth { get; init; }
    public required IReadOnlyList<string> DistinctDirectories { get; init; }
    public required string TreeText { get; init; }
}

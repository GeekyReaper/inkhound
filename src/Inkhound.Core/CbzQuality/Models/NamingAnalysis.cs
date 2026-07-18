namespace Inkhound.Core.CbzQuality.Models;

public sealed record NamingAnalysis
{
    public required bool IsConsistentZeroPadding { get; init; }
    public required bool LexicographicOrderMatchesNaturalOrder { get; init; }
    public required int MaxDigitCount { get; init; }
    public required int MinDigitCount { get; init; }
    public required IReadOnlyList<int> DetectedGaps { get; init; }
    public required IReadOnlyList<int> DetectedDuplicates { get; init; }
    public string? CoverImageEntryName { get; init; }
}

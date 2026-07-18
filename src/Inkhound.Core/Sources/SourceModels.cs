namespace Inkhound.Core.Sources;

public record SourceVolume(
    string SourceId,
    string Name,
    int? StartYear,
    int CountOfIssues,
    string? Publisher,
    string? Description,
    string? ImageUrl);

public record SourceIssue(
    string SourceId,
    string? Name,
    string IssueNumber,
    DateTime? CoverDate,
    string? Description,
    string? ImageUrl);

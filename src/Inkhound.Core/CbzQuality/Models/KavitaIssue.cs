namespace Inkhound.Core.CbzQuality.Models;

public enum KavitaIssueSeverity
{
    Info,
    Warning,
    Error
}

public sealed record KavitaIssue
{
    public required KavitaIssueSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required int PointsDeducted { get; init; }
}

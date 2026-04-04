namespace Inkhound.Web.Jobs;

/// <summary>
/// Implemented by each business service that can be run as a job.
/// </summary>
public interface IJobHandler
{
    Task<(bool IsValid, string? Error, string Title)> ValidateAsync(Dictionary<string, string> parameters);
    Task RunAsync(JobContext ctx, CancellationToken ct);
}

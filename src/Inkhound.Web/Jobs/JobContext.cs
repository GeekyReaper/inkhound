namespace Inkhound.Web.Jobs;

public class JobContext
{
    public string                     Id          { get; init; } = Guid.NewGuid().ToString();
    public string                     JobType     { get; init; } = string.Empty;
    public string                     Title       { get; set;  } = string.Empty;
    public string                     RequestedBy { get; init; } = string.Empty;
    public Dictionary<string, string> Parameters  { get; init; } = [];
    public JobStatus                  Status      { get; set;  } = JobStatus.Init;
    public DateTime                   StartedAt   { get; init; } = DateTime.UtcNow;
    public DateTime?                  EndedAt     { get; set;  }
    public int                        Progress    { get; set;  }                   // 0-100
    public Dictionary<string, string> Result      { get; }      = [];
    public List<JobTrace>             Traces      { get; }      = [];

    public void SetProgress(int value)          => Progress = Math.Clamp(value, 0, 100);
    public void SetResult(string key, string v) => Result[key] = v;
}

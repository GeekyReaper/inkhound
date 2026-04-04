namespace Inkhound.Web.Jobs;

public enum JobStatus
{
    Init,
    Running,
    Failed,
    Succeeded,
    Canceled
}

public enum TraceLevel
{
    Info,
    Error,
    Success,
    Cancel,
    Timeout
}

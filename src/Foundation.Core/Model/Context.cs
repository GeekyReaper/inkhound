
namespace Foundation.Core.Model;

using System;
using System.Text.Json.Serialization;


public enum JobState { INITIALIZING, RUNNING, SUCCESS, ERROR }

public class ProgressionCallback
{
    public Guid JobId { get; set; }
    public Action<Progression> Callback { get; set; } = _ => { };
    public Action<int> UpdateTotal { get; set; } = _ => { };
}

public class Progression
{
    public int Total { get; set; } = 0;
    public int Completed { get; set; } = 0;
    public int Error { get; set; } = 0;

    public int Percentage
    {
        get
        {
            if (Total == 0) return 0;
            return (int)((double)Completed / Total * 100);
        }
    }

    public void Reset()
    {
        Total = 0;
        Completed = 0;
        Error = 0;
    }

    public void Increment(bool success = true, int increment = 1)
    {
        Completed += increment;
        if (!success)
        {
            Error += increment;
        }

        if (Completed > Total)
            Total = Completed;
    }
    public void IncrementTotal(int total)
    {
        Total = total;
    }
}

public class JobContext
{
    public Guid JobId { get; } = Guid.NewGuid();
    public JobState State { get; private set; } = JobState.INITIALIZING;

    public TimeSpan Duration => EndDate.HasValue ? EndDate.Value - StartDate : DateTime.UtcNow - StartDate;

    public string Title { get; set; } = string.Empty;

    public Progression Progress { get; } = new Progression();

    public DateTime StartDate { get; } = DateTime.UtcNow;
    public DateTime? EndDate { get; private set; } = null;

    [JsonIgnore]
    public Action<JobContext>? OnUpdated { get; set; } = null;

    [JsonIgnore]
    public ProgressionCallback CallbackHandler { get; }

    public JobContext(Guid? guid = null)
    {
        JobId = guid == null ? Guid.NewGuid() : guid.Value;
        CallbackHandler = new ProgressionCallback
        {
            JobId = JobId,
            Callback = SetProgress,
            UpdateTotal = AddTotal
        };

    }

    public void SetState(JobState newState)
    {
        State = newState;
        if (newState == JobState.SUCCESS || newState == JobState.ERROR)
        {
            EndDate = DateTime.UtcNow;
        }
        OnUpdated?.Invoke(this);
    }

    public void SetProgress(Progression progression)
    {
        Progress.Completed = progression.Completed;
        Progress.Error = progression.Error;
        OnUpdated?.Invoke(this);
    }
    public void AddTotal(int value)
    {
        Progress.IncrementTotal(value);
        OnUpdated?.Invoke(this);
    }




}

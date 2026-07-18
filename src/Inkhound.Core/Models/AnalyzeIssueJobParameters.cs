using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

public class AnalyzeIssueJobParameters : IJobParameters
{
    public Guid IssueId { get; set; }

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (IssueId == Guid.Empty)
            errors.Add("IssueId is required.");
        return errors.Count == 0;
    }
}

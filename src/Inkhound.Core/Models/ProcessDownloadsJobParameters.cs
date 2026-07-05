using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

public class ProcessDownloadsJobParameters : IJobParameters
{
    public Guid? IssueDownloadId { get; set; }

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        return true;
    }
}

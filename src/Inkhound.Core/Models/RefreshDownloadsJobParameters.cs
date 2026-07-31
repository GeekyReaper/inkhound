using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

public class RefreshDownloadsJobParameters : IJobParameters
{
    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        return true;
    }
}

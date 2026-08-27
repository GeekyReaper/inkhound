
using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

public class RecalculateLibraryStatisticsJobParameters : IJobParameters
{
    public Guid LibraryId { get; set; }

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (LibraryId == Guid.Empty)
            errors.Add("LibraryId cannot be empty.");
        return errors.Count == 0;
    }
}

using Foundation.Core.Interface;
using Inkhound.Core.Export;

namespace Inkhound.Core.Models;

public class ExportJobParameters : IJobParameters
{
    public ExportTargetType TargetType { get; set; }

    public Guid TargetId { get; set; }

    public ExportFormat Format { get; set; }

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (TargetId == Guid.Empty)
            errors.Add("TargetId is required.");
        return errors.Count == 0;
    }
}

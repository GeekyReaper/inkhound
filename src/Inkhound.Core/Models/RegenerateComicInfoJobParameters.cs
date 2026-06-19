
using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

public class RegenerateComicInfoJobParameters : IJobParameters
{
    public Guid VolumeId { get; set; }

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (VolumeId == Guid.Empty)
            errors.Add("VolumeId cannot be empty.");
        return errors.Count == 0;
    }
}

using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

public class SearchVolumesJobParameters : IJobParameters
{
    public string Name { get; set; } = string.Empty;
    public int Page { get; set; } = 1;
    public int? PageSize { get; set; }

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Name is required.");
        return errors.Count == 0;
    }
}

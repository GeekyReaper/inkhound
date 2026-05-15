
using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

public class ArchiveConverterPdfJobParameters : IJobParameters
{
    public string SourceFile { get; set; }
    public string WorkingPath { get; set; } = string.Empty;

    public Volume Volume { get; set; } = new Volume();
    public Issue Issue { get; set; } = new Issue();


    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (string.IsNullOrEmpty(SourceFile))
        {
            errors.Add($"SourcePath can not be null.");
        }
        if (string.IsNullOrEmpty(WorkingPath))
        {
            errors.Add($"WorkingPath can not be null.");
        }
        if (string.IsNullOrEmpty(Volume.Title))
        {
            errors.Add($"Volume is empty.");
        }
        if (string.IsNullOrEmpty(Issue.Title) || Issue.IssueNumber == 0)
        {
            errors.Add($"Issue is empty.");
        }
        return errors.Count == 0;
    }
}

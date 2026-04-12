
using System;
using Foundation.Core.Interface;
using Inkhound.Core.Models;

namespace Inkhound.Core.ComicArchiveGenerator;

public class ArchiveConverterPdfJobParameters : IJobParameters
{
    public string SourcePath { get; set; } = string.Empty;
    public string WorkingPath { get; set; } = string.Empty;

    public Volume volume { get; set; } = new Volume();
    public Issue issue { get; set; } = new Issue();


    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (string.IsNullOrEmpty(SourcePath))
        {
            errors.Add($"SourcePath can not be null.");
        }
        if (string.IsNullOrEmpty(WorkingPath))
        {
            errors.Add($"WorkingPath can not be null.");
        }
        return errors.Count == 0;
    }
}

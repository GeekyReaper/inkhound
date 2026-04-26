using System;
using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.ComicArchiveGenerator;

public class ArchiveOption : IOptionList
{
    public string WorkingPath { get; set; } = "data/working";
    public string ImportPath { get; set; } = "data/import";

    public List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new OptionDefinition
            {
                Name = nameof(WorkingPath),
                Description = "The path where the working files will be stored.",
                ValueType = EValueType.STRING,
                DefaultValue = "data/working",
                Mandatory = true,
            },
            new OptionDefinition
            {
                Name = nameof(ImportPath),
                Description = "The path where the import files will be stored.",
                ValueType = EValueType.STRING,
                DefaultValue = "data/import"
            }
        };
    }

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();

        if (WorkingPath == null)
        {
            errors.Add("Working path is null.");
        }

        if (ImportPath == null)
        {
            errors.Add("Import path is null.");
        }

        return errors.Count == 0;
    }

    public bool LoadOptions(List<OptionDefinition> options, out List<string> errors)
    {
        errors = new List<string>();

        foreach (var option in options)
        {
            if (option.IsValid(out var optionErrors))
            {

                if (option.Name == nameof(WorkingPath))
                {
                    WorkingPath = option.Value;
                }
                else if (option.Name == nameof(ImportPath))
                {
                    ImportPath = option.Value;
                }
            }
            errors.AddRange(optionErrors);
        }

        return IsValid(out errors);
    }
}
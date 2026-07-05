using System;
using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.ComicArchiveGenerator;

public class ArchiveOption : IOptionList
{
    public string WorkingPath { get; set; } = "data/working";
    public string ImportPath { get; set; } = "data/import";
    public string ImagesPath { get; set; } = "data/images";
    public string DownloadsPath { get; set; } = "data/downloads";

    public List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new OptionDefinition
            {
                Name = nameof(WorkingPath),
                Description = "The path where the working files will be stored.",
                ValueType = EValueType.PATH,
                DefaultValue = "data/working",
                Mandatory = true,
            },
            new OptionDefinition
            {
                Name = nameof(ImportPath),
                Description = "The path where the import files will be stored.",
                ValueType = EValueType.PATH,
                DefaultValue = "data/import"
            },
            new OptionDefinition
            {
                Name = nameof(ImagesPath),
                Description = "The path where uploaded cover images will be stored.",
                ValueType = EValueType.PATH,
                DefaultValue = "data/images"
            },
            new OptionDefinition
            {
                Name = nameof(DownloadsPath),
                Description = "The path where files downloaded by qBittorrent will land.",
                ValueType = EValueType.PATH,
                DefaultValue = "data/downloads"
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

        if (ImagesPath == null)
        {
            errors.Add("Images path is null.");
        }

        if (DownloadsPath == null)
        {
            errors.Add("Downloads path is null.");
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
                else if (option.Name == nameof(ImagesPath))
                {
                    ImagesPath = option.Value;
                }
                else if (option.Name == nameof(DownloadsPath))
                {
                    DownloadsPath = option.Value;
                }
            }
            errors.AddRange(optionErrors);
        }

        return IsValid(out errors);
    }
}
using System;
using System.Linq;
using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.ComicArchiveGenerator;

public enum PdfPageFormat { Jpeg, Png }

public class ArchiveOption : IOptionList
{
    public string WorkingPath { get; set; } = "data/working";
    public string ImportPath { get; set; } = "data/import";
    public string ImagesPath { get; set; } = "data/images";
    public string DownloadsPath { get; set; } = "data/downloads";
    public int PdfDpi { get; set; } = 150;
    public PdfPageFormat PdfImageFormat { get; set; } = PdfPageFormat.Jpeg;
    public int PdfJpegQuality { get; set; } = 90;

    public List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new OptionDefinition
            {
                Name = nameof(WorkingPath),
                Value = WorkingPath,
                Description = "The path where the working files will be stored.",
                ValueType = EValueType.PATH,
                DefaultValue = "data/working",
                Mandatory = true,
            },
            new OptionDefinition
            {
                Name = nameof(ImportPath),
                Value = ImportPath,
                Description = "The path where the import files will be stored.",
                ValueType = EValueType.PATH,
                DefaultValue = "data/import"
            },
            new OptionDefinition
            {
                Name = nameof(ImagesPath),
                Value = ImagesPath,
                Description = "The path where uploaded cover images will be stored.",
                ValueType = EValueType.PATH,
                DefaultValue = "data/images"
            },
            new OptionDefinition
            {
                Name = nameof(DownloadsPath),
                Value = DownloadsPath,
                Description = "The path where files downloaded by qBittorrent will land.",
                ValueType = EValueType.PATH,
                DefaultValue = "data/downloads"
            },
            new OptionDefinition
            {
                Name = nameof(PdfDpi),
                Value = PdfDpi.ToString(),
                Description = "DPI used to rasterize PDF pages into images. Higher values produce larger, heavier files without adding real detail beyond the source scan resolution.",
                ValueType = EValueType.INT,
                DefaultValue = "150"
            },
            new OptionDefinition
            {
                Name = nameof(PdfImageFormat),
                Value = PdfImageFormat.ToString(),
                Description = "Image format used for pages rendered from PDF sources. JPEG is recommended for scanned/photographic content — much smaller files with no perceptible quality loss.",
                ValueType = EValueType.SELECT,
                DefaultValue = nameof(PdfPageFormat.Jpeg),
                AllowedValues = Enum.GetNames(typeof(PdfPageFormat)).ToList()
            },
            new OptionDefinition
            {
                Name = nameof(PdfJpegQuality),
                Value = PdfJpegQuality.ToString(),
                Description = "JPEG quality (1-100) used when encoding PDF pages. Ignored when PNG is used.",
                ValueType = EValueType.INT,
                DefaultValue = "90",
                RegexValidator = @"^(100|[1-9]?[0-9])$"
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

        if (PdfDpi <= 0)
        {
            errors.Add("PDF DPI must be greater than 0.");
        }

        if (PdfJpegQuality < 1 || PdfJpegQuality > 100)
        {
            errors.Add("PDF JPEG quality must be between 1 and 100.");
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
                else if (option.Name == nameof(PdfDpi))
                {
                    PdfDpi = option.GetInt();
                }
                else if (option.Name == nameof(PdfImageFormat))
                {
                    if (Enum.TryParse<PdfPageFormat>(option.Value, true, out var format))
                        PdfImageFormat = format;
                }
                else if (option.Name == nameof(PdfJpegQuality))
                {
                    PdfJpegQuality = option.GetInt();
                }
            }
            errors.AddRange(optionErrors);
        }

        return IsValid(out errors);
    }
}
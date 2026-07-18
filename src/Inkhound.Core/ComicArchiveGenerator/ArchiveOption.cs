using System;
using System.IO.Compression;
using System.Linq;
using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.ComicArchiveGenerator;

public enum ImageConversionFormat { Jpeg, WebP, Png }

public class ArchiveOption : IOptionList
{
    public string WorkingPath { get; set; } = "data/working";
    public string ImportPath { get; set; } = "data/import";
    public string ImagesPath { get; set; } = "data/images";
    public string DownloadsPath { get; set; } = "data/downloads";

    public ImageConversionFormat ImageFormatConversion { get; set; } = ImageConversionFormat.WebP;
    public int ImageQuality { get; set; } = 85;
    public int MaxImageHeightPx { get; set; } = 4000;
    public CompressionLevel ZipCompressionLevel { get; set; } = CompressionLevel.NoCompression;

    /// <summary>Unix chmod mode (e.g. 666) applied to every written CBZ/ComicInfo.xml file, written the traditional way (owner/group/other digits). 0 disables chmod-ing entirely.</summary>
    public int FileChmodMode { get; set; } = 666;

    /// <summary>Unix chmod mode (e.g. 777) applied to every volume directory created under a library, written the traditional way. Directories need the execute bit to be traversable, hence a different default than <see cref="FileChmodMode"/>. 0 disables chmod-ing entirely.</summary>
    public int DirectoryChmodMode { get; set; } = 777;

    public List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new OptionDefinition
            {
                Name = nameof(WorkingPath),
                Section = "Paths",
                SortOrder = 0,
                Value = WorkingPath,
                Description = "The path where the working files will be stored.",
                ValueType = EValueType.PATH,
                DefaultValue = "data/working",
                Mandatory = true,
            },
            new OptionDefinition
            {
                Name = nameof(ImportPath),
                Section = "Paths",
                SortOrder = 10,
                Value = ImportPath,
                Description = "The path where the import files will be stored.",
                ValueType = EValueType.PATH,
                DefaultValue = "data/import"
            },
            new OptionDefinition
            {
                Name = nameof(ImagesPath),
                Section = "Paths",
                SortOrder = 20,
                Value = ImagesPath,
                Description = "The path where uploaded cover images will be stored.",
                ValueType = EValueType.PATH,
                DefaultValue = "data/images"
            },
            new OptionDefinition
            {
                Name = nameof(DownloadsPath),
                Section = "Paths",
                SortOrder = 30,
                Value = DownloadsPath,
                Description = "The path where files downloaded by qBittorrent will land.",
                ValueType = EValueType.PATH,
                DefaultValue = "data/downloads"
            },
            new OptionDefinition
            {
                Name = nameof(ImageFormatConversion),
                Section = "Image Conversion",
                SortOrder = 40,
                Value = ImageFormatConversion.ToString(),
                Description = "Image format used to (re-)encode every page, for PDF, CBR and CBZ sources alike. WebP gives the best Kavita compatibility score.",
                ValueType = EValueType.SELECT,
                DefaultValue = nameof(ImageConversionFormat.WebP),
                AllowedValues = Enum.GetNames(typeof(ImageConversionFormat)).ToList()
            },
            new OptionDefinition
            {
                Name = nameof(ImageQuality),
                Section = "Image Conversion",
                SortOrder = 50,
                Value = ImageQuality.ToString(),
                Description = "Encode quality (1-100) used for JPEG and WebP pages. Ignored when the target format is PNG (lossless).",
                ValueType = EValueType.INT,
                DefaultValue = "85",
                RegexValidator = @"^(100|[1-9]?[0-9])$"
            },
            new OptionDefinition
            {
                Name = nameof(MaxImageHeightPx),
                Section = "Image Conversion",
                SortOrder = 60,
                Value = MaxImageHeightPx.ToString(),
                Description = "Maximum page height in pixels. Taller pages are downscaled to this height (never upscaled). For PDF sources, pages are rendered directly at this height.",
                ValueType = EValueType.INT,
                DefaultValue = "4000"
            },
            new OptionDefinition
            {
                Name = nameof(ZipCompressionLevel),
                Section = "Image Conversion",
                SortOrder = 70,
                Value = ZipCompressionLevel.ToString(),
                Description = "Zip compression applied to the .cbz container itself. Pages are already lossy-compressed, so NoCompression (default) avoids wasting CPU on every Kavita read.",
                ValueType = EValueType.SELECT,
                DefaultValue = nameof(CompressionLevel.NoCompression),
                AllowedValues = Enum.GetNames(typeof(CompressionLevel)).ToList()
            },
            new OptionDefinition
            {
                Name = nameof(FileChmodMode),
                Section = "Filesystem",
                SortOrder = 80,
                Value = FileChmodMode.ToString(),
                Description = "On Linux (Docker/NFS deployments), chmod every written CBZ/ComicInfo.xml file to this Unix mode (e.g. 666 = read/write for everyone) right after writing it, so it stays writable later even if a future container run uses a different user. Set to 0 to disable. No effect on Windows.",
                ValueType = EValueType.INT,
                DefaultValue = "666",
                RegexValidator = @"^[0-7]{1,3}$"
            },
            new OptionDefinition
            {
                Name = nameof(DirectoryChmodMode),
                Section = "Filesystem",
                SortOrder = 90,
                Value = DirectoryChmodMode.ToString(),
                Description = "On Linux (Docker/NFS deployments), chmod every volume directory created under a library to this Unix mode (e.g. 777 = read/write/traverse for everyone) right after creating it. Set to 0 to disable. No effect on Windows.",
                ValueType = EValueType.INT,
                DefaultValue = "777",
                RegexValidator = @"^[0-7]{1,3}$"
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

        if (ImageQuality < 1 || ImageQuality > 100)
        {
            errors.Add("Image quality must be between 1 and 100.");
        }

        if (MaxImageHeightPx <= 0)
        {
            errors.Add("Max image height must be greater than 0.");
        }

        if (FileChmodMode < 0 || FileChmodMode > 777)
        {
            errors.Add("File chmod mode must be between 0 and 777.");
        }

        if (DirectoryChmodMode < 0 || DirectoryChmodMode > 777)
        {
            errors.Add("Directory chmod mode must be between 0 and 777.");
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
                else if (option.Name == nameof(ImageFormatConversion))
                {
                    if (Enum.TryParse<ImageConversionFormat>(option.Value, true, out var format))
                        ImageFormatConversion = format;
                }
                else if (option.Name == nameof(ImageQuality))
                {
                    ImageQuality = option.GetInt();
                }
                else if (option.Name == nameof(MaxImageHeightPx))
                {
                    MaxImageHeightPx = option.GetInt();
                }
                else if (option.Name == nameof(ZipCompressionLevel))
                {
                    if (Enum.TryParse<CompressionLevel>(option.Value, true, out var level))
                        ZipCompressionLevel = level;
                }
                else if (option.Name == nameof(FileChmodMode))
                {
                    FileChmodMode = option.GetInt();
                }
                else if (option.Name == nameof(DirectoryChmodMode))
                {
                    DirectoryChmodMode = option.GetInt();
                }
            }
            errors.AddRange(optionErrors);
        }

        return IsValid(out errors);
    }
}

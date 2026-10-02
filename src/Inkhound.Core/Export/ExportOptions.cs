using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.Export;

/// <summary>
/// Options du module Export : dossier de stockage, format par défaut et qualité par format.
/// La durée de vie des fichiers n'est pas ici : elle est portée par la tâche « Clean export » du
/// planificateur (<c>SchedulerOptions.CleanExportsMaxAgeDays</c>).
/// </summary>
public class ExportOptions : IOptionList
{
    /// <summary>Dossier où sont écrits les fichiers exportés (relatif au répertoire courant du process).</summary>
    public string ExportPath { get; set; } = "data/export";

    /// <summary>Format préselectionné dans l'UI lors d'un export.</summary>
    public ExportFormat DefaultFormat { get; set; } = ExportFormat.Cbz;

    /// <summary>PDF : <see cref="ExportImageFormat.Original"/> ou <see cref="ExportImageFormat.Jpeg"/>.</summary>
    public ExportImageFormat PdfImageFormat { get; set; } = ExportImageFormat.Jpeg;

    /// <summary>PDF : qualité JPEG (1-100) quand les pages sont ré-encodées.</summary>
    public int PdfImageQuality { get; set; } = 85;

    /// <summary>PDF : hauteur maximale d'une page en pixels (les pages plus hautes sont réduites).</summary>
    public int PdfMaxImageHeightPx { get; set; } = 2000;

    /// <summary>CBZ : encodage des pages (<see cref="ExportImageFormat.Original"/> = copie du fichier source).</summary>
    public ExportImageFormat CbzImageFormat { get; set; } = ExportImageFormat.Original;

    /// <summary>CBZ : qualité (1-100) pour JPEG et WebP ; ignorée pour PNG.</summary>
    public int CbzImageQuality { get; set; } = 85;

    /// <summary>CBZ : hauteur maximale d'une page en pixels (les pages plus hautes sont réduites).</summary>
    public int CbzMaxImageHeightPx { get; set; } = 4000;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ExportPath))
            errors.Add($"{nameof(ExportPath)} is required.");
        if (PdfImageFormat is not (ExportImageFormat.Original or ExportImageFormat.Jpeg))
            errors.Add($"{nameof(PdfImageFormat)} must be Original or Jpeg.");
        if (PdfImageQuality is < 1 or > 100)
            errors.Add($"{nameof(PdfImageQuality)} must be between 1 and 100.");
        if (CbzImageQuality is < 1 or > 100)
            errors.Add($"{nameof(CbzImageQuality)} must be between 1 and 100.");
        if (PdfMaxImageHeightPx < 200)
            errors.Add($"{nameof(PdfMaxImageHeightPx)} must be at least 200.");
        if (CbzMaxImageHeightPx < 200)
            errors.Add($"{nameof(CbzMaxImageHeightPx)} must be at least 200.");
        return errors.Count == 0;
    }

    public List<OptionDefinition> GetOptions()
    {
        const string quality = @"^(100|[1-9]?[0-9])$";
        return new List<OptionDefinition>
        {
            new() { Name = nameof(ExportPath), Section = "General", SortOrder = 0, Value = ExportPath, ValueType = EValueType.PATH, DefaultValue = "data/export", Description = "Folder where exported files are written. Files older than the \"Clean export\" scheduler task's maximum age are deleted.", Mandatory = true },
            new() { Name = nameof(DefaultFormat), Section = "General", SortOrder = 10, Value = DefaultFormat.ToString(), ValueType = EValueType.SELECT, DefaultValue = nameof(ExportFormat.Cbz), AllowedValues = Enum.GetNames(typeof(ExportFormat)).ToList(), Description = "Format preselected when exporting an issue or a volume (a volume is always exported as a ZIP of issues in this format).", Mandatory = true },

            new() { Name = nameof(PdfImageFormat), Section = "PDF", SortOrder = 20, Value = PdfImageFormat.ToString(), ValueType = EValueType.SELECT, DefaultValue = nameof(ExportImageFormat.Jpeg), AllowedValues = [nameof(ExportImageFormat.Original), nameof(ExportImageFormat.Jpeg)], Description = "Original embeds the pages untouched (JPEG pages are kept as-is; WebP/PNG pages make much heavier PDFs). Jpeg re-encodes every page with the quality and height below.", Mandatory = true },
            new() { Name = nameof(PdfImageQuality), Section = "PDF", SortOrder = 30, Value = PdfImageQuality.ToString(), ValueType = EValueType.INT, DefaultValue = "85", RegexValidator = quality, Description = "JPEG quality (1-100) used when pages are re-encoded.", Mandatory = false },
            new() { Name = nameof(PdfMaxImageHeightPx), Section = "PDF", SortOrder = 40, Value = PdfMaxImageHeightPx.ToString(), ValueType = EValueType.INT, DefaultValue = "2000", Description = "Maximum page height in pixels when pages are re-encoded (never upscaled).", Mandatory = false },

            new() { Name = nameof(CbzImageFormat), Section = "CBZ", SortOrder = 50, Value = CbzImageFormat.ToString(), ValueType = EValueType.SELECT, DefaultValue = nameof(ExportImageFormat.Original), AllowedValues = Enum.GetNames(typeof(ExportImageFormat)).ToList(), Description = "Original copies the library CBZ untouched (instant). Other values re-encode every page (useful to get a lighter file for a reader or a device).", Mandatory = true },
            new() { Name = nameof(CbzImageQuality), Section = "CBZ", SortOrder = 60, Value = CbzImageQuality.ToString(), ValueType = EValueType.INT, DefaultValue = "85", RegexValidator = quality, Description = "Quality (1-100) for JPEG and WebP pages. Ignored for Original and PNG.", Mandatory = false },
            new() { Name = nameof(CbzMaxImageHeightPx), Section = "CBZ", SortOrder = 70, Value = CbzMaxImageHeightPx.ToString(), ValueType = EValueType.INT, DefaultValue = "4000", Description = "Maximum page height in pixels when pages are re-encoded (never upscaled).", Mandatory = false }
        };
    }

    public bool LoadOptions(List<OptionDefinition> options, out List<string> errors)
    {
        foreach (var option in options)
        {
            switch (option.Name)
            {
                case nameof(ExportPath): ExportPath = option.Value; break;
                case nameof(DefaultFormat): if (Enum.TryParse<ExportFormat>(option.Value, true, out var f)) DefaultFormat = f; break;
                case nameof(PdfImageFormat): if (Enum.TryParse<ExportImageFormat>(option.Value, true, out var pf)) PdfImageFormat = pf; break;
                case nameof(PdfImageQuality): PdfImageQuality = option.GetInt(); break;
                case nameof(PdfMaxImageHeightPx): PdfMaxImageHeightPx = option.GetInt(); break;
                case nameof(CbzImageFormat): if (Enum.TryParse<ExportImageFormat>(option.Value, true, out var cf)) CbzImageFormat = cf; break;
                case nameof(CbzImageQuality): CbzImageQuality = option.GetInt(); break;
                case nameof(CbzMaxImageHeightPx): CbzMaxImageHeightPx = option.GetInt(); break;
            }
        }
        return IsValid(out errors);
    }
}

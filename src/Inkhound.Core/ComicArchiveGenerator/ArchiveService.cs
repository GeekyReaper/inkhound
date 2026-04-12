using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Foundation.Core;
using Foundation.Core.Model;
using Inkhound.Core.Models;
using PDFtoImage;
using SkiaSharp;

namespace Inkhound.Core.ComicArchiveGenerator;

public class ArchiveService : BaseService<ArchiveOption>
{
    public ArchiveService()
    {
    }

    #region Override BaseService

    public override string GetServiceName() => "ArchiveService";
    public override async Task<bool> LoadOptions(List<OptionDefinition> optionList)
    {


        return await base.LoadOptions(optionList);
    }

    protected override async Task<EState> CheckInternalState()
    {
        // Check if working path exists
        if (!Directory.Exists(Options.WorkingPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(Options.WorkingPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                    File.Create(Path.Combine(Options.WorkingPath, "testworking.txt")).Dispose(); // Test write access
                    File.Delete(Path.Combine(Options.WorkingPath, "testworking.txt"));
                }
                else
                {
                    Console.WriteLine("Working path directory is null or empty.");
                    return EState.ERROR;
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating working directory: {ex.Message}");
                return EState.ERROR;
            }

        }

        // Check if import path exists
        if (!Directory.Exists(Options.ImportPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(Options.ImportPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                    File.Create(Path.Combine(Options.ImportPath, "testimport.txt")).Dispose(); // Test write access
                    File.Delete(Path.Combine(Options.ImportPath, "testimport.txt"));
                }
                else
                {
                    Console.WriteLine("Import path directory is null or empty.");
                    return EState.ERROR;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating import directory: {ex.Message}");
                return EState.ERROR;
            }
        }

        return EState.OK;
    }

    #endregion


    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("osx")]
    public async Task<List<FileInfo>?> ConvertPdfToImage(string sourcePath, string destinationPath, ProgressionCallback? progression = null)
    {
        var fullSourcePath = Path.Combine(Options.ImportPath, sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            SendTrace($"PDF source not found: {fullSourcePath}", new TraceDefinition() { Level = ETraceLevel.ERROR });
            return null;
        }

        if (new FileInfo(fullSourcePath).Length == 0)
        {
            SendTrace($"PDF source file is empty: {fullSourcePath}", new TraceDefinition() { Level = ETraceLevel.ERROR });
            return null;
        }

        var fullDestPath = Path.Combine(Options.WorkingPath, destinationPath);
        Directory.CreateDirectory(fullDestPath);

        var imagePaths = new List<FileInfo>();

        var totalPages = Conversion.GetPageCount(File.OpenRead(fullSourcePath));
        var internalprogress = new Progression { Total = totalPages, Completed = 0, Error = 0 };
        // Initialize number of items
        progression?.UpdateTotal(internalprogress.Total);


        await using var stream = File.OpenRead(fullSourcePath);
        int index = 0;
        await foreach (var page in Conversion.ToImagesAsync(stream))
        {
            try
            {
                var fileName = $"page_{++index:D3}.png";
                var filePath = Path.Combine(fullDestPath, fileName);

                await using var output = File.OpenWrite(filePath);
                page.Encode(output, SKEncodedImageFormat.Png, 100);
                imagePaths.Add(new FileInfo(filePath));
                internalprogress.Increment();
                SendTrace($"Successfully converted page {index}/{totalPages}");
            }
            catch (Exception ex)
            {
                SendTrace($"Error converting page {index}", ex);
                internalprogress.Increment(success: false);
            }
            progression?.Callback(internalprogress);

        }

        return imagePaths;
    }

    public async Task<FileInfo> CreateComicInfo(Volume volume, Issue issue, string destinationPath)
    {
        var fullDestPath = Path.Combine(Options.WorkingPath, destinationPath);
        Directory.CreateDirectory(fullDestPath);

        var filePath = Path.Combine(fullDestPath, "ComicInfo.xml");

        var writers = volume.Authors
            .Where(a => a.Role.Equals("Writer", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Name)
            .ToList();

        var artists = volume.Authors
            .Where(a => a.Role.Equals("Artist", StringComparison.OrdinalIgnoreCase)
                     || a.Role.Equals("Penciller", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Name)
            .ToList();

        var elements = new List<object?>
        {
            new XElement("Title", issue.Title ?? volume.Title),
            new XElement("Series", volume.Title),
            new XElement("Number", issue.IssueNumber),
            issue.Year.HasValue || volume.Year.HasValue
                ? new XElement("Year", issue.Year ?? volume.Year)
                : null,
            issue.PublishedAt.HasValue
                ? new XElement("Month", issue.PublishedAt.Value.Month)
                : null,
            !string.IsNullOrEmpty(volume.Publisher)
                ? new XElement("Publisher", volume.Publisher)
                : null,
            !string.IsNullOrEmpty(issue.Description ?? volume.Description)
                ? new XElement("Summary", issue.Description ?? volume.Description)
                : null,
            volume.Genres.Count > 0
                ? new XElement("Genre", string.Join(", ", volume.Genres))
                : null,
            writers.Count > 0
                ? new XElement("Writer", string.Join(", ", writers))
                : null,
            artists.Count > 0
                ? new XElement("Penciller", string.Join(", ", artists))
                : null,
        };

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("ComicInfo",
                new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
                elements.Where(e => e != null).Cast<object>().ToArray()
            )
        );

        await using var stream = File.Create(filePath);
        await doc.SaveAsync(stream, SaveOptions.None, CancellationToken.None);

        return new FileInfo(filePath);
    }

    public async Task<FileInfo> CreateCbzFile(Volume volume, Issue issue, FileInfo comicInfo, List<FileInfo> filepages)
    {
        var cbzName = BuildCbzFilename(volume, issue);
        var cbzPath = Path.Combine(comicInfo.DirectoryName!, cbzName);

        await using var zipStream = File.Create(cbzPath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

        archive.CreateEntryFromFile(comicInfo.FullName, comicInfo.Name, CompressionLevel.NoCompression);

        foreach (var page in filepages)
            archive.CreateEntryFromFile(page.FullName, page.Name, CompressionLevel.NoCompression);

        return new FileInfo(cbzPath);
    }

    private static string BuildCbzFilename(Volume volume, Issue issue)
    {
        var title = NormalizeTitle(volume.Title);
        var volumeYear = volume.Year.HasValue ? $" ({volume.Year})" : string.Empty;
        var number = issue.IssueNumber.ToString("D3");
        var issueYear = issue.PublishedAt.HasValue ? $" ({issue.PublishedAt.Value.Year})" : string.Empty;

        var name = string.IsNullOrWhiteSpace(issue.Title)
            ? $"{title}{volumeYear} - {number}{issueYear}"
            : $"{title}{volumeYear} - {number} - {NormalizeTitle(issue.Title)}{issueYear}";

        return name + ".cbz";
    }

    private static string NormalizeTitle(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : ' ');
        }
        return sb.ToString().Trim();
    }
}

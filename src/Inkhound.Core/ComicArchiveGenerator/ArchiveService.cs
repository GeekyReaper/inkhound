using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Foundation.Core;
using Foundation.Core.Model;
using Inkhound.Core.Models;
using PDFtoImage;
using SharpCompress.Archives.Rar;
using SkiaSharp;

namespace Inkhound.Core.ComicArchiveGenerator;

public enum EArchiveType { PDF, CBR, CBZ, UNKNOW }
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

    public string WorkingPath => Options.WorkingPath;

    #region Conversion Methods
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("osx")]
    public async Task<List<FileInfo>?> ConvertPdfToImages(FileInfo source, string destinationPath, ProgressionCallback? progression = null)
    {
        if (source.Length == 0)
        {
            SendTrace($"PDF source file is empty: {source.FullName}", new TraceDefinition() { Level = ETraceLevel.ERROR });
            return null;
        }

        var fullDestPath = Path.Combine(Options.WorkingPath, destinationPath);
        Directory.CreateDirectory(fullDestPath);

        var imagePaths = new List<FileInfo>();

        var totalPages = Conversion.GetPageCount(File.OpenRead(source.FullName));
        var internalprogress = new Progression { Total = totalPages, Completed = 0, Error = 0 };
        // Initialize number of items
        progression?.UpdateTotal(internalprogress.Total);


        await using var stream = File.OpenRead(source.FullName);
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

    public async Task<List<FileInfo>?> ConvertCbrToImages(FileInfo source, string destinationPath, ProgressionCallback? progression = null)
    {
        if (source.Length == 0)
        {
            SendTrace($"CBR source file is empty: {source.FullName}", new TraceDefinition() { Level = ETraceLevel.ERROR });
            return null;
        }

        var fullDestPath = Path.Combine(Options.WorkingPath, destinationPath);
        Directory.CreateDirectory(fullDestPath);

        var imagePaths = new List<FileInfo>();
        string[] imageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

        using var archive = RarArchive.OpenArchive(source.FullName);
        var entries = archive.Entries
            .Where(e => !e.IsDirectory && imageExtensions.Contains(Path.GetExtension(e.Key ?? string.Empty).ToLowerInvariant()))
            .OrderBy(e => e.Key)
            .ToList();

        progression?.UpdateTotal(entries.Count);
        var internalProgress = new Progression { Total = entries.Count, Completed = 0, Error = 0 };

        int index = 0;
        foreach (var entry in entries)
        {
            try
            {
                var ext = Path.GetExtension(entry.Key!).ToLowerInvariant();
                var fileName = $"page_{++index:D3}{ext}";
                var filePath = Path.Combine(fullDestPath, fileName);

                await using var output = File.Create(filePath);
                await using var entryStream = entry.OpenEntryStream();
                await entryStream.CopyToAsync(output);

                imagePaths.Add(new FileInfo(filePath));
                internalProgress.Increment();
                SendTrace($"Successfully extracted page {index}/{entries.Count}");
            }
            catch (Exception ex)
            {
                SendTrace($"Error extracting page {index}", ex);
                internalProgress.Increment(success: false);
            }
            progression?.Callback(internalProgress);
        }

        return imagePaths;
    }

    public async Task<List<FileInfo>?> ConvertCbzToImages(FileInfo source, string destinationPath, ProgressionCallback? progression = null)
    {
        if (source.Length == 0)
        {
            SendTrace($"CBZ source file is empty: {source.FullName}", new TraceDefinition() { Level = ETraceLevel.ERROR });
            return null;
        }

        var fullDestPath = Path.Combine(Options.WorkingPath, destinationPath);
        Directory.CreateDirectory(fullDestPath);

        var imagePaths = new List<FileInfo>();
        string[] imageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

        using var zip = ZipFile.OpenRead(source.FullName);
        var entries = zip.Entries
            .Where(e => imageExtensions.Contains(Path.GetExtension(e.Name).ToLowerInvariant()))
            .OrderBy(e => e.FullName)
            .ToList();

        progression?.UpdateTotal(entries.Count);
        var internalProgress = new Progression { Total = entries.Count, Completed = 0, Error = 0 };

        int index = 0;
        foreach (var entry in entries)
        {
            try
            {
                var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                var fileName = $"page_{++index:D3}{ext}";
                var filePath = Path.Combine(fullDestPath, fileName);

                await using var output = File.Create(filePath);
                await using var entryStream = entry.Open();
                await entryStream.CopyToAsync(output);

                imagePaths.Add(new FileInfo(filePath));
                internalProgress.Increment();
                SendTrace($"Successfully extracted page {index}/{entries.Count}");
            }
            catch (Exception ex)
            {
                SendTrace($"Error extracting page {index}", ex);
                internalProgress.Increment(success: false);
            }
            progression?.Callback(internalProgress);
        }

        return imagePaths;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("osx")]
    public async Task<List<FileInfo>?> ConvertToImages(FileInfo source, string destinationPath, ProgressionCallback? progression = null)
    {
        var archiveType = await GetArchiveType(source.FullName);
        return archiveType switch
        {
            EArchiveType.PDF => await ConvertPdfToImages(source, destinationPath, progression),
            EArchiveType.CBR => await ConvertCbrToImages(source, destinationPath, progression),
            EArchiveType.CBZ => await ConvertCbzToImages(source, destinationPath, progression),
            _ => null
        };
    }
    public async Task<FileInfo> CreateComicInfo(Volume volume, Issue issue, string destinationPath, ProgressionCallback? progression = null)
    {

        progression?.UpdateTotal(1);

        var fullDestPath = Path.Combine(Options.WorkingPath, destinationPath);
        Directory.CreateDirectory(fullDestPath);

        var filePath = Path.Combine(fullDestPath, "ComicInfo.xml");

        var authors = issue.Authors.Count > 0 ? issue.Authors : volume.Authors;

        var writers = authors
            .Where(a => a.Role.Equals("Writer", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Name)
            .ToList();

        var pencillers = authors
            .Where(a => a.Role.Equals("Penciler", StringComparison.OrdinalIgnoreCase)
                     || a.Role.Equals("Penciller", StringComparison.OrdinalIgnoreCase)
                     )
            .Select(a => a.Name)
            .ToList();

        var inkers = authors
            .Where(a => a.Role.Equals("Inker", StringComparison.OrdinalIgnoreCase)
                     )
            .Select(a => a.Name)
            .ToList();

        var editors = authors
            .Where(a => a.Role.Equals("Editor", StringComparison.OrdinalIgnoreCase)
                     )
            .Select(a => a.Name)
            .ToList();

        var artists = authors
            .Where(a => a.Role.Equals("Artist", StringComparison.OrdinalIgnoreCase)
                     )
            .Select(a => a.Name)
            .ToList();

        var colorists = authors
            .Where(a => a.Role.Equals("Colorist", StringComparison.OrdinalIgnoreCase)
                     )
            .Select(a => a.Name)
            .ToList();
        var letterers = authors
            .Where(a => a.Role.Equals("Letterer", StringComparison.OrdinalIgnoreCase)
                     )
            .Select(a => a.Name)
            .ToList();
        var translators = authors
            .Where(a => a.Role.Equals("Translator", StringComparison.OrdinalIgnoreCase)
                     )
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
            pencillers.Count > 0
                ? new XElement("Penciller", string.Join(", ", pencillers))
                : null,
            artists.Count > 0
                ? new XElement("Artist", string.Join(", ", artists))
                : null,
            colorists.Count > 0
                ? new XElement("Colorist", string.Join(", ", colorists))
                : null,
            letterers.Count > 0
                ? new XElement("Letterer", string.Join(", ", letterers))
                : null,
            translators.Count > 0
                ? new XElement("Translator", string.Join(", ", translators))
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
        var result = new FileInfo(filePath);
        SendTrace($"ComicInfo.xml created to {filePath} (size {result.Length / 1024.0:F1} KB)");
        progression?.Callback(new Progression() { Completed = 1, Error = 0 });

        return result;
    }

    public async Task<FileInfo> CreateCbzFile(string workingPath, Volume volume, Issue issue, FileInfo comicInfo, List<FileInfo> filepages, ProgressionCallback? progression = null)
    {
        progression?.UpdateTotal(filepages.Count + 1);
        var progress = new Progression() { Total = filepages.Count + 1 };

        var filecbzname = GetPath(issue, volume);
        var cbzPath = Path.Combine(workingPath, filecbzname);

        await using var zipStream = File.Create(cbzPath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

        archive.CreateEntryFromFile(comicInfo.FullName, comicInfo.Name, CompressionLevel.NoCompression);
        progress.Increment(true);
        SendTrace($"Successfully zip page {progress.Completed}/{progress.Total}");
        progression?.Callback(progress);

        foreach (var page in filepages)
        {
            archive.CreateEntryFromFile(page.FullName, page.Name, CompressionLevel.NoCompression);
            progress.Increment(true);
            SendTrace($"Successfully zip page {progress.Completed}/{progress.Total}");
            progression?.Callback(progress);
        }
        var file = new FileInfo(cbzPath);
        SendTrace($"CBZ created to {file.Name} (size {file.Length / 1024.0:F1} KB)");
        return new FileInfo(cbzPath);
    }

    #endregion

    #region Static methods to map volume/issue to file path

    public static string GetPath(Issue issue, Volume volume, Library? library = null)
    {
        var title = NormalizeTitle(volume.Title);
        var number = issue.IssueNumber.ToString("D3");
        var issueYear = issue.PublishedAt.HasValue ? $" ({issue.PublishedAt.Value.Year})" : string.Empty;

        var name = string.IsNullOrWhiteSpace(issue.Title)
            ? $"{title} - {number}{issueYear}"
            : $"{title} - {number} - {NormalizeTitle(issue.Title)}{issueYear}";

        name = name + ".cbz";

        if (library != null)
        {
            name = Path.Combine(GetPath(volume, library), name);
        }

        return name;


    }

    public static string GetPath(Volume volume, Library? library = null)
    {
        var title = NormalizeTitle(volume.Title);
        var volumeYear = volume.Year.HasValue ? $" ({volume.Year})" : string.Empty;

        var path = $"{title}{volumeYear}";

        if (library != null)
        {
            path = Path.Combine(library.Path, path);
        }

        return path;
    }

    public static Task<List<DirectoryInfo>> GetDirectoriesAsync(string path) =>
        Task.Run(() =>
        {
            var dir = new DirectoryInfo(path);
            return dir.Exists ? [.. dir.GetDirectories()] : (List<DirectoryInfo>)[];
        });

    public static Task<List<FileInfo>> GetFilesAsync(string path, string filter = "*") =>
        Task.Run(() =>
        {
            var dir = new DirectoryInfo(path);
            return dir.Exists ? [.. dir.GetFiles(filter)] : (List<FileInfo>)[];
        });

    public async Task<EArchiveType> GetArchiveType(string filepath)
    {
        var header = new byte[4];
        await using var fs = File.OpenRead(filepath);
        await fs.ReadExactlyAsync(header);

        // PDF: %PDF
        if (header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
            return EArchiveType.PDF;

        // CBZ (ZIP): PK\x03\x04
        if (header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
            return EArchiveType.CBZ;

        // CBR (RAR): Rar!
        if (header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72 && header[3] == 0x21)
            return EArchiveType.CBR;

        SendTrace($"GetArchiveType Unrecognized archive format: {filepath}", ETraceLevel.DEBUG);
        return EArchiveType.UNKNOW;
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

    public static void AttachFileToIssue(FileInfo cbzFile, Issue issue, Volume volume, Library library)
    {
        var issuenumberfilename = GetPath(issue, volume);

        if (cbzFile.Name != issuenumberfilename)
        {
            var newPath = Path.Combine(Path.Combine(GetPath(volume, library), issuenumberfilename));
            cbzFile.MoveTo(newPath);
        }

        issue.CbzFilename = cbzFile.Name;
        issue.FileSizeBytes = (int)cbzFile.Length;
        issue.DownloadedAt = DateTime.UtcNow;
        issue.Status = IssueStatus.DOWNLOADED;
    }
    public DirectoryInfo GenerateTempDirectory()
    {
        var tempAbsolute = Path.Combine(WorkingPath, Guid.NewGuid().ToString("N"));
        return Directory.CreateDirectory(tempAbsolute);
    }

    public DirectoryInfo CreateVolumeDirectory(Volume volume, Library library)
    {
        var path = GetPath(volume, library);
        if (Directory.Exists(path))
        {
            SendTrace($"Directory already exists for volume '{volume.Title}' at {path}", new TraceDefinition() { Level = ETraceLevel.WARNING });
            return new DirectoryInfo(path);
        }
        return Directory.CreateDirectory(path);
    }

    #endregion


}

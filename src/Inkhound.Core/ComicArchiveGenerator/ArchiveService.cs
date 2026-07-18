using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Foundation.Core;
using Foundation.Core.Model;
using Inkhound.Core.CbzQuality.Analysis;
using Inkhound.Core.CbzQuality.Models;
using Inkhound.Core.CbzQuality.Scoring;
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

    public string ImagesPath => Options.ImagesPath;
    public string DownloadsPath => Options.DownloadsPath;
    public string ImportPath => Options.ImportPath;

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

        // Check if downloads path exists (dossier de réception qBittorrent)
        if (!Directory.Exists(Options.DownloadsPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(Options.DownloadsPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                    File.Create(Path.Combine(Options.DownloadsPath, "testdownloads.txt")).Dispose(); // Test write access
                    File.Delete(Path.Combine(Options.DownloadsPath, "testdownloads.txt"));
                }
                else
                {
                    Console.WriteLine("Downloads path directory is null or empty.");
                    return EState.ERROR;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating downloads directory: {ex.Message}");
                return EState.ERROR;
            }
        }

        return EState.OK;
    }

    #endregion

    public string WorkingPath => Options.WorkingPath;

    #region Conversion Methods

    private (SKEncodedImageFormat Format, string Extension) GetTargetImageFormat() => Options.ImageFormatConversion switch
    {
        ImageConversionFormat.WebP => (SKEncodedImageFormat.Webp, ".webp"),
        ImageConversionFormat.Png => (SKEncodedImageFormat.Png, ".png"),
        _ => (SKEncodedImageFormat.Jpeg, ".jpg")
    };

    /// <summary>
    /// Decodes, downscales (if taller than <see cref="ArchiveOption.MaxImageHeightPx"/>) and re-encodes
    /// an image entry extracted from a CBR/CBZ to the configured target format. Falls back to copying
    /// the raw bytes as-is under their original extension when the entry can't be decoded by SkiaSharp
    /// (corrupted/unsupported source image) — same behavior a plain extraction would have had.
    /// </summary>
    private async Task<FileInfo> ConvertImageEntryAsync(Stream sourceStream, string originalExtension, string fullDestPath, int pageIndex)
    {
        using var ms = new MemoryStream();
        await sourceStream.CopyToAsync(ms);
        ms.Position = 0;

        using var bitmap = SKBitmap.Decode(ms);
        if (bitmap is null)
        {
            var rawPath = Path.Combine(fullDestPath, $"page_{pageIndex:D3}{originalExtension}");
            ms.Position = 0;
            await using var rawOut = File.Create(rawPath);
            await ms.CopyToAsync(rawOut);
            SendTrace($"Page {pageIndex} could not be decoded — copied as-is ({originalExtension})", new TraceDefinition() { Level = ETraceLevel.WARNING });
            return new FileInfo(rawPath);
        }

        var toEncode = bitmap;
        if (bitmap.Height > Options.MaxImageHeightPx)
        {
            var newWidth = (int)Math.Round(bitmap.Width * (Options.MaxImageHeightPx / (double)bitmap.Height));
            toEncode = bitmap.Resize(new SKImageInfo(newWidth, Options.MaxImageHeightPx), SKFilterQuality.High);
        }

        var (format, ext) = GetTargetImageFormat();
        var filePath = Path.Combine(fullDestPath, $"page_{pageIndex:D3}{ext}");
        await using var output = File.Create(filePath);
        toEncode.Encode(output, format, Options.ImageQuality);
        if (!ReferenceEquals(toEncode, bitmap)) toEncode.Dispose();
        return new FileInfo(filePath);
    }

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
        var workingtempdir = Directory.CreateDirectory(fullDestPath);

        // Copy sourcefile to working directory to avoid locking issues
        var sourcefilecopyPath = Path.Combine(workingtempdir.FullName, source.Name);
        var workingSource = RunTimed(
            $"Copying source file ({source.Length / 1024.0 / 1024.0:F1} MB) to working directory: {sourcefilecopyPath}",
            () => source.CopyTo(sourcefilecopyPath, overwrite: true));

        var imagePaths = new List<FileInfo>();

        var totalPages = Conversion.GetPageCount(File.OpenRead(source.FullName));
        var internalprogress = new Progression { Total = totalPages, Completed = 0, Error = 0 };
        // Initialize number of items
        progression?.UpdateTotal(internalprogress.Total);


        // Render directly at the target page height (PDFium rasterizes precisely at this size, no
        // downscale-after-the-fact needed) and encode directly to the configured target format —
        // never through an intermediate JPEG hop.
        var renderOptions = new RenderOptions(Height: Options.MaxImageHeightPx, WithAspectRatio: true);
        var (targetFormat, targetExt) = GetTargetImageFormat();

        await using var stream = File.OpenRead(source.FullName);
        int index = 0;
        await foreach (var page in Conversion.ToImagesAsync(stream, options: renderOptions))
        {
            try
            {
                ++index;
                var fileName = $"page_{index:D3}{targetExt}";
                var filePath = Path.Combine(fullDestPath, fileName);

                await using var output = File.OpenWrite(filePath);
                page.Encode(output, targetFormat, Options.ImageQuality);
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
        SendTrace($"Deletion of working source file: {workingSource.FullName}");
        workingSource.Delete();

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
                ++index;
                var ext = Path.GetExtension(entry.Key!).ToLowerInvariant();
                await using var entryStream = entry.OpenEntryStream();
                var converted = await ConvertImageEntryAsync(entryStream, ext, fullDestPath, index);

                imagePaths.Add(converted);
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
                ++index;
                var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                await using var entryStream = entry.Open();
                var converted = await ConvertImageEntryAsync(entryStream, ext, fullDestPath, index);

                imagePaths.Add(converted);
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
    private static XDocument BuildComicInfoDocument(Volume volume, Issue issue)
    {
        var authors = issue.Authors.Count > 0 ? issue.Authors : volume.Authors;

        var writers    = authors.Where(a => a.Role.Equals("Writer",     StringComparison.OrdinalIgnoreCase)).Select(a => a.Name).ToList();
        var pencillers = authors.Where(a => a.Role.Equals("Penciler",   StringComparison.OrdinalIgnoreCase)
                                         || a.Role.Equals("Penciller",  StringComparison.OrdinalIgnoreCase)).Select(a => a.Name).ToList();
        var artists    = authors.Where(a => a.Role.Equals("Artist",     StringComparison.OrdinalIgnoreCase)).Select(a => a.Name).ToList();
        var colorists  = authors.Where(a => a.Role.Equals("Colorist",   StringComparison.OrdinalIgnoreCase)).Select(a => a.Name).ToList();
        var letterers  = authors.Where(a => a.Role.Equals("Letterer",   StringComparison.OrdinalIgnoreCase)).Select(a => a.Name).ToList();
        var translators= authors.Where(a => a.Role.Equals("Translator", StringComparison.OrdinalIgnoreCase)).Select(a => a.Name).ToList();

        var elements = new List<object?>
        {
            new XElement("Title", issue.Title ?? volume.Title),
            new XElement("Series", volume.Title),
            new XElement("Number", issue.IssueNumber),
            issue.Year.HasValue || volume.Year.HasValue
                ? new XElement("Year", issue.Year ?? volume.Year) : null,
            issue.PublishedAt.HasValue
                ? new XElement("Month", issue.PublishedAt.Value.Month) : null,
            !string.IsNullOrEmpty(volume.Publisher)
                ? new XElement("Publisher", volume.Publisher) : null,
            !string.IsNullOrEmpty(issue.Description ?? volume.Description)
                ? new XElement("Summary", issue.Description ?? volume.Description) : null,
            volume.Genres.Count > 0
                ? new XElement("Genre", string.Join(", ", volume.Genres)) : null,
            volume.AgeRating != AgeRating.Unknown
                ? new XElement("AgeRating", volume.AgeRating.ToKavitaString()) : null,
            writers.Count > 0
                ? new XElement("Writer", string.Join(", ", writers)) : null,
            pencillers.Count > 0
                ? new XElement("Penciller", string.Join(", ", pencillers)) : null,
            artists.Count > 0
                ? new XElement("Artist", string.Join(", ", artists)) : null,
            colorists.Count > 0
                ? new XElement("Colorist", string.Join(", ", colorists)) : null,
            letterers.Count > 0
                ? new XElement("Letterer", string.Join(", ", letterers)) : null,
            translators.Count > 0
                ? new XElement("Translator", string.Join(", ", translators)) : null,
        };

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("ComicInfo",
                new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
                elements.Where(e => e != null).Cast<object>().ToArray()
            )
        );
    }

    public async Task<FileInfo> CreateComicInfo(Volume volume, Issue issue, string destinationPath, ProgressionCallback? progression = null)
    {
        progression?.UpdateTotal(1);

        var fullDestPath = Path.Combine(Options.WorkingPath, destinationPath);
        Directory.CreateDirectory(fullDestPath);

        var filePath = Path.Combine(fullDestPath, "ComicInfo.xml");
        var doc = BuildComicInfoDocument(volume, issue);

        await using var stream = File.Create(filePath);
        await doc.SaveAsync(stream, SaveOptions.None, CancellationToken.None);

        var result = new FileInfo(filePath);
        SendTrace($"ComicInfo.xml created to {filePath} (size {result.Length / 1024.0:F1} KB)");
        progression?.Callback(new Progression() { Completed = 1, Error = 0 });

        return result;
    }

    public async Task InjectComicInfoIntoCbzAsync(Volume volume, Issue issue, string cbzPath)
    {
        if (!File.Exists(cbzPath))
        {
            SendTrace($"InjectComicInfoIntoCbz: CBZ not found at {cbzPath}", ETraceLevel.WARNING);
            return;
        }

        var doc = BuildComicInfoDocument(volume, issue);

        using var zip = ZipFile.Open(cbzPath, ZipArchiveMode.Update);
        var existing = zip.GetEntry("ComicInfo.xml");
        existing?.Delete();

        var entry = zip.CreateEntry("ComicInfo.xml", CompressionLevel.NoCompression);
        await using var entryStream = entry.Open();
        await doc.SaveAsync(entryStream, SaveOptions.None, CancellationToken.None);
        entryStream.Close();

        SendTrace($"ComicInfo.xml injected into {Path.GetFileName(cbzPath)}");
        EnsurePermissiveFileMode(cbzPath);
    }

    /// <summary>
    /// On Linux/NFS deployments (e.g. a Docker container mounting a NAS share), a CBZ written by one
    /// container UID can end up unreadable/unwritable by a later run under a different UID (image
    /// update, different compose config, ...) because of the default umask (typically 644 — writable
    /// only by its owner). Chmod-ing every written file to <see cref="ArchiveOption.FileChmodMode"/>
    /// right after writing it keeps it accessible going forward. No-op on Windows (NTFS has no POSIX
    /// mode bits) or when <see cref="ArchiveOption.FileChmodMode"/> is 0, and best-effort: if the
    /// process isn't the file's owner (and isn't root), the OS itself will refuse the chmod — that's a
    /// pre-existing permission problem on that specific file, not something this call can fix, so
    /// failures are logged as a warning rather than thrown.
    /// </summary>
    public void EnsurePermissiveFileMode(string path)
    {
        if (Options.FileChmodMode <= 0 || OperatingSystem.IsWindows()) return;

        try
        {
            File.SetUnixFileMode(path, ToUnixFileMode(Options.FileChmodMode));
        }
        catch (Exception ex)
        {
            SendTrace($"Could not set file mode {Options.FileChmodMode} on {path}: {ex.Message}", ETraceLevel.WARNING);
        }
    }

    /// <summary>Same as <see cref="EnsurePermissiveFileMode"/>, but for a directory (using <see cref="ArchiveOption.DirectoryChmodMode"/> — directories need the execute bit to be traversable, hence the separate, more permissive default).</summary>
    public void EnsurePermissiveDirectoryMode(string path)
    {
        if (Options.DirectoryChmodMode <= 0 || OperatingSystem.IsWindows()) return;

        try
        {
            File.SetUnixFileMode(path, ToUnixFileMode(Options.DirectoryChmodMode));
        }
        catch (Exception ex)
        {
            SendTrace($"Could not set directory mode {Options.DirectoryChmodMode} on {path}: {ex.Message}", ETraceLevel.WARNING);
        }
    }

    /// <summary>Converts a traditional chmod number (e.g. 666, digits read as owner/group/other) into <see cref="UnixFileMode"/> flags.</summary>
    private static UnixFileMode ToUnixFileMode(int chmod)
    {
        int owner = (chmod / 100) % 10;
        int group = (chmod / 10) % 10;
        int other = chmod % 10;

        UnixFileMode mode = 0;
        if ((owner & 4) != 0) mode |= UnixFileMode.UserRead;
        if ((owner & 2) != 0) mode |= UnixFileMode.UserWrite;
        if ((owner & 1) != 0) mode |= UnixFileMode.UserExecute;
        if ((group & 4) != 0) mode |= UnixFileMode.GroupRead;
        if ((group & 2) != 0) mode |= UnixFileMode.GroupWrite;
        if ((group & 1) != 0) mode |= UnixFileMode.GroupExecute;
        if ((other & 4) != 0) mode |= UnixFileMode.OtherRead;
        if ((other & 2) != 0) mode |= UnixFileMode.OtherWrite;
        if ((other & 1) != 0) mode |= UnixFileMode.OtherExecute;
        return mode;
    }

    public async Task<FileInfo> CreateCbzFile(string workingPath, Volume volume, Issue issue, FileInfo comicInfo, List<FileInfo> filepages, ProgressionCallback? progression = null)
    {
        progression?.UpdateTotal(filepages.Count + 1);
        var progress = new Progression() { Total = filepages.Count + 1 };

        var filecbzname = GetPath(issue, volume);
        var cbzPath = Path.Combine(workingPath, filecbzname);

        await using var zipStream = File.Create(cbzPath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

        archive.CreateEntryFromFile(comicInfo.FullName, comicInfo.Name, Options.ZipCompressionLevel);
        progress.Increment(true);
        SendTrace($"Successfully zip page {progress.Completed}/{progress.Total}");
        progression?.Callback(progress);

        foreach (var page in filepages)
        {
            archive.CreateEntryFromFile(page.FullName, page.Name, Options.ZipCompressionLevel);
            progress.Increment(true);
            SendTrace($"Successfully zip page {progress.Completed}/{progress.Total}");
            progression?.Callback(progress);
        }
        var file = new FileInfo(cbzPath);
        SendTrace($"CBZ created to {file.Name} (size {file.Length / 1024.0:F1} KB)");
        EnsurePermissiveFileMode(cbzPath);
        return new FileInfo(cbzPath);
    }

    #endregion

    #region Quality Analysis

    /// <summary>
    /// Read-only measurement of an existing .cbz file's Kavita compatibility — used solely by the
    /// "Analyze" feature (never by the conversion pipeline above, which always writes deterministically
    /// per <see cref="ArchiveOption"/>, not by chasing a maximum score).
    /// </summary>
    public async Task<CbzAnalysisResult> AnalyzeCbzAsync(string cbzPath, ScoringSettings scoringSettings,
        IProgress<CbzAnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
        => await new CbzAnalyzer().AnalyzeAsync(cbzPath, options: null, scoringSettings, progress, cancellationToken);

    public static KavitaCompatibilityReport ScoreCbz(CbzAnalysisResult analysis, ScoringSettings scoringSettings)
        => KavitaCompatibilityScorer.Score(analysis, scoringSettings);

    public static async Task<string> ComputeFileHashAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
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
            EnsurePermissiveDirectoryMode(path);
            return new DirectoryInfo(path);
        }
        var dir = Directory.CreateDirectory(path);
        EnsurePermissiveDirectoryMode(path);
        return dir;
    }

    #endregion


}

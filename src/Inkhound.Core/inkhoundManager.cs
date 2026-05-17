using Inkhound.Core.ComicVine;
using Inkhound.Core.Models;
using Foundation.Core.Model;
using Foundation.Core;

using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Inkhound.Core.DbStorage;
using Inkhound.Core.ComicArchiveGenerator;
using Inkhound.Core.Kavita;
using Inkhound.Core.Kavita.Models;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using SharpCompress.Compressors.ZStandard.Unsafe;
using System.Text.RegularExpressions;

namespace Inkhound.Core;

public class InkhoundManager : BaseServiceManager
{
    private readonly string _dbPath;



    //public InkhoundDbContext? Database { get; private set; }

    public InkhoundManager(string dbPath = "data/inkhound.db")
    {
        _dbPath = dbPath;

    }

    // Checks if the SQLite database exists and creates it if needed, then sets the Database property



    public StateServiceManager GetCurrentState() => CurrentState;

    #region Internal Service Management
    public List<string> GetServiceNames()
        => [.. Services.Values.Select(s => s.GetServiceName())];


    private DbStorageContext GetDb()
    {
        var db = GetService<DbStorageService, DbStorageOption>();
        if (db.Database is null)
            throw new InvalidOperationException("Database is not initialized.");
        return db.Database;
    }


    public List<OptionDefinition> GetOptionsForService(string serviceName)
    {
        var db = GetService<DbStorageService, DbStorageOption>();
        return db.GetOptionsForService(serviceName);
    }

    public async Task<bool> UpdateOptionsForService(string serviceName, Dictionary<string, string> updates)
    {
        var db = GetService<DbStorageService, DbStorageOption>();
        var existing = db.GetOptionsForService(serviceName);

        foreach (var option in existing)
        {
            if (updates.TryGetValue(option.Name, out var value))
                option.Value = value;
        }

        if (!db.SetOptionsForService(existing)) return false;

        var service = Services.Values.FirstOrDefault(s => s.GetServiceName() == serviceName);
        if (service is not null)
            await service.LoadOptions(existing);

        return true;
    }

    public async Task AutomaticLoadServices()
    {
        // Instantiate services
        var databaseService = GetService<DbStorageService, DbStorageOption>();
        var comicVine = GetService<ComicVineService, ComicVineOptions>();
        var archiveService = GetService<ArchiveService, ArchiveOption>();
        var kavitaService = GetService<KavitaService, KavitaOptions>();

        // Load database options and initialize database
        var databaseoption = new DbStorageOption { Path = _dbPath, UseInMemory = false };

        if (await databaseService.LoadOptions(databaseoption.GetOptions()))
        {
            foreach (var service in Services)
            {
                if (databaseService.GetServiceName() != service.Value.GetServiceName())
                {
                    // Try to load stored options for this service from the database          
                    var storedOptions = databaseService.GetOptionsForService(service.Value.GetServiceName());
                    if (storedOptions.Count > 0)
                    {
                        await service.Value.LoadOptions(storedOptions);
                    }
                    else
                    { // No stored options, save current defaults to database
                        var currentOptions = service.Value.GetOptions();
                        databaseService.Database?.SetOptionsForService(currentOptions, service.Value.GetServiceName());
                    }
                }
            }

        }


    }

    public async Task ManuelLoadServiceComicvine(ComicVineOptions options)
    {
        var comicVine = GetService<ComicVineService, ComicVineOptions>();
        await comicVine.LoadOptions(options.GetOptions());

    }


    public async Task ManuelLoadServiceDbStorage(DbStorageOption options)
    {
        var database = GetService<DbStorageService, DbStorageOption>();
        await database.LoadOptions(options.GetOptions());

    }

    public static Volume Map(CvVolume cvVolume)
    {
        return new Volume
        {
            SourceId = cvVolume.Id.ToString(),
            SourceType = "ComicVine",
            Title = cvVolume.Name,
            Year = cvVolume.StartYear != null && int.TryParse(cvVolume.StartYear, out var y) ? y : null,
            Description = cvVolume.Description,
            Image = cvVolume.Image is { } img ? new VolumeImage(img.IconUrl, img.MediumUrl, img.ScreenUrl, img.ScreenLargeUrl, img.SmallUrl, img.SuperUrl, img.ThumbUrl, img.TinyUrl, img.OriginalUrl, img.ImageTags) : null,
            Publisher = cvVolume.Publisher?.Name,
            Authors = cvVolume.People?
                .Select(p => new VolumeAuthor(p.Name, p.Role))
                .ToList() ?? [],
            Issues = cvVolume.Issues?.Select(c => c.Name).ToList()
        };
    }

    public static Issue Map(CvIssue cvIssue)
    {
        int.TryParse(cvIssue.IssueNumber, out var issueNum);
        return new Issue
        {
            ComicVineId = cvIssue.Id.ToString(),
            IssueNumber = issueNum,
            Title = cvIssue.Name,
            Year = cvIssue.CoverDate != null && DateTime.TryParse(cvIssue.CoverDate, out var d) ? d.Year : null,
            Description = cvIssue.Description,
            Image = cvIssue.Image is { } img ? new VolumeImage(img.IconUrl, img.MediumUrl, img.ScreenUrl, img.ScreenLargeUrl, img.SmallUrl, img.SuperUrl, img.ThumbUrl, img.TinyUrl, img.OriginalUrl, img.ImageTags) : null,
            Authors = cvIssue.PersonCredits?
                .Select(p => new VolumeAuthor(p.Name, p.Role))
                .ToList() ?? [],
        };
    }


    #endregion

    #region Library CRUD  

    public async Task<List<Library>> GetLibrariesAsync()
    {
        return await GetDb().Libraries.ToListAsync();
    }

    public async Task<Library?> GetLibraryAsync(Guid id)
    {
        return await GetDb().Libraries.FindAsync(id);
    }

    public async Task<Library> CreateLibraryAsync(string name, string path, int kavitaLibraryId)
    {
        var ctx = GetDb();
        var library = new Library
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = path,
            KavitaLibraryId = kavitaLibraryId,
            CreatedAt = DateTime.UtcNow
        };
        ctx.Libraries.Add(library);
        await ctx.SaveChangesAsync();
        return library;
    }

    public async Task<Library?> UpdateLibraryAsync(Guid id, string name, string path, int kavitaLibraryId)
    {
        var ctx = GetDb();
        var library = await ctx.Libraries.FindAsync(id);
        if (library is null) return null;

        library.Name = name;
        library.Path = path;
        library.KavitaLibraryId = kavitaLibraryId;
        await ctx.SaveChangesAsync();
        return library;
    }

    public async Task<bool> DeleteLibraryAsync(Guid id)
    {
        var ctx = GetDb();
        var library = await ctx.Libraries.FindAsync(id);
        if (library is null) return false;

        ctx.Libraries.Remove(library);
        await ctx.SaveChangesAsync();
        return true;
    }
    #endregion

    #region Volume CRUD
    public async Task<Volume?> GetVolumeAsync(Guid id)
    {
        return await GetDb().Volumes.FindAsync(id);
    }

    public async Task<List<Volume>> GetVolumesByLibraryAsync(Guid libraryId)
    {
        return await GetDb().Volumes
            .Where(v => v.LibraryId == libraryId)
            .OrderBy(v => v.Title)
            .ToListAsync();
    }

    public async Task<List<Issue>> GetIssuesByVolumeAsync(Guid volumeId)
    {
        return await GetDb().Issues
            .Where(i => i.VolumeId == volumeId)
            .OrderBy(i => i.IssueNumber)
            .ToListAsync();
    }

    #endregion



    #region ComicVine Search and Import
    public async Task<Page<CvVolumeStub>> ComicVineSearchVolumeByNameAsync(
        string name,
        int pageNumber = 1,
        int? pageSize = null,
        CancellationToken ct = default)
    {
        var comicVine = GetService<ComicVineService, ComicVineOptions>();
        if (comicVine.CurrentState.State != EState.OK)
            throw new InvalidOperationException("ComicVine service is not available");

        var response = await comicVine.SearchVolumesByNameAsync(name, pageNumber, pageSize, ct: ct);

        return new Page<CvVolumeStub>
        {
            Items = response.Results,
            PageNumber = pageNumber,
            PageSize = response.Limit,
            TotalItems = response.NumberOfTotalResults,
        };
    }

    public async Task<Page<CvIssue>> ComicVineGetIssuesByVolumeAsync(
        int comicVineVolumeId, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var comicVine = GetService<ComicVineService, ComicVineOptions>();
        if (comicVine.CurrentState.State != EState.OK)
            throw new InvalidOperationException("ComicVine service is not available");

        var response = await comicVine.GetIssuesPageAsync(comicVineVolumeId, page, pageSize, ct: ct);

        return new Page<CvIssue>
        {
            Items = response.Results,
            PageNumber = page,
            PageSize = response.Limit,
            TotalItems = response.NumberOfTotalResults,
        };
    }

    #region ComicVine Integration
    public async Task<Volume> AddVolumeFromComicVineAsync(
        Guid libraryId, int comicVineVolumeId, CancellationToken ct = default)
    {
        var ctx = GetDb();

        _ = await ctx.Libraries.FindAsync([libraryId], ct)
            ?? throw new KeyNotFoundException($"Library {libraryId} not found.");

        var duplicate = await ctx.Volumes.FirstOrDefaultAsync(
            v => v.LibraryId == libraryId && v.SourceId == comicVineVolumeId.ToString(), ct);
        if (duplicate is not null)
            throw new InvalidOperationException($"Volume {comicVineVolumeId} already exists in this library.");

        var comicVine = GetService<ComicVineService, ComicVineOptions>();
        if (comicVine.CurrentState.State != EState.OK)
            throw new InvalidOperationException("ComicVine service is not available");

        var cvVolume = await comicVine.GetVolumeAsync(comicVineVolumeId, ct)
            ?? throw new KeyNotFoundException($"ComicVine volume {comicVineVolumeId} not found.");

        var now = DateTime.UtcNow;
        var volume = Map(cvVolume);
        volume.Id = Guid.NewGuid();
        volume.LibraryId = libraryId;
        volume.Status = VolumeStatus.MONITORED;
        volume.CreatedAt = now;
        volume.UpdatedAt = now;
        ctx.Volumes.Add(volume);
        await ctx.SaveChangesAsync(ct);

        var cvIssues = await comicVine.GetAllIssuesForVolumeAsync(comicVineVolumeId, ELevelDetail.FULL, ct);
        foreach (var cvIssue in cvIssues)
        {
            var issue = Map(cvIssue);
            issue.Id = Guid.NewGuid();
            issue.Status = IssueStatus.MISSING;
            issue.VolumeId = volume.Id;
            ctx.Issues.Add(issue);
        }
        if (cvIssues.Count > 0)
            await ctx.SaveChangesAsync(ct);

        return volume;
    }


    public async Task<FileInfo?> LaunchJobImportArchive(ArchiveConverterPdfJobParameters parameters)
    {
        var archiveService = GetService<ArchiveService, ArchiveOption>();

        if (archiveService.CurrentState.State != EState.OK)
        {
            throw new InvalidOperationException("ArchiveService is not available");
        }

        var job = StartJob($"Transform {parameters.SourceFile} to archive", parameters);
        job.SetState(JobState.RUNNING);

        var sourcefile = File.Exists(parameters.SourceFile) ? new FileInfo(parameters.SourceFile) : null;
        if (sourcefile == null)
        {
            EndJob(false);
            return null;
        }

        // STEP 1 : Get pages from file
        List<FileInfo>? pageFiles = null;
        switch (await archiveService.GetArchiveType(sourcefile.FullName))
        {
            case EArchiveType.PDF:
                pageFiles = await archiveService.ConvertPdfToImage(sourcefile, parameters.WorkingPath, job.CallbackHandler);
                break;
            case EArchiveType.CBR:
                pageFiles = await archiveService.ConvertCbrToImage(sourcefile, parameters.WorkingPath, job.CallbackHandler);
                break;
            case EArchiveType.CBZ:
                pageFiles = await archiveService.ConvertCbzToImage(sourcefile, parameters.WorkingPath, job.CallbackHandler);
                break;
            default:
                EndJob(false);
                return null;

        }

        if (pageFiles == null)
        {
            EndJob(false);
            return null;
        }

        // STEP 2 : Add ComicsInfo
        var comicsInfo = await archiveService.CreateComicInfo(parameters.Volume, parameters.Issue, parameters.WorkingPath, job.CallbackHandler);

        // STEP 3 : Generage CBZ
        var archive = await archiveService.CreateCbzFile(parameters.Library, parameters.Volume, parameters.Issue, comicsInfo, pageFiles, job.CallbackHandler);

        // STEP 4 : Delete files
        comicsInfo.Delete();
        pageFiles.ForEach(c => c.Delete());

        EndJob(job.Progress.Error < job.Progress.Total);

        return archive;



    }

    private static readonly string[] _archiveExtensions = ["*.cbz", "*.cbr", "*.pdf"];
    private static readonly Regex _issueHashRegex = new(@"#\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex _digitSeqRegex = new(@"\d+", RegexOptions.Compiled);

    public async Task ImportArchiveFromDirectoryAsync(Guid volumeId, string importDirectory, CancellationToken ct = default)
    {
        var ctx = GetDb();

        var volume = await ctx.Volumes.FindAsync([volumeId], ct)
            ?? throw new KeyNotFoundException($"Volume {volumeId} not found.");
        var library = await ctx.Libraries.FindAsync([volume.LibraryId], ct)
            ?? throw new KeyNotFoundException($"Library {volume.LibraryId} not found.");
        var issues = await ctx.Issues.Where(i => i.VolumeId == volumeId).ToListAsync(ct);

        var archiveService = GetService<ArchiveService, ArchiveOption>();
        if (archiveService.CurrentState.State != EState.OK)
            throw new InvalidOperationException("ArchiveService is not available");

        var tempRelative = Guid.NewGuid().ToString("N");
        var tempAbsolute = Path.Combine(archiveService.WorkingPath, tempRelative);
        Directory.CreateDirectory(tempAbsolute);

        try
        {
            var files = _archiveExtensions
                .SelectMany(ext => Directory.GetFiles(importDirectory, ext))
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.Name)
                .ToList();

            foreach (var file in files)
            {
                var issueNumber = ParseIssueNumber(file.Name);
                if (issueNumber is null) continue;

                var issue = issues.FirstOrDefault(i => i.IssueNumber == issueNumber);
                if (issue is null) continue;

                var subPath = Path.Combine(tempRelative, issueNumber.Value.ToString("D3"));
                var parameters = new ArchiveConverterPdfJobParameters
                {
                    SourceFile = file.FullName,
                    WorkingPath = subPath,
                    Volume = volume,
                    Issue = issue
                };

                var archive = await LaunchJobImportArchive(parameters);
                if (archive is null) continue;

                var volumeDir = new DirectoryInfo(ArchiveService.GetPath(volume, library));
                volumeDir.Create();
                var dest = Path.Combine(volumeDir.FullName, archive.Name);
                File.Move(archive.FullName, dest, overwrite: true);

                issue.CbzFilename = archive.Name;
                issue.FilePath = dest;
                issue.Status = IssueStatus.DOWNLOADED;
                await ctx.SaveChangesAsync(ct);
            }
        }
        finally
        {
            if (Directory.Exists(tempAbsolute))
                Directory.Delete(tempAbsolute, recursive: true);
        }
    }

    private static int? ParseIssueNumber(string filename)
    {
        var name = Path.GetFileNameWithoutExtension(filename);
        var m = _issueHashRegex.Match(name);
        if (m.Success) return int.Parse(m.Groups[1].Value);
        var seqs = _digitSeqRegex.Matches(name)
                                 .Cast<Match>()
                                 .Select(x => int.Parse(x.Value))
                                 .Where(n => !(n >= 1800 && n <= 2099))
                                 .ToList();
        return seqs.Count > 0 ? seqs[0] : null;
    }

    public async Task<List<DirectoryInfo>> GetDirectories(string path)
    {
        return await Task.Run(() =>
        {
            var dir = new DirectoryInfo(path);
            return dir.Exists
                ? new List<DirectoryInfo>(dir.GetDirectories())
                : new List<DirectoryInfo>();
        });
    }

    public async Task<List<FileInfo>> GetFiles(string path, string filter = "*")
    {
        return await Task.Run(() =>
        {
            var dir = new DirectoryInfo(path);
            return dir.Exists
                ? new List<FileInfo>(dir.GetFiles(filter))
                : new List<FileInfo>();
        });
    }

    #endregion

    #endregion

    #region Kavita

    private List<KavitaLibrary>? _kavitaLibrariesCache;

    public async Task<List<KavitaLibrary>> GetKavitaLibrariesAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _kavitaLibrariesCache is not null && _kavitaLibrariesCache.Count > 0)
            return _kavitaLibrariesCache;

        var kavita = GetService<KavitaService, KavitaOptions>();
        _kavitaLibrariesCache = await kavita.GetLibrariesAsync();
        return _kavitaLibrariesCache;
    }

    public async Task<bool> ScanKavitaLibraryAsync(int libraryId, bool force = false)
    {
        var kavita = GetService<KavitaService, KavitaOptions>();
        return await kavita.ScanLibraryAsync(libraryId, force);
    }

    #endregion

    #region Library Management

    public async Task<Library?> LaunchJobSynchronizeLibrary(SynchronizeLibraryJobParameters parameters)
    {
        var job = StartJob($"Synchronize library {parameters.LibraryId}", parameters);
        job.SetState(JobState.RUNNING);

        var ctx = GetDb();
        var library = await ctx.Libraries.FindAsync(parameters.LibraryId);
        if (library is null)
        {
            EndJob(false);
            return null;
        }

        var comicVine = GetService<ComicVineService, ComicVineOptions>();
        if (comicVine.CurrentState.State != EState.OK)
        {
            EndJob(false);
            return null;
        }

        // STEP 1. Scan directories in library path and match with ComicVine volumes
        var libraryDir = new DirectoryInfo(library.Path);
        var directories = await GetDirectories(library.Path);

        job.CallbackHandler.UpdateTotal(directories.Count);

        var existingVolumes = await ctx.Volumes
            .Where(v => v.LibraryId == parameters.LibraryId)
            .ToDictionaryAsync(v => v.SourceId, v => v);

        foreach (var dir in directories)
        {

            Volume volume;
            if (existingVolumes.Values.Any(v => ArchiveService.GetPath(v, library) == dir.FullName))
            {
                // This directory is already matched to an existing volume, skip it
                volume = existingVolumes.Values.First(v => ArchiveService.GetPath(v, library) == dir.FullName);
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);

            }
            else
            {
                // Try to find a ComicVine volume match for this directory name
                var cvVolume = await comicVine.AutomaticSearchVolume(dir.Name, parameters.CountryCode, job.CallbackHandler);
                if (cvVolume is null)
                {
                    var trace = new TraceDefinition { Level = ETraceLevel.DEBUG };
                    trace.Message.Add($"[Sync] No ComicVine match for directory: {dir.Name}");
                    GlobalTraceHandler(trace);
                    job.Progress.Increment(false);
                    job.CallbackHandler.Callback(job.Progress);
                    continue;
                }


                var sourceId = cvVolume.Id.ToString();

                if (existingVolumes.ContainsKey(sourceId))
                {
                    volume = existingVolumes[sourceId];
                    // This ComicVine volume is already in the database, but with the wrong path
                    dir.MoveTo(ArchiveService.GetPath(volume, library));
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);

                }
                else
                {

                    volume = Map(cvVolume);
                    volume.Id = Guid.NewGuid();
                    volume.LibraryId = parameters.LibraryId;
                    volume.Status = VolumeStatus.FREEZE;
                    ctx.Volumes.Add(volume);
                    await ctx.SaveChangesAsync(); // Save here to get the Volume ID for issue linking                
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                }
            }

            // Finish matching this volume to the directory (in case it was just created or had wrong path)


            // STEP 2. Find CBZ files in volume directory and match to ComicVine issues
            var cbzFiles = await GetFiles(dir.FullName, "*.cbz");
            int.TryParse(volume.SourceId, out var sourceVolumeId);
            var cvIssues = await comicVine.GetAllIssuesForVolumeAsync(sourceVolumeId, ELevelDetail.ID);

            var existingIssueIds = await ctx.Issues
                .Where(i => i.VolumeId == volume.Id)
                .ToDictionaryAsync(i => i.ComicVineId, i => i);

            var matchedCvIssueIds = new HashSet<int>();

            foreach (var cbzFile in cbzFiles)
            {
                if (existingIssueIds.Values.Any(v => v.CbzFilename == cbzFile.Name))
                {
                    continue; // This CBZ file is already matched to an existing issue, skip it
                }
                {

                    var issueNum = ComicVineService.ParseIssueNumber(cbzFile.Name);
                    if (issueNum is null)
                        continue;

                    if (existingIssueIds.Values.Any(v => v.IssueNumber == issueNum))
                    {
                        var existingIssue = existingIssueIds.Values.First(v => v.IssueNumber == issueNum);
                        existingIssue.Status = IssueStatus.DOWNLOADED;
                        var issuenumberfilename = ArchiveService.GetPath(existingIssue, volume);
                        if (string.IsNullOrEmpty(existingIssue.CbzFilename))
                        { // This issue was previously matched without a file, so just update the filename and path
                            if (cbzFile.Name != issuenumberfilename)
                            {
                                var newPath = Path.Combine(cbzFile.DirectoryName ?? string.Empty, issuenumberfilename);
                                cbzFile.MoveTo(newPath);
                                existingIssue.FilePath = newPath;
                            }
                            existingIssue.CbzFilename = issuenumberfilename;
                            existingIssue.Status = IssueStatus.DOWNLOADED;
                            continue;
                        }
                        var issueExistFile = new FileInfo(Path.Combine(cbzFile.DirectoryName ?? string.Empty, existingIssue.CbzFilename ?? string.Empty));
                        if (issueExistFile.Exists)
                        {
                            if (issueExistFile.Name != issuenumberfilename || issueExistFile.Length < cbzFile.Length)
                            {
                                issueExistFile.Delete();
                                if (cbzFile.Name != issuenumberfilename)
                                {
                                    var newPath = Path.Combine(cbzFile.DirectoryName ?? string.Empty, issuenumberfilename);
                                    cbzFile.MoveTo(newPath);
                                    existingIssue.FilePath = newPath;
                                    existingIssue.CbzFilename = issuenumberfilename;
                                }
                                else
                                {
                                    existingIssue.FilePath = cbzFile.FullName;
                                    existingIssue.CbzFilename = cbzFile.Name;
                                }
                                continue;
                            }
                            // The existing file is correct, just update the path if needed and delete the new file
                            if (cbzFile.Name != issuenumberfilename)
                            {
                                cbzFile.Delete();
                            }
                            var existingPath = Path.Combine(cbzFile.DirectoryName ?? string.Empty, existingIssue.CbzFilename ?? string.Empty);
                            if (existingIssue.FilePath != existingPath)
                            {
                                existingIssue.FilePath = existingPath;
                            }
                            existingIssue.CbzFilename = issuenumberfilename;
                            existingIssue.Status = IssueStatus.DOWNLOADED;
                            continue;

                        }




                        if (cbzFile.Name != issuenumberfilename)
                        {
                            var newPath = Path.Combine(cbzFile.DirectoryName ?? string.Empty, issuenumberfilename);
                            cbzFile.MoveTo(newPath);
                            existingIssue.FilePath = newPath;
                            existingIssue.CbzFilename = issuenumberfilename;
                        }
                        continue;
                    }

                    // Try to find a ComicVine issue match for this CBZ file based on issue number
                    var cvIssueRef = cvIssues.FirstOrDefault(
                        i => int.TryParse(i.IssueNumber, out var n) && n == issueNum);
                    if (cvIssueRef is null)
                        continue;

                    matchedCvIssueIds.Add(cvIssueRef.Id);


                    var cvIssueFull = await comicVine.GetIssueAsync(cvIssueRef.Id);
                    if (cvIssueFull is null)
                        continue;

                    var issue = Map(cvIssueFull);
                    var issuefilename = ArchiveService.GetPath(issue, volume);


                    if (cbzFile.Name != issuefilename)
                    {
                        var newPath = Path.Combine(cbzFile.DirectoryName ?? string.Empty, issuefilename);
                        cbzFile.MoveTo(newPath);
                        issue.FilePath = newPath;
                        issue.CbzFilename = issuefilename;
                    }
                    else
                    {
                        issue.FilePath = cbzFile.FullName;
                        issue.CbzFilename = cbzFile.Name;
                    }
                    issue.VolumeId = volume.Id;
                    issue.Status = IssueStatus.DOWNLOADED;
                    ctx.Issues.Add(issue);

                }

                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);
            }

            await ctx.SaveChangesAsync();
        }
        EndJob(job.Progress.Error < directories.Count);
        return library;
    }

    #endregion
}
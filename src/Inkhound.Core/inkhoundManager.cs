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
using SharpCompress.Compressors.ZStandard.Unsafe;

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

        var response = await comicVine.GetIssuesPageAsync(comicVineVolumeId, page, pageSize, ELevelDetail.SUMMARY, ct: ct);

        return new Page<CvIssue>
        {
            Items = response.Results,
            PageNumber = page,
            PageSize = response.Limit,
            TotalItems = response.NumberOfTotalResults,
        };
    }





    #endregion


    #region Archive

    public async Task<List<DirectoryInfo>> GetDirectoriesAsync(string path)
    {
        return await ArchiveService.GetDirectoriesAsync(path);
    }

    public async Task<List<FileInfo>> GetFilesAsync(string path, string filter = "*")
    {
        return await ArchiveService.GetFilesAsync(path, filter);
    }

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

    #region Library Actions

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
        var directories = await ArchiveService.GetDirectoriesAsync(library.Path);


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

                if (existingVolumes.TryGetValue(sourceId, out var existingVolume))
                {
                    volume = existingVolume;
                    // This ComicVine volume is already in the database, but with the wrong path
                    dir.MoveTo(ArchiveService.GetPath(volume, library));
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);

                }
                else
                {

                    volume = Mapper.Map(cvVolume);
                    volume.Id = Guid.NewGuid();
                    volume.LibraryId = parameters.LibraryId;
                    volume.Status = VolumeStatus.PAUSED;
                    ctx.Volumes.Add(volume);

                    // ADD issue in MISSING status for all issues in this volume, will be updated to DOWNLOADED if a matching CBZ file is found in step 2
                    var cvIssuesFull = await comicVine.GetAllIssuesForVolumeAsync(cvVolume.Id, ELevelDetail.FULL);
                    List<VolumeAuthor> allIssueAuthors = [];
                    foreach (var cvIssue in cvIssuesFull)
                    {
                        var issue = Mapper.Map(cvIssue);
                        issue.Id = Guid.NewGuid();
                        issue.VolumeId = volume.Id;
                        issue.Status = IssueStatus.MISSING;
                        ctx.Issues.Add(issue);
                        allIssueAuthors.AddRange(issue.Authors);
                    }

                    var roleByName = allIssueAuthors
                        .GroupBy(a => a.Name)
                        .ToDictionary(g => g.Key, g => g.First().Role);

                    volume.Authors = volume.Authors
                        .Select(a => string.IsNullOrEmpty(a.Role) && roleByName.TryGetValue(a.Name, out var role)
                            ? new VolumeAuthor(a.Name, role ?? string.Empty)
                            : a)
                        .ToList();

                    await ctx.SaveChangesAsync(); // Save here to get the Volume ID for issue linking

                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                }
            }

            // Finish matching this volume to the directory (in case it was just created or had wrong path)


            // STEP 2. Find CBZ files in volume directory and match to ComicVine issues
            var cbzFiles = await ArchiveService.GetFilesAsync(dir.FullName, "*.cbz");
            job.CallbackHandler.UpdateTotal(cbzFiles.Count);
            int.TryParse(volume.SourceId, out var sourceVolumeId);

            //var cvIssues = await comicVine.GetAllIssuesForVolumeAsync(sourceVolumeId, ELevelDetail.SUMMARY);

            var localIssues = await ctx.Issues
                .Where(i => i.VolumeId == volume.Id).ToListAsync();

            foreach (var cbzFile in cbzFiles)
            {
                if (localIssues.Any(v => v.CbzFilename == cbzFile.Name))
                {
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    continue; // This CBZ file is already matched to an existing issue, skip it
                }



                var issueNum = ComicVineService.ParseIssueNumber(cbzFile.Name);
                if (issueNum is null)
                {
                    job.Progress.Increment(false);
                    job.CallbackHandler.Callback(job.Progress);
                    continue;
                }
                if (!localIssues.Any(v => v.IssueNumber == issueNum))
                {
                    job.Progress.Increment(false);
                    job.CallbackHandler.Callback(job.Progress);
                    continue; // No issue with this number in the database, skip it
                }

                var existingIssue = localIssues.First(v => v.IssueNumber == issueNum);
                var issuenumberfilename = ArchiveService.GetPath(existingIssue, volume);
                if (existingIssue.Status != IssueStatus.DOWNLOADED)
                { // This issue was previously matched without a file, so just update the filename and path

                    ArchiveService.AttachFileToIssue(cbzFile, existingIssue, volume, library);
                    await ctx.SaveChangesAsync();
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    continue;
                }
                var issueExistFile = new FileInfo(ArchiveService.GetPath(existingIssue, volume, library));
                if (issueExistFile.Exists)
                {
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    if (issueExistFile.Name != issuenumberfilename || issueExistFile.Length < cbzFile.Length)
                    {
                        issueExistFile.Delete();
                        ArchiveService.AttachFileToIssue(cbzFile, existingIssue, volume, library);
                        await ctx.SaveChangesAsync();
                        job.Progress.Increment(true);
                        job.CallbackHandler.Callback(job.Progress);

                        continue;
                    }
                    // The existing file is correct, just update the path if needed and delete the new file
                    if (cbzFile.Name != issuenumberfilename)
                    {
                        cbzFile.Delete();
                    }

                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    continue;

                }

                ArchiveService.AttachFileToIssue(cbzFile, existingIssue, volume, library);
                await ctx.SaveChangesAsync();
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);



            }

            await ctx.SaveChangesAsync();
        }

        library.UpdatedAt = DateTime.UtcNow;
        ctx.SaveChanges();

        EndJob(job.Progress.Error < directories.Count);
        return library;
    }




    #endregion

    #region Volume Actions
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
        var volume = Mapper.Map(cvVolume);
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
            var issue = Mapper.Map(cvIssue);
            issue.Id = Guid.NewGuid();
            issue.Status = IssueStatus.MISSING;
            issue.VolumeId = volume.Id;
            ctx.Issues.Add(issue);
        }
        if (cvIssues.Count > 0)
            await ctx.SaveChangesAsync(ct);

        return volume;
    }

    #endregion

    #region Issue Actions
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
        var pageFiles = await archiveService.ConvertToImages(sourcefile, parameters.WorkingPath, job.CallbackHandler);
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

        var tempDir = archiveService.GenerateTempDirectory();

        try
        {
            var files = _archiveExtensions
                .SelectMany(ext => Directory.GetFiles(importDirectory, ext))
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.Name)
                .ToList();

            foreach (var file in files)
            {
                var issueNumber = ComicVineService.ParseIssueNumber(file.Name);
                if (issueNumber is null) continue;

                var issue = issues.FirstOrDefault(i => i.IssueNumber == issueNumber);
                if (issue is null) continue;

                var subPath = Path.Combine(tempDir.FullName, issueNumber.Value.ToString("D3"));
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
                issue.Status = IssueStatus.DOWNLOADED;
                await ctx.SaveChangesAsync(ct);
            }
        }
        finally
        {
            if (tempDir.Exists)
                tempDir.Delete(recursive: true);
        }
    }


    #endregion
}
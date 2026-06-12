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
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Library>(library.Id));
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
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Library>(library.Id));
        return library;
    }

    public async Task<bool> DeleteLibraryAsync(Guid id)
    {
        var ctx = GetDb();
        var library = await ctx.Libraries.FindAsync(id);
        if (library is null) return false;

        ctx.Libraries.Remove(library);
        await ctx.SaveChangesAsync();
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Library>(id));
        return true;
    }

    public async Task<bool> DeleteVolumeAsync(Guid id)
    {
        var ctx = GetDb();
        var volume = await ctx.Volumes.FindAsync(id);
        if (volume is null) return false;

        ctx.Issues.RemoveRange(ctx.Issues.Where(i => i.VolumeId == id));
        ctx.Volumes.Remove(volume);
        await ctx.SaveChangesAsync();
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(id));
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

        try
        {

        // STEP 1. Scan directories in library path and match with ComicVine volumes
        var libraryDir = new DirectoryInfo(library.Path);
        var directories = await ArchiveService.GetDirectoriesAsync(library.Path);

        JobSendTrace("[Sync] Found " + directories.Count + " directories in library path");

        job.CallbackHandler.UpdateTotal(directories.Count);

        var existingVolumes = await ctx.Volumes
            .Where(v => v.LibraryId == parameters.LibraryId)
            .ToDictionaryAsync(v => v.SourceId, v => v);

        JobSendTrace($"[Sync] Found {existingVolumes.Count} existing volumes in database for this library");

        foreach (var dir in directories)
        {

            Volume volume;
            if (existingVolumes.Values.Any(v => ArchiveService.GetPath(v, library) == dir.FullName))
            {
                JobSendTrace($"[Sync] Directory {dir.FullName} is already matched to an existing volume, skipping");
                // This directory is already matched to an existing volume, skip it
                volume = existingVolumes.Values.First(v => ArchiveService.GetPath(v, library) == dir.FullName);
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);

            }
            else
            {
                // Try to find a ComicVine volume match for this directory name
                JobSendTrace($"[Sync] Searching for ComicVine volume for directory: {dir.Name}");
                var cvVolume = await comicVine.AutomaticSearchVolume(dir.Name, parameters.CountryCode, job.CallbackHandler);
                if (cvVolume is null)
                {
                    JobSendTrace($"[Sync] No ComicVine match for directory: {dir.Name}", ETraceLevel.DEBUG);
                    job.Progress.Increment(false);
                    job.CallbackHandler.Callback(job.Progress);
                    continue;
                }


                var sourceId = cvVolume.Id.ToString();
                JobSendTrace($"[Sync] Found ComicVine match for directory {dir.Name} : {cvVolume.Name} (sourceId={sourceId})");
                if (existingVolumes.TryGetValue(sourceId, out var existingVolume))
                {
                    volume = existingVolume;
                    // This ComicVine volume is already in the database, but with the wrong path
                    dir.MoveTo(ArchiveService.GetPath(volume, library));
                    JobSendTrace($"[Sync] Updated path for existing volume {volume.Title} to {dir.FullName}");
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
                    JobSendTrace($"[Sync] Added new volume {volume.Title} to database with path {dir.FullName}");

                    // ADD issue in MISSING status for all issues in this volume, will be updated to DOWNLOADED if a matching CBZ file is found in step 2
                    JobSendTrace($"[Sync] Adding issues for volume {volume.Title}");
                    var cvIssuesFull = await comicVine.GetAllIssuesForVolumeAsync(cvVolume.Id, ELevelDetail.FULL);
                    foreach (var cvIssue in cvIssuesFull)
                    {
                        var issue = Mapper.Map(cvIssue);
                        issue.Id = Guid.NewGuid();
                        issue.VolumeId = volume.Id;
                        issue.Status = IssueStatus.MISSING;
                        ctx.Issues.Add(issue);
                    }

                    JobSendTrace($"[Sync] Added {cvIssuesFull.Count} issues for volume {volume.Title} to database");
                    await ctx.SaveChangesAsync(); // Save here to get the Volume ID for issue linking
                    OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));

                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                }
            }



            // Finish matching this volume to the directory (in case it was just created or had wrong path)


            // STEP 2. Find CBZ files in volume directory and match to ComicVine issues
            var cbzFiles = await ArchiveService.GetFilesAsync(dir.FullName, "*.cbz");
            JobSendTrace($"[Sync] Found {cbzFiles.Count} CBZ files in directory {dir.FullName} for volume {volume.Title}");
            job.CallbackHandler.UpdateTotal(cbzFiles.Count);
            int.TryParse(volume.SourceId, out var sourceVolumeId);

            //var cvIssues = await comicVine.GetAllIssuesForVolumeAsync(sourceVolumeId, ELevelDetail.SUMMARY);

            var localIssues = await ctx.Issues
                .Where(i => i.VolumeId == volume.Id).ToListAsync();
            JobSendTrace($"[Sync] Found {localIssues.Count} issues in database for volume {volume.Title}");

            foreach (var cbzFile in cbzFiles)
            {
                if (localIssues.Any(v => v.CbzFilename == cbzFile.Name))
                {
                    JobSendTrace($"[Sync] CBZ file {cbzFile.Name} is already matched to an existing issue, skipping");
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    continue; // This CBZ file is already matched to an existing issue, skip it
                }



                var issueNum = ComicVineService.ParseIssueNumber(cbzFile.Name);
                if (issueNum is null)
                {
                    JobSendTrace($"[Sync] Unable to parse issue number from CBZ file {cbzFile.Name}", ETraceLevel.WARNING);
                    job.Progress.Increment(false);
                    job.CallbackHandler.Callback(job.Progress);
                    continue;
                }
                if (!localIssues.Any(v => v.IssueNumber == issueNum))
                {
                    JobSendTrace($"[Sync] No issue with number {issueNum} found in database for volume {volume.Title}, skipping CBZ file {cbzFile.Name}", ETraceLevel.WARNING);
                    job.Progress.Increment(false);
                    job.CallbackHandler.Callback(job.Progress);
                    continue; // No issue with this number in the database, skip it
                }

                var existingIssue = localIssues.First(v => v.IssueNumber == issueNum);
                var issuenumberfilename = ArchiveService.GetPath(existingIssue, volume);
                if (existingIssue.Status != IssueStatus.DOWNLOADED)
                { // This issue was previously matched without a file, so just update the filename and path
                    JobSendTrace($"[Sync] Matching CBZ file {cbzFile.Name} to issue {existingIssue.Title} (issue number {existingIssue.IssueNumber})");
                    ArchiveService.AttachFileToIssue(cbzFile, existingIssue, volume, library);
                    await ctx.SaveChangesAsync();
                    OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(existingIssue.Id));
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    continue;
                }
                var issueExistFile = new FileInfo(ArchiveService.GetPath(existingIssue, volume, library));
                if (issueExistFile.Exists)
                {
                    JobSendTrace($"[Sync] Existing file found for issue {existingIssue.Title} (issue number {existingIssue.IssueNumber})");
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    if (issueExistFile.Name != issuenumberfilename || issueExistFile.Length < cbzFile.Length)
                    {
                        issueExistFile.Delete();
                        ArchiveService.AttachFileToIssue(cbzFile, existingIssue, volume, library);
                        await ctx.SaveChangesAsync();
                        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(existingIssue.Id));
                        job.Progress.Increment(true);
                        job.CallbackHandler.Callback(job.Progress);

                        continue;
                    }
                    // The existing file is correct, just update the path if needed and delete the new file
                    if (cbzFile.Name != issuenumberfilename)
                    {
                        JobSendTrace($"[Sync] Updating path for existing issue {existingIssue.Title} to match CBZ file {cbzFile.Name}");
                        cbzFile.Delete();
                    }

                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    continue;

                }

                JobSendTrace($"[Sync] Attaching CBZ file {cbzFile.Name} to issue {existingIssue.Title} (issue number {existingIssue.IssueNumber})");
                ArchiveService.AttachFileToIssue(cbzFile, existingIssue, volume, library);
                await ctx.SaveChangesAsync();
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(existingIssue.Id));
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);



            }

            // STEP 3. Update volume status and counts
            var volumeIssues = await ctx.Issues
                .Where(i => i.VolumeId == volume.Id).ToListAsync();

            List<VolumeAuthor> allIssueAuthors = [];
            foreach (var cvIssue in volumeIssues)
            {
                allIssueAuthors.AddRange(cvIssue.Authors);
            }

            var roleByName = allIssueAuthors
                .GroupBy(a => a.Name)
                .ToDictionary(g => g.Key, g => g.First().Role);

            volume.Authors = volume.Authors
                .Select(a => string.IsNullOrEmpty(a.Role) && roleByName.TryGetValue(a.Name, out var role)
                    ? new VolumeAuthor(a.Name, role ?? string.Empty)
                    : a)
                .ToList();
            volume.CountOfIssues = localIssues.Count;
            volume.CountOfDownloadedIssues = localIssues.Count(i => i.Status == IssueStatus.DOWNLOADED);


            volume.UpdatedAt = DateTime.UtcNow;
            volume.CountOfIssues = await ctx.Issues.CountAsync(i => i.VolumeId == volume.Id);
            volume.CountOfDownloadedIssues = await ctx.Issues.CountAsync(i => i.VolumeId == volume.Id && i.Status == IssueStatus.DOWNLOADED);
            volume.Status = volume.CountOfIssues == volume.CountOfDownloadedIssues ? VolumeStatus.COMPLETED : VolumeStatus.MONITORED;

            await ctx.SaveChangesAsync();
            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));


        }

        library.UpdatedAt = DateTime.UtcNow;
        JobSendTrace($"[Sync] Synchronization complete for library {library.Name}");
        ctx.SaveChanges();
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Library>(library.Id));

        EndJob(job.Progress.Error < directories.Count);
        return library;
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Sync] Unhandled error during synchronization: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
            return library;
        }
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
        volume.CountOfIssues = 0;
        ctx.Volumes.Add(volume);
        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));

        var cvIssues = await comicVine.GetAllIssuesForVolumeAsync(comicVineVolumeId, ELevelDetail.FULL, ct);

        List<VolumeAuthor> allIssueAuthors = [];

        foreach (var cvIssue in cvIssues)
        {
            var issue = Mapper.Map(cvIssue);
            issue.Id = Guid.NewGuid();
            issue.Status = IssueStatus.MISSING;
            issue.VolumeId = volume.Id;
            allIssueAuthors.AddRange(issue.Authors);
            ctx.Issues.Add(issue);
        }
        if (cvIssues.Count > 0)
        {
            var roleByName = allIssueAuthors
                        .GroupBy(a => a.Name)
                        .ToDictionary(g => g.Key, g => g.First().Role);

            volume.Authors = volume.Authors
                .Select(a => string.IsNullOrEmpty(a.Role) && roleByName.TryGetValue(a.Name, out var role)
                    ? new VolumeAuthor(a.Name, role ?? string.Empty)
                    : a)
                .ToList();



            volume.CountOfIssues = cvIssues.Count;
            volume.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync(ct);
            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));
        }

        return volume;
    }

    public async Task<string> UploadImageAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var archiveService = GetService<ArchiveService, ArchiveOption>();
        var dir = archiveService.ImagesPath;
        Directory.CreateDirectory(dir);
        var filename = $"{Guid.NewGuid()}{extension}";
        var path = Path.Combine(dir, filename);
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct);
        return $"/images/{filename}";
    }

    public async Task<Volume> AddVolumeManuallyAsync(
        Guid libraryId,
        string title,
        int? year,
        string? publisher,
        string? description,
        string? imageUrl,
        List<VolumeAuthor> authors,
        List<string> genres,
        List<(int Number, string? Title, int? Year, string? Description, string? ImageUrl)> issues,
        CancellationToken ct = default)
    {
        var ctx = GetDb();

        _ = await ctx.Libraries.FindAsync([libraryId], ct)
            ?? throw new KeyNotFoundException($"Library {libraryId} not found.");

        var now = DateTime.UtcNow;

        VolumeImage? volumeImage = null;
        if (!string.IsNullOrEmpty(imageUrl))
            volumeImage = new VolumeImage(imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, imageUrl, null);

        var volume = new Volume
        {
            Id = Guid.NewGuid(),
            LibraryId = libraryId,
            SourceId = Guid.NewGuid().ToString(),
            SourceType = "manual",
            Title = title,
            Year = year,
            Publisher = publisher,
            Description = description,
            Image = volumeImage,
            Authors = authors,
            Genres = genres,
            Status = VolumeStatus.MONITORED,
            CreatedAt = now,
            UpdatedAt = now,
            DateAdded = now,
            CountOfIssues = issues.Count
        };

        ctx.Volumes.Add(volume);

        foreach (var issueData in issues)
        {
            VolumeImage? issueImage = null;
            if (!string.IsNullOrEmpty(issueData.ImageUrl))
                issueImage = new VolumeImage(issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, issueData.ImageUrl, null);

            ctx.Issues.Add(new Issue
            {
                Id = Guid.NewGuid(),
                VolumeId = volume.Id,
                ComicVineId = Guid.NewGuid().ToString(),
                IssueNumber = issueData.Number,
                Title = issueData.Title,
                Year = issueData.Year,
                Description = issueData.Description,
                Image = issueImage,
                Authors = [],
                Status = IssueStatus.MISSING
            });
        }

        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));

        return volume;
    }

    public async Task ImportArchiveFromDirectoryAsync(Guid volumeId, string importDirectory, bool overrideExisting = false, CancellationToken ct = default)
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

        var job = StartJob($"Import {importDirectory} to {volume.Title}");
        job.SetState(JobState.RUNNING);

        var tempDir = archiveService.GenerateTempDirectory();
        JobSendTrace($"Created temporary directory {tempDir.FullName} for import processing", ETraceLevel.INFO);
        try
        {
            var files = _archiveExtensions
                .SelectMany(ext => Directory.GetFiles(importDirectory, ext))
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.Name)
                .ToList();

            job.AddTotal(files.Count);
            JobSendTrace($"Found {files.Count} archive files in import directory {importDirectory}", ETraceLevel.INFO);

            foreach (var file in files)
            {
                JobSendTrace($"Processing file {file.Name}", ETraceLevel.INFO);
                var issueNumber = ComicVineService.ParseIssueNumber(file.Name);
                if (issueNumber is null) continue;

                var issue = issues.FirstOrDefault(i => i.IssueNumber == issueNumber);
                if (issue is null) continue;

                if (issue.Status == IssueStatus.DOWNLOADED && !overrideExisting)
                {
                    JobSendTrace($"Skipping {file.Name} (already downloaded)", ETraceLevel.INFO);
                    job.Progress.Increment(true);
                    job.CallbackHandler.Callback(job.Progress);
                    continue; // Skip already downloaded issues if not overriding
                }
                var subPath = Path.Combine(tempDir.FullName, issueNumber.Value.ToString("D3"));
                var parameters = new ArchiveConverterPdfJobParameters
                {
                    SourceFile = file.FullName,
                    WorkingPath = subPath,
                    Library = library,
                    Volume = volume,
                    Issue = issue
                };
                JobSendTrace($"Launching import job for file {file.Name} to issue {issue.Title} (issue number {issue.IssueNumber})", ETraceLevel.INFO);
                var archive = await LaunchJobImportArchive(parameters);
                JobSendTrace($"Import job completed for file {file.Name} with result: {(archive != null ? "Success" : "Failure")}", ETraceLevel.INFO);
                if (archive is null) continue;

                var volumeDir = archiveService.CreateVolumeDirectory(volume, library);

                var dest = Path.Combine(volumeDir.FullName, archive.Name);
                JobSendTrace($"Moving archive file {archive.FullName} to final destination {dest}", ETraceLevel.INFO);
                File.Copy(archive.FullName, dest, overwrite: true);
                JobSendTrace($"Moving Done", ETraceLevel.INFO);

                issue.CbzFilename = archive.Name;
                issue.FileSizeBytes = (int)archive.Length;
                issue.Status = IssueStatus.DOWNLOADED;
                volume.CountOfDownloadedIssues = await ctx.Issues.CountAsync(i => i.VolumeId == volume.Id && i.Status == IssueStatus.DOWNLOADED, ct);
                volume.UpdatedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync(ct);
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Issue>(issue.Id));
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));
                JobSendTrace($"Successfully imported file {file.Name} as issue {issue.Title} (issue number {issue.IssueNumber})", ETraceLevel.INFO);
                job.Progress.Increment(true);
                job.CallbackHandler.Callback(job.Progress);
            }
        }
        catch (Exception ex)
        {
            JobSendTrace($"Erreur non gérée lors de l'import: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
            return;
        }

        if (tempDir.Exists)
            tempDir.Delete(recursive: true);
        JobSendTrace($"Import process completed for directory {importDirectory}", ETraceLevel.INFO);
        EndJob(true);


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

        var sourcefile = File.Exists(parameters.SourceFile) ? new FileInfo(parameters.SourceFile) : null;
        if (sourcefile == null)
        {
            return null;
        }

        var job = StartJob($"Transform {sourcefile.Name} to archive", parameters);
        job.SetState(JobState.RUNNING);

        try
        {
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
            var archive = await archiveService.CreateCbzFile(parameters.WorkingPath, parameters.Volume, parameters.Issue, comicsInfo, pageFiles, job.CallbackHandler);

            // STEP 4 : Delete files
            comicsInfo.Delete();
            pageFiles.ForEach(c => c.Delete());

            EndJob(job.Progress.Error < job.Progress.Total);

            return archive;
        }
        catch (Exception ex)
        {
            JobSendTrace($"Erreur non gérée lors de l'import: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
            return null;
        }



    }

    private static readonly string[] _archiveExtensions = ["*.cbz", "*.cbr", "*.pdf"];



    #endregion
}
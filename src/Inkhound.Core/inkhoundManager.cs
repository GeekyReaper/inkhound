using Inkhound.Core.ComicVine;
using Inkhound.Core.Models;
using Foundation.Core.Model;
using Foundation.Core;

using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Inkhound.Core.DbStorage;
using Inkhound.Core.ComicArchiveGenerator;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using SharpCompress.Compressors.ZStandard.Unsafe;

namespace Inkhound.Core;

public class InkhoundManager : BaseServiceManager
{
    private readonly string _dbPath;



    //public InkhoundDbContext? Database { get; private set; }

    public InkhoundManager(string dbPath = "data/inkhound.db")
    {
        _dbPath = dbPath;

        //var options = new ComicVineOptions { ApiKey = "ff3e0b9ffa62b7c50563beee41c1075dc3616fbd" };
        //comicVine = new ComicVineService(options);

    }

    // Checks if the SQLite database exists and creates it if needed, then sets the Database property



    public StateServiceManager GetCurrentState() => CurrentState;

    public List<string> GetServiceNames()
        => [.. Services.Values.Select(s => s.GetServiceName())];

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

    // Search volumes by name via ComicVine and map results to a Page<Volume>
    public async Task<Page<Volume>> AutomaticSearchVolumeAsync(
        string name,
        int pageNumber = 1,
        int? pageSize = null,
        CancellationToken ct = default)
    {
        var comicVine = GetService<ComicVineService, ComicVineOptions>();
        if (comicVine.CurrentState.State != EState.OK)
        {
            throw new InvalidOperationException("ComicVine service is not available");
        }


        var response = await comicVine.AutomaticSearchVolumesAsync(name, pageNumber, pageSize, ct: ct);

        var items = response.Results.Select(cv => new Volume
        {
            SourceId = cv.Id.ToString(),
            SourceType = "ComicVine",
            Title = cv.Name,
        });

        return new Page<Volume>
        {
            Items = [.. items],
            PageNumber = pageNumber,
            PageSize = response.Limit,
            TotalItems = response.NumberOfTotalResults,
        };
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
        return new Issue
        {
            ComicVineId = cvIssue.Id.ToString(),
            IssueNumber = cvIssue.IssueNumber,
            Title = cvIssue.Name,
            Year = cvIssue.CoverDate != null && DateTime.TryParse(cvIssue.CoverDate, out var d) ? d.Year : null,
            Description = cvIssue.Description,
            Image = cvIssue.Image is { } img ? new VolumeImage(img.IconUrl, img.MediumUrl, img.ScreenUrl, img.ScreenLargeUrl, img.SmallUrl, img.SuperUrl, img.ThumbUrl, img.TinyUrl, img.OriginalUrl, img.ImageTags) : null,
            Authors = cvIssue.PersonCredits?
                .Select(p => new VolumeAuthor(p.Name, p.Role))
                .ToList() ?? [],
        };
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

        var sourcefile = File.Exists(parameters.SourceFile) ? new FileInfo(parameters.SourceFile) : archiveService.getFileFromImportPath(parameters.SourceFile);
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
        var archive = await archiveService.CreateCbzFile(parameters.Volume, parameters.Issue, comicsInfo, pageFiles, job.CallbackHandler);

        // STEP 4 : Delete files
        comicsInfo.Delete();
        pageFiles.ForEach(c => c.Delete());

        EndJob(job.Progress.Error < job.Progress.Total);

        return archive;



    }

    // public async Task<FileInfo?> LaunchJobConvertPdfToImage(ArchiveConverterPdfJobParameters parameters)
    // {
    //     var archiveService = GetService<ArchiveService, ArchiveOption>();

    //     if (archiveService.CurrentState.State != EState.OK)
    //     {
    //         throw new InvalidOperationException("ArchiveService is not available");
    //     }
    //     var job = StartJob<ArchiveConverterPdfJobParameters>($"PDF to Image - File {parameters.SourceFile}", parameters);

    //     job.SetState(JobState.RUNNING);

    //     // STEP 1 : Convert from PDF
    //     var pageFiles = await archiveService.ConvertPdfToImage(parameters.SourceFile, parameters.WorkingPath, job.CallbackHandler);
    //     if (pageFiles == null)
    //     {
    //         EndJob(false);
    //         return null;
    //     }

    //     // STEP 2 : Add ComicsInfo
    //     var comicsInfo = await archiveService.CreateComicInfo(parameters.Volume, parameters.Issue, parameters.WorkingPath, job.CallbackHandler);

    //     // STEP 3 : Generage CBZ
    //     var archive = await archiveService.CreateCbzFile(parameters.Volume, parameters.Issue, comicsInfo, pageFiles, job.CallbackHandler);


    //     EndJob(job.Progress.Error < job.Progress.Total);

    //     return archive;
    // }
}

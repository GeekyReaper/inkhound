using Inkhound.Core.ComicVine;
using Inkhound.Core.Models;
using Foundation.Core.Model;
using Foundation.Core;

using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Inkhound.Core.DbStorage;
using Inkhound.Core.ComicArchiveGenerator;

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
    public async Task<Page<Volume>> SearchVolumeAsync(
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


        var response = await comicVine.SearchVolumesAsync(name, pageNumber, pageSize, ct: ct);

        var items = response.Results.Select(cv => new Volume
        {
            SourceId = cv.Id.ToString(),
            SourceType = "ComicVine",
            Title = cv.Name,
            Year = cv.StartYear != null && int.TryParse(cv.StartYear, out var y) ? y : null,
            Description = cv.Description,
            ImageUrl = cv.Image?.MediumUrl,
            Publisher = cv.Publisher?.Name,
        });

        return new Page<Volume>
        {
            Items = [.. items],
            PageNumber = pageNumber,
            PageSize = response.Limit,
            TotalItems = response.NumberOfTotalResults,
        };
    }

    public async Task<List<FileInfo>?> LaunchJobConvertPdfToImage(ArchiveConverterPdfJobParameters parameters)
    {
        var archiveService = GetService<ArchiveService, ArchiveOption>();
        if (archiveService.CurrentState.State != EState.OK)
        {
            throw new InvalidOperationException("ArchiveService is not available");
        }
        var job = StartJob<ArchiveConverterPdfJobParameters>($"PDF to Image - File {parameters.SourcePath}", parameters);

        job.SetState(JobState.RUNNING);

        // STEP 1 : Convert from PDF
        var result = await archiveService.ConvertPdfToImage(parameters.SourcePath, parameters.WorkingPath, job.CallbackHandler);
        if (result == null)
        {
            EndJob(false);
            return result;
        }

        // STEP 2 : Add ComicsInfo

        EndJob(job.Progress.Error < job.Progress.Total);

        return result;
    }
}

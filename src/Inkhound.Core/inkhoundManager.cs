using Inkhound.Core.ComicVine;
using Inkhound.Core.Data;
using Inkhound.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Inkhound.Core;

public class inkhoundManager
{
    private readonly string _dbPath;

    public ComicVineService comicVine { get; private set; }
    public InkhoundDbContext? Database { get; private set; }

    public inkhoundManager(string dbPath = "data/inkhound.db")
    {
        _dbPath = dbPath;

        var options = new ComicVineOptions { ApiKey = "ff3e0b9ffa62b7c50563beee41c1075dc3616fbd" };



        comicVine = new ComicVineService(options);
        InitSQL();
    }

    // Checks if the SQLite database exists and creates it if needed, then sets the Database property
    public void InitSQL()
    {
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var options = new DbContextOptionsBuilder<InkhoundDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        Database = new InkhoundDbContext(options);
        Database.Database.EnsureCreated();
    }

    // Search volumes by name via ComicVine and map results to a Page<Volume>
    public async Task<Page<Volume>> SearchVolumeAsync(
        string name,
        int pageNumber = 1,
        int? pageSize = null,
        CancellationToken ct = default)
    {


        var response = await comicVine.SearchVolumesAsync(name, pageNumber, pageSize, ct: ct);

        var items = response.Results.Select(cv => new Volume
        {
            ComicVineId = cv.Id.ToString(),
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
}

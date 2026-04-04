using Inkhound.Core.ComicVine;
using Inkhound.Core.Entities;

namespace Inkhound.Core;

public class inkhoundManager
{
    public ComicVineService comicVine { get; private set; }

    public inkhoundManager()
    {
        var options = new ComicVineOptions { ApiKey = "ff3e0b9ffa62b7c50563beee41c1075dc3616fbd" };

        var http = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout     = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
        http.DefaultRequestHeaders.Add("User-Agent", "Inkhound/1.0");

        comicVine = new ComicVineService(http, options);
    }

    // Search volumes by name via ComicVine and map results to a Page<Volume>
    public async Task<Page<Volume>> SearchVolumeAsync(
        string            name,
        int               pageNumber = 1,
        int?              pageSize   = null,
        CancellationToken ct         = default)
    {
              

        var response = await comicVine.SearchVolumesAsync(name, pageNumber, pageSize,  ct: ct);

        var items = response.Results.Select(cv => new Volume
        {
            ComicVineId = cv.Id.ToString(),
            Title       = cv.Name,
            Year        = cv.StartYear != null && int.TryParse(cv.StartYear, out var y) ? y : null,
            Description = cv.Description,
            ImageUrl    = cv.Image?.MediumUrl,
            Publisher   = cv.Publisher?.Name,
        });

        return new Page<Volume>
        {
            Items      = [..items],
            PageNumber = pageNumber,
            PageSize   = response.Limit,
            TotalItems = response.NumberOfTotalResults,
        };
    }
}


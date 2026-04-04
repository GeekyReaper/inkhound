using System.Runtime.CompilerServices;
using Inkhound.Core.ComicVine;

var options = new ComicVineOptions { ApiKey = "ff3e0b9ffa62b7c50563beee41c1075dc3616fbd" };

var http = new HttpClient
{
    BaseAddress = new Uri(options.BaseUrl),   // <- BaseUrl utilisé ici
    Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
};
http.DefaultRequestHeaders.Add("User-Agent", "Inkhound/1.0");



var service = new ComicVineService(http, options);

int page = 1;
int pagesize = 50;
bool stop = false;

while (!stop)

{
    var publisher = await service.GetPublishersPageAsync(pagesize, (page - 1) * pagesize, PublisherSortField.Name, SortDirection.Asc);

    Console.WriteLine($"Page {page} of {publisher.NumberOfTotalResults}");

    foreach (var p in publisher.Results)
    {
        Console.WriteLine($"- {p.Id} {p.Name} ({p.LocationCity}, {p.LocationState}) {p.Deck}");
    }

    page++;

    Console.Write("Continue ? (y/n) : ");
    var input = Console.ReadLine();
    stop = input == null || input.ToLower() != "y";

}
//var search = "";

// Console.Write("Search for a comic series: ");
// search = Console.ReadLine();

// while (search != "quit" && !string.IsNullOrEmpty(search))
// {

//     var r = await service.SearchVolumesAsync(search, 50, 0, VolumeSortField.CountOfIssues, SortDirection.Desc);

//     Console.WriteLine($"Found {r.NumberOfTotalResults} volumes:");
//     foreach (var v in r.Results)
//     {
//         Console.WriteLine($"- {v.Id} {v.Name} ({v.StartYear}) {v.CountOfIssues} issues Publisher: {v.Publisher?.Name}");
//     }

//     Console.Write("Search for Issues : ");

//     var idVolume = int.Parse(Console.ReadLine());
//     var volume = await service.GetVolumeAsync(idVolume);
//     Console.WriteLine($"- {volume.Id} {volume.Description} {volume.Deck} {volume.SiteDetailUrl}");
//     var issues = await service.GetAllIssuesForVolumeAsync(idVolume);

//     Console.WriteLine($"Found {issues.Count} issues:");
//     foreach (var i in issues)
//     {
//         Console.WriteLine($"- {i.Id}  {i.Name} ({i.IssueNumber}) {i.CoverDate} {i.StoreDate} {i.SiteDetailUrl}");
//     }

//     Console.Write("Search another comic series : ");
//     search = Console.ReadLine();

// }


Console.WriteLine("Hello, World!");

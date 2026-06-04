using System.Runtime.CompilerServices;
using Inkhound.Core;
using Inkhound.Core.ComicVine;

using Inkhound.Core.Models;
using Inkhound;
using Inkhound.Core.ComicArchiveGenerator;
using Microsoft.VisualBasic;
using Inkhound.Core.Blob;
using System.Reflection.Metadata;


// Console.WriteLine("Connect Access");
// var access = new BlobAccess();
// Console.Write("Protocol : 1>LOCAL 2>SMB");
// var strprotocol = Console.ReadLine();
// switch (strprotocol)
// {
//     case "1":
//         access.Protocol = EBlobProtocol.LOCAL;
//         break;
//     case "2":
//         access.Protocol = EBlobProtocol.SMB;
//         break;
//     default:
//         access.Protocol = EBlobProtocol.LOCAL;
//         break;
// }

// Console.Write("Host : ");
// access.Host = Console.ReadLine();
// Console.Write("username : ");
// access.Username = Console.ReadLine();
// Console.Write("password : ");
// access.Password = Console.ReadLine();
// Console.Write("path : ");
// access.Path = Console.ReadLine();

// var blobservice = new BlobService(access);

// var t = await blobservice.Init();

// Console.WriteLine($"STATUS : {blobservice.Status} ({blobservice.StatusMessage})");

// var directory = await blobservice.GetDirectoriesAsync();
// Console.WriteLine("== Directory");
// foreach (var d in directory)
// {
//     Console.WriteLine($"{d.FullPath}");
// }

// var files = await blobservice.GetFilesAsync();
// Console.WriteLine("== Files");
// foreach (var f in files)
// {
//     Console.WriteLine($"{f.FullPath}");
// }



var options = new ComicVineOptions { ApiKey = "ff3e0b9ffa62b7c50563beee41c1075dc3616fbd" };

var manager = new InkhoundManager();

bool stop = false;
while (!stop)
{
    Console.WriteLine("Connect Inkhound Manager");
    Console.WriteLine("1. Automatic load services");
    Console.WriteLine("2. Attach events");

    Console.WriteLine("3. Search Volume");
    Console.WriteLine("4. Search Volume and issue");
    Console.WriteLine("0. Exit");
    Console.Write("Choose an option : ");
    var option = int.TryParse(Console.ReadLine(), out int result) ? result : 0;

    if (option == 1)
    {
        await manager.AutomaticLoadServices();
        Console.WriteLine("Services loaded.");
    }

    if (option == 2)
    {
        manager.OnHealthcheck = (state) =>
        {
            Console.WriteLine($"Healthcheck: {state.GlobalState}");
            foreach (var service in state.stateServices)
            {
                Console.WriteLine($"- {service.ServiceName}: {service.State} (Last refresh: {service.LastRefresh})");
            }
        };
        manager.OnTrace = (message) =>
        {
            Console.WriteLine(message.ToConsole());
        };

        manager.OnJobUpdated = (job) =>
        {
            Console.WriteLine($"Job update: {job.Title} - {job.State} ({job.Progress.Percentage}%)  Duration: {job.Duration.TotalSeconds}s Progression: {job.Progress.Completed}/{job.Progress.Total} Errors: {job.Progress.Error}");
        };
    }
    if (option == 3)
    {
        var comicvineService = manager.GetService<ComicVineService, ComicVineOptions>();
        var stop2 = false;
        while (!stop2)
        {
            Console.Write("Volume name : ");
            var volumename = Console.ReadLine();

            stop2 = volumename == "q";

            if (!stop2)
            {
                var result2 = await comicvineService.AutomaticSearchVolume(volumename, "FR");
                if (result2 != null)
                {
                    Console.WriteLine($"Volume Indentified \r\n{result2}");
                    Console.Write("ImportPath : ");
                    var importfile = Console.ReadLine();

                    var findissue = await comicvineService.FindVolume(importfile, "FR", result2);
                    if (findissue.Issue != null && findissue.Volume != null)
                    {
                        Console.Write("WorkingPath : ");
                        var workingpath = Console.ReadLine();
                        // var param = new ArchiveConverterPdfJobParameters() { Issue = InkhoundManager.Map(findissue.Issue), Volume = InkhoundManager.Map(findissue.Volume), SourceFile = importfile, WorkingPath = workingpath };
                        // var archive = manager.GetService<ArchiveService, ArchiveOption>();
                        // var f = await manager.LaunchJobImportArchive(param);
                        // if (f != null)
                        // {
                        //     Console.WriteLine($"{f.FullName} - size : {f.Length}");
                        // }

                    }




                }

                Console.WriteLine($"{result2}");

                Console.WriteLine("");
            }
        }



    }
    if (option == 0)
    {
        stop = true;
    }
    if (option == 4)
    {
        Console.Write("Issue file : ");
        var issuefile = Console.ReadLine();

        var comicvineService = manager.GetService<ComicVineService, ComicVineOptions>();
        var result2 = await comicvineService.FindVolume(issuefile, "FR");
        if (result2.Volume != null)
        {
            Console.WriteLine($"Volume Indentified \r\n{result2.Volume}");
            if (result2.Issue != null)
            {
                Console.WriteLine($"Issue Indentified \r\n{result2.Issue}");
            }
        }
        else
        {
            Console.WriteLine("No volume found");
        }

    }

}





//var result = await manager.GetService<ComicVineService, ComicVineOptions>().FindVolume("Bouncer/Tome 04 - La Vengeance du manchot.pdf", "FR");
//Console.WriteLine($"{result.Volume?.Name} ({result.Volume?.Publisher}) - {result.Volume?.StartYear}");
//Console.WriteLine($"\t{result.Issue?.IssueNumber} ({result.Issue?.Name})");


//var result1 = manager.LaunchJobConvertPdfToImage(new ArchiveConverterPdfJobParameters() { SourcePath = "Les Aigles decapitees T08.pdf", WorkingPath = "Les Aigles decapitees T08" });
//var result2 = manager.LaunchJobConvertPdfToImage(new ArchiveConverterPdfJobParameters() { SourcePath = "002 - Elfes T02 L'Honneur des Elfes sylvains.pdf", WorkingPath = "Elfes T02" });

//var archive = manager.GetService<ArchiveService, ArchiveOption>();
//var images = await archive.ConvertPdfToImage("Les Aigles decapitees T08.pdf", "Les Aigles decapitees T08");
//var images = result1.Result;
//var images2 = result2.Result;
// Console.WriteLine($"Converted {images.Count} images:");
// foreach (var img in images)
// {
//     Console.WriteLine($"- {img}");
// }


// while (!stop)
// {

//     var publisher = await service.GetPublishersPageAsync(pagesize, (page - 1) * pagesize, PublisherSortField.Name, SortDirection.Asc);

//     Console.WriteLine($"Page {page} of {publisher.NumberOfTotalResults}");

//     foreach (var p in publisher.Results)
//     {
//         Console.WriteLine($"- {p.Id} {p.Name} ({p.LocationCity}, {p.LocationState}) {p.Deck}");
//     }

//     page++;

//     Console.Write("Continue ? (y/n) : ");
//     var input = Console.ReadLine();
//     stop = input == null || input.ToLower() != "y";

// }
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



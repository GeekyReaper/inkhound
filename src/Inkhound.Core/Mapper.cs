using Inkhound.Core.Bedetheque;
using Inkhound.Core.ComicVine;
using Inkhound.Core.Models;

namespace Inkhound.Core;

public static class Mapper
{
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
            Issues = cvVolume.Issues?.Select(c => c.Name).ToList(),
            CountOfIssues = cvVolume.CountOfIssues
        };
    }

    public static Issue Map(CvIssue cvIssue)
    {
        int.TryParse(cvIssue.IssueNumber, out var issueNum);
        return new Issue
        {
            SourceId = cvIssue.Id.ToString(),
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

    public static Volume Map(BdSerie serie)
    {
        int.TryParse(serie.AnneeDebut, out var year);
        return new Volume
        {
            SourceId = serie.Id.ToString(),
            SourceType = "bedetheque",
            Title = serie.Titre,
            Year = year == 0 ? null : year,
            Description = serie.Description,
            Image = serie.CoverUrl is { } url ? new VolumeImage(url, url, url, url, url, url, url, url, url, null) : null,
            Genres = serie.Genre is { } g ? [g] : [],
            CountOfIssues = serie.NombreAlbums ?? serie.Albums.Count,
        };
    }

    public static Issue Map(BdAlbum album)
    {
        int.TryParse(album.NumeroAlbum, out var issueNum);
        int.TryParse(album.Annee, out var year);
        return new Issue
        {
            SourceId = album.Id.ToString(),
            IssueNumber = issueNum,
            Title = album.Titre,
            Year = year == 0 ? null : year,
            Description = album.Description,
            Image = album.CoverUrl is { } url ? new VolumeImage(url, url, url, url, url, url, url, url, url, null) : null,
            Authors = album.Auteurs.Select(a => new VolumeAuthor(a.Nom, a.Role ?? string.Empty)).ToList(),
        };
    }
}

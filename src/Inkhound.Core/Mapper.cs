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
}

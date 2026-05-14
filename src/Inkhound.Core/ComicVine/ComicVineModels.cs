namespace Inkhound.Core.ComicVine;

// Sort field for volume search — maps to ComicVine API field names
public enum VolumeSortField { Name, StartYear, CountOfIssues, DateAdded, DateLastUpdated }

// Sort field for publisher search — maps to ComicVine API field names
public enum PublisherSortField { Name, DateAdded, DateLastUpdated }

// Sort direction
public enum SortDirection { Asc, Desc }

// Generic list envelope — used by /api/volumes/ and /api/issues/
public record CvPagedResponse<T>(
    string Error,
    int StatusCode,
    int Limit,
    int Offset,
    int NumberOfPageResults,
    int NumberOfTotalResults,
    IReadOnlyList<T> Results
);

// Generic detail envelope — used by /api/volume/4050-{id}/ and /api/issue/4000-{id}/
public record CvDetailResponse<T>(
    string Error,
    int StatusCode,
    T? Results
);

// Shared sub-objects
public record CvPublisher(int Id, string Name, string ApiDetailUrl);
public record CvImage(
    string? IconUrl,
    string? MediumUrl,
    string? ScreenUrl,
    string? ScreenLargeUrl,
    string? SmallUrl,
    string? SuperUrl,
    string? ThumbUrl,
    string? TinyUrl,
    string? OriginalUrl,
    string? ImageTags
);
public record CvVolumeRef(int Id, string Name, string ApiDetailUrl);

// Lightweight issue reference — returned inside a volume's `issues` field
public record CvIssueRef(int Id, string Name, string ApiDetailUrl);

// Person credit — same as CvCredit but with a role field ("writer", "penciller", "inker", etc.)
public record CvPersonCredit(int Id, string Name, string? Role);

// Lightweight volume — returned by /search
public record CvVolumeStub(
    int Id,
    string Name,
    string? StartYear,
    int CountOfIssues,
    string? Description,
    CvPublisher? Publisher,
    CvImage? Image,
    CvIssueRef? FirstIssue,
    CvIssueRef? LastIssue,
    string? SiteDetailUrl);

// Volume — returned by detail endpoint (fields match VolumeDetailFieldList)
public record CvVolume(
    int Id,
    string Name,
    string? StartYear,
    int CountOfIssues,
    CvPublisher? Publisher,
    CvImage? Image,
    string? Deck,
    string? Description,
    string SiteDetailUrl,
    DateTime? DateAdded,
    DateTime? DateLastUpdated,
    IReadOnlyList<CvIssueRef>? Issues,
    IReadOnlyList<CvPersonCredit>? People
);

// Publisher — returned by list search and detail endpoint
public record CvPublisherDetail(
    int Id,
    string Name,
    CvImage? Image,
    string? Deck,               // short tagline
    string? Description,        // full HTML description
    string? LocationCity,
    string? LocationState,
    string ApiDetailUrl,
    string SiteDetailUrl
);

// Result of FindVolume — best matching volume and its corresponding issue
public record CvFindResult(CvVolume? Volume, CvIssue? Issue);

// Issue — returned by list (no person_credits) and detail (with person_credits)
public record CvIssue(
    int Id,
    string? Name,
    string IssueNumber,         // string in API: "1", "1.5", "Annual 1"
    CvVolumeRef Volume,
    string? CoverDate,          // "YYYY-MM-DD"
    string? StoreDate,          // "YYYY-MM-DD"
    string? Description,
    CvImage? Image,
    string ApiDetailUrl,
    string SiteDetailUrl,
    IReadOnlyList<CvPersonCredit>? PersonCredits  // null in list responses
);

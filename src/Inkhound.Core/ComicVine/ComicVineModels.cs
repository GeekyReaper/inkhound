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
public record CvImage(string MediumUrl, string OriginalUrl);
public record CvVolumeRef(int Id, string Name, string ApiDetailUrl);

// Person credit on an issue (role = "writer", "penciller", "inker", etc.)
public record CvPersonCredit(int Id, string Name, string Role);

// Volume — returned by list search and detail endpoint
public record CvVolume(
    int Id,
    string Name,
    string? StartYear,          // string in API, may be null for ongoing series
    int CountOfIssues,
    CvPublisher? Publisher,
    CvImage? Image,
    string? Deck,               // short tagline
    string? Description,        // full HTML description
    string ApiDetailUrl,
    string SiteDetailUrl
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

namespace Inkhound.Core.ComicVine;

public class ComicVineOptions
{
    public const string SectionName = "ComicVine";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://comicvine.gamespot.com/api/";
    public int TimeoutSeconds { get; set; } = 30;

    public int pageSize { get; set; } = 20;
}

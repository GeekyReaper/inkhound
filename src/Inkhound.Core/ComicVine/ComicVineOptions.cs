using Inkhound.Core.Models;

namespace Inkhound.Core.ComicVine;

public class ComicVineOptions
{
    public const string SectionName = "ComicVine";
    public string ApiKey { get; set; } = string.Empty;

    public string UserAgent { get; set; } = "Inkhound/1.0";

    public string BaseUrl { get; set; } = "https://comicvine.gamespot.com/api/";
    public int TimeoutSeconds { get; set; } = 30;

    public int PageSize { get; set; } = 20;

    public string ServiceName => ComicVineService.ServiceName;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
            errors.Add("ApiKey is required.");

        if (string.IsNullOrWhiteSpace(BaseUrl))
            errors.Add("BaseUrl is required.");

        if (TimeoutSeconds <= 0)
            errors.Add("TimeoutSeconds must be greater than 0.");

        if (PageSize <= 0 || PageSize > 100)
            errors.Add("PageSize must be between 1 and 100.");

        return errors.Count == 0;
    }


    public static List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new OptionDefinition { Name = "ApiKey", ValueType = OptionValueType.Password, DefaultValue = string.Empty, Mandatory = true },
            new OptionDefinition { Name = "UserAgent", ValueType = OptionValueType.String, DefaultValue = "Inkhound/1.0", Mandatory = false },
            new OptionDefinition { Name = "BaseUrl", ValueType = OptionValueType.String, DefaultValue = "https://comicvine.gamespot.com/api/", Mandatory = true },
            new OptionDefinition { Name = "TimeoutSeconds", ValueType = OptionValueType.Int, DefaultValue = "30", Mandatory = false },
            new OptionDefinition { Name = "PageSize", ValueType = OptionValueType.Int, DefaultValue = "20", Mandatory = false }
        };
    }

    public static ComicVineOptions SetOptions(List<OptionDefinition> options)
    {
        var comicVineOptions = new ComicVineOptions();
        foreach (var option in options)
        {
            switch (option.Name)
            {
                case "ApiKey":
                    comicVineOptions.ApiKey = option.Value;
                    break;
                case "UserAgent":
                    comicVineOptions.UserAgent = option.Value;
                    break;
                case "BaseUrl":
                    comicVineOptions.BaseUrl = option.Value;
                    break;
                case "TimeoutSeconds":
                    comicVineOptions.TimeoutSeconds = option.GetInt();
                    break;
                case "PageSize":
                    comicVineOptions.PageSize = option.GetInt();
                    break;
            }
        }
        return comicVineOptions;
    }
}

using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.ComicVine;

public class ComicVineOptions : IOptionList
{
    public const string SectionName = "ComicVine";
    public string ApiKey { get; set; } = string.Empty;

    public string UserAgent { get; set; } = "Inkhound/1.0";

    public string BaseUrl { get; set; } = "https://comicvine.gamespot.com/api/";
    public int TimeoutSeconds { get; set; } = 30;

    public int PageSize { get; set; } = 20;

    public int RateLimitMs { get; set; } = 250;

    public bool UseProxy { get; set; } = false;

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

        if (RateLimitMs <= 0)
            errors.Add("RateLimitMs must be greater than 0.");

        return errors.Count == 0;
    }


    public ComicVineOptions()
    {

    }

    public List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new OptionDefinition { Name = "ApiKey", Section = "Connection", SortOrder = 0, Value = ApiKey, ValueType = EValueType.PASSWORD, DefaultValue = string.Empty, Mandatory = true },
            new OptionDefinition { Name = "UserAgent", Section = "Connection", SortOrder = 10, ValueType = EValueType.STRING, Value = UserAgent, DefaultValue = "Inkhound/1.0", Mandatory = false },
            new OptionDefinition { Name = "BaseUrl", Section = "Connection", SortOrder = 20, ValueType =EValueType.STRING, Value = BaseUrl, DefaultValue = "https://comicvine.gamespot.com/api/", RegexValidator = @"^https://.*$", Mandatory = true },
            new OptionDefinition { Name = "TimeoutSeconds", Section = "Connection", SortOrder = 30, ValueType = EValueType.INT, Value = TimeoutSeconds.ToString(), DefaultValue = "30", Mandatory = false },
            new OptionDefinition { Name = "PageSize", Section = "Search Behavior", SortOrder = 40, ValueType = EValueType.INT, Value = PageSize.ToString(), DefaultValue = "20", Mandatory = false },
            new OptionDefinition { Name = "RateLimitMs", Section = "Search Behavior", SortOrder = 50, ValueType = EValueType.INT, Value = RateLimitMs.ToString(), DefaultValue = "250", Mandatory = false },
            new OptionDefinition { Name = "UseProxy", Section = "Connection", SortOrder = 60, Value = UseProxy.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "false", Description = "Route HTTP requests through the active Webshare proxy, if available.", Mandatory = false }
        };
    }



    public bool LoadOptions(List<OptionDefinition> options, out List<string> errors)
    {
        errors = new List<string>();

        foreach (var option in options)
        {
            if (option.IsValid(out var optionErrors))
            {
                switch (option.Name)
                {
                    case "ApiKey":
                        ApiKey = option.Value;
                        break;
                    case "UserAgent":
                        UserAgent = option.Value;
                        break;
                    case "BaseUrl":
                        BaseUrl = option.Value;
                        break;
                    case "TimeoutSeconds":
                        TimeoutSeconds = option.GetInt();
                        break;
                    case "PageSize":
                        PageSize = option.GetInt();
                        break;
                    case "RateLimitMs":
                        RateLimitMs = option.GetInt();
                        break;
                    case "UseProxy":
                        UseProxy = option.GetBool();
                        break;
                }
            }
            errors.AddRange(optionErrors);
        }
        return errors.Count == 0 && !IsValid(out errors);
    }

}

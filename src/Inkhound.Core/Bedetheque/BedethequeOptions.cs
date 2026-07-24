using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.Bedetheque;

public class BedethequeOptions : IOptionList
{
    public const string SectionName = "Bedetheque";

    public string UserAgent { get; set; } = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public string BaseUrl { get; set; } = "https://www.bedetheque.com";
    public int TimeoutSeconds { get; set; } = 30;

    public int RateLimitMs { get; set; } = 800;

    public int MaxParallelRequests { get; set; } = 3;

    public bool UseProxy { get; set; } = false;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(BaseUrl))
            errors.Add("BaseUrl is required.");

        if (TimeoutSeconds <= 0)
            errors.Add("TimeoutSeconds must be greater than 0.");

        if (RateLimitMs <= 0)
            errors.Add("RateLimitMs must be greater than 0.");

        if (MaxParallelRequests <= 0)
            errors.Add("MaxParallelRequests must be greater than 0.");

        return errors.Count == 0;
    }

    public BedethequeOptions()
    {

    }

    public List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new OptionDefinition { Name = "UserAgent", Section = "Connection", SortOrder = 0, ValueType = EValueType.STRING, Value = UserAgent, DefaultValue = UserAgent, Mandatory = false },
            new OptionDefinition { Name = "BaseUrl", Section = "Connection", SortOrder = 10, ValueType = EValueType.STRING, Value = BaseUrl, DefaultValue = "https://www.bedetheque.com", RegexValidator = @"^https?://.*$", Mandatory = true },
            new OptionDefinition { Name = "TimeoutSeconds", Section = "Connection", SortOrder = 20, ValueType = EValueType.INT, Value = TimeoutSeconds.ToString(), DefaultValue = "30", Mandatory = false },
            new OptionDefinition { Name = "RateLimitMs", Section = "Search Behavior", SortOrder = 30, ValueType = EValueType.INT, Value = RateLimitMs.ToString(), DefaultValue = "800", Description = "Bedetheque.com is scraped (no public API) and sensitive to automated traffic — keep this conservative.", Mandatory = false },
            new OptionDefinition { Name = "MaxParallelRequests", Section = "Search Behavior", SortOrder = 40, ValueType = EValueType.INT, Value = MaxParallelRequests.ToString(), DefaultValue = "3", Mandatory = false },
            new OptionDefinition { Name = "UseProxy", Section = "Connection", SortOrder = 50, Value = UseProxy.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "false", Description = "Route HTTP requests through the active Webshare proxy, if available.", Mandatory = false }
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
                    case "UserAgent":
                        UserAgent = option.Value;
                        break;
                    case "BaseUrl":
                        BaseUrl = option.Value;
                        break;
                    case "TimeoutSeconds":
                        TimeoutSeconds = option.GetInt();
                        break;
                    case "RateLimitMs":
                        RateLimitMs = option.GetInt();
                        break;
                    case "MaxParallelRequests":
                        MaxParallelRequests = option.GetInt();
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

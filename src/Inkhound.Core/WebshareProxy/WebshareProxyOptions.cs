using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.WebshareProxy;

public class WebshareProxyOptions : IOptionList
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://proxy.webshare.io/api/v2";
    public int TimeoutSeconds { get; set; } = 30;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
            errors.Add("ApiKey is required.");

        if (string.IsNullOrWhiteSpace(BaseUrl))
            errors.Add("BaseUrl is required.");

        if (TimeoutSeconds <= 0)
            errors.Add("TimeoutSeconds must be greater than 0.");

        return errors.Count == 0;
    }

    public List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new() { Name = nameof(ApiKey), Section = "Connection", SortOrder = 0, Value = ApiKey, ValueType = EValueType.PASSWORD, DefaultValue = string.Empty, Description = "API key from Webshare.io → Dashboard → API.", Mandatory = true },
            new() { Name = nameof(BaseUrl), Section = "Connection", SortOrder = 10, Value = BaseUrl, ValueType = EValueType.STRING, DefaultValue = "https://proxy.webshare.io/api/v2", RegexValidator = @"^https://.*$", Description = "Base URL of the Webshare API.", Mandatory = true },
            new() { Name = nameof(TimeoutSeconds), Section = "Connection", SortOrder = 20, Value = TimeoutSeconds.ToString(), ValueType = EValueType.INT, DefaultValue = "30", Description = "HTTP request timeout in seconds.", Mandatory = false }
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
                    case nameof(ApiKey): ApiKey = option.Value; break;
                    case nameof(BaseUrl): BaseUrl = option.Value; break;
                    case nameof(TimeoutSeconds): TimeoutSeconds = option.GetInt(); break;
                }
            }
            else
            {
                errors.AddRange(optionErrors);
            }
        }

        return errors.Count == 0;
    }
}

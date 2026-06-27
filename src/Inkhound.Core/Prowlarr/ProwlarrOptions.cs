using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.Prowlarr;

public class ProwlarrOptions : IOptionList
{
    public string BaseUrl { get; set; } = "http://localhost:9696";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(BaseUrl))
            errors.Add("BaseUrl is required.");

        if (string.IsNullOrWhiteSpace(ApiKey))
            errors.Add("ApiKey is required.");

        if (TimeoutSeconds <= 0)
            errors.Add("TimeoutSeconds must be greater than 0.");

        return errors.Count == 0;
    }

    public List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new OptionDefinition { Name = nameof(BaseUrl), Value = BaseUrl, ValueType = EValueType.STRING, DefaultValue = "http://localhost:9696", Description = "Base URL of the Prowlarr instance.", Mandatory = true },
            new OptionDefinition { Name = nameof(ApiKey), Value = ApiKey, ValueType = EValueType.PASSWORD, DefaultValue = string.Empty, Description = "API key from Prowlarr → Settings → General → Security.", Mandatory = true },
            new OptionDefinition { Name = nameof(TimeoutSeconds), Value = TimeoutSeconds.ToString(), ValueType = EValueType.INT, DefaultValue = "30", Description = "HTTP request timeout in seconds.", Mandatory = false }
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
                    case nameof(BaseUrl):
                        BaseUrl = option.Value;
                        break;
                    case nameof(ApiKey):
                        ApiKey = option.Value;
                        break;
                    case nameof(TimeoutSeconds):
                        TimeoutSeconds = option.GetInt();
                        break;
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

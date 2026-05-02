using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.Kavita;

public class KavitaOptions : IOptionList
{
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string ApiKey { get; set; } = string.Empty;
    public string PluginName { get; set; } = "Inkhound";
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
            new OptionDefinition { Name = nameof(BaseUrl), Value = BaseUrl, ValueType = EValueType.STRING, DefaultValue = "http://localhost:5000", Description = "Base URL of the local Kavita instance.", Mandatory = true },
            new OptionDefinition { Name = nameof(ApiKey), Value = ApiKey, ValueType = EValueType.PASSWORD, DefaultValue = string.Empty, Description = "API key from Kavita → User Settings → API Key.", Mandatory = true },
            new OptionDefinition { Name = nameof(PluginName), Value = PluginName, ValueType = EValueType.STRING, DefaultValue = "Inkhound", Description = "Plugin name sent to Kavita during authentication.", Mandatory = false },
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
                    case nameof(PluginName):
                        PluginName = option.Value;
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

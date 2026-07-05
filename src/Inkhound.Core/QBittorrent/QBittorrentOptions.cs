using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.QBittorrent;

public class QBittorrentOptions : IOptionList
{
    public string BaseUrl { get; set; } = "http://localhost:8080";
    public string ApiKey { get; set; } = string.Empty;
    public string DefaultCategory { get; set; } = string.Empty;
    public string DefaultSavePath { get; set; } = string.Empty;
    public bool AddPaused { get; set; } = false;
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
            new() { Name = nameof(BaseUrl), Value = BaseUrl, ValueType = EValueType.STRING, DefaultValue = "http://localhost:8080", Description = "Base URL of the QBittorrent Web UI.", Mandatory = true },
            new() { Name = nameof(ApiKey), Value = ApiKey, ValueType = EValueType.PASSWORD, DefaultValue = string.Empty, Description = "API key from QBittorrent → Preferences → Web UI → API Key.", Mandatory = true },
            new() { Name = nameof(DefaultCategory), Value = DefaultCategory, ValueType = EValueType.STRING, DefaultValue = string.Empty, Description = "Default QBittorrent category applied to downloaded torrents (optional).", Mandatory = false },
            new() { Name = nameof(DefaultSavePath), Value = DefaultSavePath, ValueType = EValueType.PATH, DefaultValue = string.Empty, Description = "Default save path for downloaded torrents (optional, overrides category path).", Mandatory = false },
            new() { Name = nameof(AddPaused), Value = AddPaused.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "false", Description = "Add torrents in paused state without starting the download immediately.", Mandatory = false },
            new() { Name = nameof(TimeoutSeconds), Value = TimeoutSeconds.ToString(), ValueType = EValueType.INT, DefaultValue = "30", Description = "HTTP request timeout in seconds.", Mandatory = false }
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
                    case nameof(BaseUrl): BaseUrl = option.Value; break;
                    case nameof(ApiKey): ApiKey = option.Value; break;
                    case nameof(DefaultCategory): DefaultCategory = option.Value; break;
                    case nameof(DefaultSavePath): DefaultSavePath = option.Value; break;
                    case nameof(AddPaused): AddPaused = option.GetBool(); break;
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

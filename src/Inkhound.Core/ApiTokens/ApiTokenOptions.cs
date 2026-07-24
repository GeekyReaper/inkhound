using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.ApiTokens;

public class ApiTokenOptions : IOptionList
{
    public bool Enabled { get; set; } = false;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        return true;
    }

    public List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new() { Name = nameof(Enabled), Section = "General", SortOrder = 0, Value = Enabled.ToString(), ValueType = EValueType.BOOL, DefaultValue = "False", Description = "Enable API token management — shows the API Tokens page and allows authenticating requests via the X-Api-Key header.", Mandatory = false }
        };
    }

    public bool LoadOptions(List<OptionDefinition> options, out List<string> errors)
    {
        errors = new List<string>();

        foreach (var option in options)
        {
            if (option.Name == nameof(Enabled))
                Enabled = option.GetBool();
        }

        return true;
    }
}

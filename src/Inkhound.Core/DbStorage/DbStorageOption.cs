using System;
using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.DbStorage;

public class DbStorageOption : IOptionList
{
    public string Path { get; set; } = "./data/dbstorage.db";
    public bool UseInMemory { get; set; } = false;

    public DbStorageOption()
    {

    }



    public List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new OptionDefinition
            {
                Name = "Path",
                Section = "Storage",
                SortOrder = 0,
                Value = Path,
                ValueType = EValueType.STRING,
                DefaultValue = "/data/dbstorage.db",
                Description = "Path to the database file.",
                Mandatory = true
          },
            new OptionDefinition
            {
                Name = "UseInMemory",
                Section = "Storage",
                SortOrder = 10,
                Value = UseInMemory.ToString(),
                ValueType = EValueType.BOOL,
                DefaultValue = "false",
                Mandatory = false,
                Description = "Use in-memory storage instead of file-based storage."
            }
        };
    }

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Path) && !UseInMemory)
        {
            errors.Add("ConnectionString is required when not using in-memory storage.");
        }

        return errors.Count == 0;
    }

    public bool LoadOptions(List<OptionDefinition> options, out List<string> errors)
    {
        errors = new List<string>();

        foreach (var option in options)
        {
            if (option.IsValid(out var optionserrors))
            {
                switch (option.Name)
                {
                    case "Path":
                        Path = option.Value;
                        break;
                    case "UseInMemory":
                        UseInMemory = option.GetBool();

                        break;
                    default:
                        errors.Add($"Unknown option: {option.Name}");
                        break;
                }
            }
            else
            {
                errors.AddRange(optionserrors.Select(e => $"{option.Name}: {e}"));
            }

        }

        return errors.Count == 0;
    }

}

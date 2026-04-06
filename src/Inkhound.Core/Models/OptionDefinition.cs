using System;

namespace Inkhound.Core.Models;

public enum OptionValueType { String, Int, Bool, Password }
public class OptionDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public OptionValueType ValueType { get; set; } = OptionValueType.String;

    public string DefaultValue { get; set; } = string.Empty;
    public DateTime LastValue { get; set; } = DateTime.MinValue;
    public bool Mandatory { get; set; } = false;

    public int GetInt() => int.TryParse(Value, out var result) ? result : 0;
    public bool GetBool() => bool.TryParse(Value, out var result) && result;

    public string ServiceName { get; set; } = string.Empty;


}

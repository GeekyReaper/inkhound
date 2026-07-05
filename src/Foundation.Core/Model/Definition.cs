using System;

namespace Foundation.Core.Model;


public enum EValueType { STRING, INT, DOUBLE, BOOL, PASSWORD, TEXT, PATH }
public enum ETraceLevel { INFO, DEBUG, WARNING, ERROR, CRITICAL, NONE }
public class OptionDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public EValueType ValueType { get; set; } = EValueType.STRING;
    public bool Mandatory { get; set; } = true;

    public string Description { get; set; } = string.Empty;

    public string RegexValidator { get; set; } = string.Empty;

    public string DefaultValue { get; set; } = string.Empty;

    public int GetInt() => int.TryParse(Value, out var result) ? result : 0;
    public bool GetBool() => bool.TryParse(Value, out var result) && result;

    public DateTime LastValue { get; set; } = DateTime.MinValue;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();

        if (Mandatory && string.IsNullOrWhiteSpace(Value))
            errors.Add($"{Name} is required.");

        if (ValueType == EValueType.INT)
        {
            if (!int.TryParse(Value, out _))
                errors.Add($"{Name} must be a valid integer.");
        }
        else if (ValueType == EValueType.BOOL)
        {
            if (!bool.TryParse(Value, out _))
                errors.Add($"{Name} must be a valid boolean.");
        }
        else if (ValueType == EValueType.DOUBLE)
        {
            if (!double.TryParse(Value, out _))
                errors.Add($"{Name} must be a valid double.");
        }

        if (!string.IsNullOrWhiteSpace(RegexValidator) && !System.Text.RegularExpressions.Regex.IsMatch(Value, RegexValidator))
            errors.Add($"{Name} does not match the required format.");

        return errors.Count == 0;
    }

    public string ServiceName { get; set; } = "Unknow";

}

public class TraceDefinition
{
    public List<string> Message { get; } = new List<string>();
    public DateTime Date { get; } = DateTime.UtcNow;
    public string ServiceName { get; set; } = string.Empty;

    public Guid? JobId { get; set; } = null;

    public ETraceLevel Level { get; set; } = ETraceLevel.NONE;

    public string ToConsole()
    {
        if (JobId.HasValue)
        {
            return $"[{Date:yyyy-MM-dd HH:mm:ss}] [{Level}] {ServiceName}\r\n\t(Job: {JobId}): {string.Join("\r\n\t-- ", Message)}";
        }
        else
        {
            return $"[{Date:yyyy-MM-dd HH:mm:ss}] [{Level}] {ServiceName}: {string.Join("\r\n-- ", Message)}";
        }
    }

}




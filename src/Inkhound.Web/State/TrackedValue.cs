namespace Inkhound.Web.State;

/// <summary>DTO sent inside each StatePatch.</summary>
public record TrackedValue(object? Value, DateTime UpdatedAt);

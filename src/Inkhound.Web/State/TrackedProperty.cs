namespace Inkhound.Web.State;

/// <summary>
/// Wraps a value of any type T with its last-modified timestamp.
/// Works for primitives (string, int, bool) and complex objects or collections.
/// </summary>
public class TrackedProperty<T>
{
    public T        Value     { get; private set; }
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    public TrackedProperty(T initialValue) => Value = initialValue;

    /// <summary>
    /// Replaces the entire value and updates the timestamp.
    /// Use for primitives and for complex objects when rebuilding the full object.
    /// </summary>
    public void Set(T newValue)
    {
        Value     = newValue;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Mutates one or more internal properties of the existing instance
    /// and updates the timestamp. Avoids rebuilding the whole object.
    /// Use for complex types (classes, lists).
    /// </summary>
    public void Mutate(Action<T> mutator)
    {
        mutator(Value);
        UpdatedAt = DateTime.UtcNow;
    }

    public TrackedValue ToDto() => new(Value, UpdatedAt);
}

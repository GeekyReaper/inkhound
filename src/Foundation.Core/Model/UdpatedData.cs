using System.Runtime.CompilerServices;

namespace Foundation.Core.Model;

public class UpdatedData
{
    public string DataType { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static UpdatedData CreateUpdatedData<T>(Guid id) where T : class
    {
        return new UpdatedData
        {
            DataType = typeof(T).FullName ?? typeof(T).Name,
            Id = id,
            UpdatedAt = DateTime.UtcNow
        };
    }

}


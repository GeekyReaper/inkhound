namespace Inkhound.Web.Auth;

public interface IUserStore
{
    Task<IReadOnlyList<UserRecord>> GetAllAsync();
    Task<UserRecord?> GetByIdAsync(string id);
    Task<UserRecord?> GetByLoginAsync(string login);
    Task<UserRecord> CreateAsync(CreateUserRequest request);
    Task<UserRecord> UpdateAsync(string id, UpdateUserRequest request);
    Task DeleteAsync(string id);
    Task<UserRecord?> ValidateAsync(string login, string password);
}

public record CreateUserRequest(string Login, string Password, string Role);
public record UpdateUserRequest(string? Login, string? Password, string? Role);

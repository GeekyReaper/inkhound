using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Inkhound.Web.Auth;

public class FileUserStore : IUserStore
{
    private readonly string _filePath;
    private readonly ReaderWriterLockSlim _lock = new();
    private List<UserRecord> _users = [];

    public FileUserStore(IConfiguration config)
    {
        _filePath = config["Auth:UsersFile"] ?? "data/system/users.json";
        Load();
    }

    // ── Read ────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<UserRecord>> GetAllAsync()
    {
        _lock.EnterReadLock();
        try { return Task.FromResult<IReadOnlyList<UserRecord>>(_users.AsReadOnly()); }
        finally { _lock.ExitReadLock(); }
    }

    public Task<UserRecord?> GetByIdAsync(string id)
    {
        _lock.EnterReadLock();
        try { return Task.FromResult(_users.FirstOrDefault(u => u.Id == id)); }
        finally { _lock.ExitReadLock(); }
    }

    public Task<UserRecord?> GetByLoginAsync(string login)
    {
        _lock.EnterReadLock();
        try { return Task.FromResult(_users.FirstOrDefault(u => u.Login == login)); }
        finally { _lock.ExitReadLock(); }
    }

    // ── Mutations ───────────────────────────────────────────────────────────────

    public async Task<UserRecord> CreateAsync(CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Login))
            throw new ArgumentException("Login is required.");
        if (string.IsNullOrWhiteSpace(request.Password))
            throw new ArgumentException("Password is required.");
        if (request.Role is not ("admin" or "guest"))
            throw new ArgumentException("Role must be 'admin' or 'guest'.");

        _lock.EnterWriteLock();
        try
        {
            if (_users.Any(u => u.Login == request.Login))
                throw new InvalidOperationException($"Login '{request.Login}' is already taken.");

            var record = new UserRecord(
                Id:           Guid.NewGuid().ToString(),
                Login:        request.Login,
                PasswordHash: HashPassword(request.Password),
                Role:         request.Role
            );
            _users.Add(record);
            await PersistAsync();
            return record;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public async Task<UserRecord> UpdateAsync(string id, UpdateUserRequest request)
    {
        _lock.EnterWriteLock();
        try
        {
            var idx = _users.FindIndex(u => u.Id == id);
            if (idx < 0) throw new KeyNotFoundException($"User '{id}' not found.");

            var existing = _users[idx];

            if (request.Login is not null && request.Login != existing.Login &&
                _users.Any(u => u.Login == request.Login))
                throw new InvalidOperationException($"Login '{request.Login}' is already taken.");

            if (request.Role is not null && request.Role is not ("admin" or "guest"))
                throw new ArgumentException("Role must be 'admin' or 'guest'.");

            var newHash = request.Password is not null
                ? HashPassword(request.Password)
                : existing.PasswordHash;

            var updated = existing with
            {
                Login        = request.Login  ?? existing.Login,
                PasswordHash = newHash,
                Role         = request.Role   ?? existing.Role
            };
            _users[idx] = updated;
            await PersistAsync();
            return updated;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public async Task DeleteAsync(string id)
    {
        _lock.EnterWriteLock();
        try
        {
            var idx = _users.FindIndex(u => u.Id == id);
            if (idx < 0) throw new KeyNotFoundException($"User '{id}' not found.");
            _users.RemoveAt(idx);
            await PersistAsync();
        }
        finally { _lock.ExitWriteLock(); }
    }

    public Task<UserRecord?> ValidateAsync(string login, string password)
    {
        _lock.EnterReadLock();
        try
        {
            var user = _users.FirstOrDefault(u => u.Login == login);
            if (user is null || !VerifyPassword(password, user.PasswordHash))
                return Task.FromResult<UserRecord?>(null);
            return Task.FromResult<UserRecord?>(user);
        }
        finally { _lock.ExitReadLock(); }
    }

    // ── Persistence ─────────────────────────────────────────────────────────────

    private void Load()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (!File.Exists(_filePath))
        {
            // Create default admin account
            _users =
            [
                new UserRecord(
                    Id:           Guid.NewGuid().ToString(),
                    Login:        "admin",
                    PasswordHash: HashPassword("admin"),
                    Role:         "admin"
                )
            ];
            PersistAsync().GetAwaiter().GetResult();
            return;
        }

        var json = File.ReadAllText(_filePath);
        _users = JsonSerializer.Deserialize<List<UserRecord>>(json) ?? [];
    }

    private async Task PersistAsync()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = _filePath + ".tmp";
        var json = JsonSerializer.Serialize(_users, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(tmp, json);
        File.Move(tmp, _filePath, overwrite: true);
    }

    // ── Password helpers ────────────────────────────────────────────────────────

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations:  100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32
        );
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations:  100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32
        );
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

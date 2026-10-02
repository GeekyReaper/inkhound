namespace Inkhound.Core.Models;

public class User
{
    public Guid Id { get; set; }
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Rôle applicatif — voir <see cref="Inkhound.Core.Security.UserRoles"/>. Défaut : admin.</summary>
    public string Role { get; set; } = Inkhound.Core.Security.UserRoles.Admin;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

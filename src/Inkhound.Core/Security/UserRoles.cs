namespace Inkhound.Core.Security;

/// <summary>
/// Rôles applicatifs. <see cref="Admin"/> accède à tout ; <see cref="Guest"/> n'accède ni aux pages de
/// configuration (sections Settings / Access / Links du menu) ni aux API correspondantes. Les valeurs
/// sont celles du claim JWT « role » et de la colonne <c>Users.Role</c>.
/// </summary>
public static class UserRoles
{
    public const string Admin = "admin";
    public const string Guest = "guest";

    public static bool IsValid(string? role) => role is Admin or Guest;
}

namespace Inkhound.Web.Auth;

public record UserRecord(string Id, string Login, string PasswordHash, string Role);

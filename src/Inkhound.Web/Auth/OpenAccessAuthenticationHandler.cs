using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Inkhound.Web.Auth;

// Authentifie systématiquement avec succès une identité virtuelle (login="guest", role="admin"),
// non persistée. Sélectionné par le scheme "Smart" (Program.cs) uniquement tant qu'aucun utilisateur
// réel n'existe en base (InkhoundManager.HasUsers == false) — voir docs/project.md / plan "mode
// bootstrap ouvert".
public class OpenAccessAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString()),
            new Claim(ClaimTypes.Name, "guest"),
            new Claim(ClaimTypes.Role, "admin")
        }, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

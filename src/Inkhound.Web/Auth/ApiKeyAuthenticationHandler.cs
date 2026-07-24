using System.Security.Claims;
using System.Text.Encodings.Web;
using Inkhound.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Inkhound.Web.Auth;

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions { }

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    InkhoundManager manager)
    : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var value))
            return AuthenticateResult.NoResult();

        var token = await manager.ValidateApiTokenAsync(value.ToString());
        if (token is null)
            return AuthenticateResult.Fail("Invalid, expired or disabled API token.");

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, token.Name),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim("token_id", token.Id.ToString())
        }, Scheme.Name);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VersionController : ControllerBase
{
    // GET /api/version — APP_VERSION est injectée par le Dockerfile (ARG/ENV, alimentée par
    // scripts/docker-release.ps1) ; absente en local (dotnet watch run), d'où le repli "debug".
    [HttpGet]
    public IActionResult Get() =>
        Ok(new { version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "debug" });
}

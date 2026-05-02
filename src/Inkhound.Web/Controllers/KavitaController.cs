using Inkhound.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/kavita")]
[Authorize(Roles = "admin")]
public class KavitaController(InkhoundManager manager) : ControllerBase
{
    private record KavitaLibraryDto(int Id, string Name, int Type, DateTime LastScanned);

    // GET /api/kavita/libraries?refresh=false
    [HttpGet("libraries")]
    public async Task<IActionResult> GetLibraries([FromQuery] bool refresh = false)
    {
        var libraries = await manager.GetKavitaLibrariesAsync(refresh);
        return Ok(libraries.Select(l => new KavitaLibraryDto(l.Id, l.Name, l.Type, l.LastScanned)));
    }

    // POST /api/kavita/libraries/{id}/scan?force=false
    [HttpPost("libraries/{id:int}/scan")]
    public async Task<IActionResult> ScanLibrary(int id, [FromQuery] bool force = false)
    {
        var success = await manager.ScanKavitaLibraryAsync(id, force);
        return success ? NoContent() : StatusCode(503, new { message = "Kavita scan request failed." });
    }
}

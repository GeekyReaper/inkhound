using Inkhound.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

/// <summary>
/// Observation et purge de l'empreinte mémoire du process (page Settings &gt; System).
/// </summary>
[ApiController]
[Route("api/system")]
[Authorize(Roles = "admin")]
public class SystemController(InkhoundManager manager) : ControllerBase
{
    // GET /api/system/memory — instantané (lecture pure, aucune collecte déclenchée).
    [HttpGet("memory")]
    public IActionResult GetMemory() => Ok(manager.GetMemorySnapshot());

    // POST /api/system/memory/compact — vide les caches applicatifs puis force une collecte
    // compactante. Bloquant de l'ordre de la seconde : action manuelle uniquement.
    [HttpPost("memory/compact")]
    public async Task<IActionResult> CompactMemory() => Ok(await manager.CompactMemoryAsync());
}

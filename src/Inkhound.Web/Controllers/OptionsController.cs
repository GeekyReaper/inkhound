using Inkhound.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/options")]
[Authorize(Roles = "admin")]
public class OptionsController(InkhoundManager manager) : ControllerBase
{
    // GET /api/options
    [HttpGet]
    public IActionResult GetServices()
        => Ok(manager.GetServiceNames());

    // GET /api/options/{serviceName}
    [HttpGet("{serviceName}")]
    public IActionResult Get(string serviceName)
    {
        var options = manager.GetOptionsForService(serviceName);
        if (options.Count == 0)
            return NotFound(new { message = $"No options found for service '{serviceName}'." });
        return Ok(options);
    }

    // PUT /api/options/{serviceName}
    // Body: { "OptionName": "value", ... }
    [HttpPut("{serviceName}")]
    public async Task<IActionResult> Update(string serviceName, [FromBody] Dictionary<string, string> updates)
    {
        var success = await manager.UpdateOptionsForService(serviceName, updates);
        if (!success)
            return StatusCode(503, new { message = "Database service unavailable." });
        return NoContent();
    }
}

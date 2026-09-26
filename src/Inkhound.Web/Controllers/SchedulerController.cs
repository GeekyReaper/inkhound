using Inkhound.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

/// <summary>
/// Configuration et déclenchement manuel du planificateur de jobs récurrents (service <c>Scheduler</c>).
/// L'exécution est entièrement backend (boucle cron dans <see cref="InkhoundManager"/>) — ces routes
/// ne servent qu'à lire l'état et à modifier la configuration.
/// </summary>
[ApiController]
[Route("api/scheduler")]
[Authorize(Roles = "admin")]
public class SchedulerController(InkhoundManager manager) : ControllerBase
{
    /// <summary>Corps de <c>PUT /api/scheduler</c> — nouvelle configuration des cinq tâches.</summary>
    public record SchedulerConfigRequest(
        bool ProcessDownloadsEnabled, string ProcessDownloadsCron,
        bool RollingRefreshEnabled, string RollingRefreshCron, int RollingRefreshBatchSize,
        bool AutoSearchEnabled, string AutoSearchCron, int AutoSearchBatchSize, int AutoSearchMinScore,
        bool BedethequeCatalogEnabled, string BedethequeCatalogCron, int BedethequeCatalogLetterCount,
        bool NewsEnabled = false, string? NewsCron = null, int NewsEnrichBatchSize = 5);

    // GET /api/scheduler — état courant (config + dernier / prochain déclenchement).
    [HttpGet]
    public IActionResult Get() => Ok(manager.GetSchedulerStatus());

    // PUT /api/scheduler — met à jour la configuration ; prise en compte à chaud au tick suivant.
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] SchedulerConfigRequest request)
    {
        var processCron = (request.ProcessDownloadsCron ?? string.Empty).Trim();
        var rollingCron = (request.RollingRefreshCron ?? string.Empty).Trim();
        var autoSearchCron = (request.AutoSearchCron ?? string.Empty).Trim();
        var catalogCron = (request.BedethequeCatalogCron ?? string.Empty).Trim();
        var newsCron = (request.NewsCron ?? string.Empty).Trim();

        if (request.ProcessDownloadsEnabled && !InkhoundManager.IsValidCronExpression(processCron))
            return BadRequest(new { message = "Import downloads: invalid 5-field cron expression." });

        if (request.RollingRefreshEnabled && !InkhoundManager.IsValidCronExpression(rollingCron))
            return BadRequest(new { message = "Rolling refresh: invalid 5-field cron expression." });

        if (request.RollingRefreshEnabled && request.RollingRefreshBatchSize < 1)
            return BadRequest(new { message = "Rolling refresh: batch size must be at least 1." });

        if (request.AutoSearchEnabled && !InkhoundManager.IsValidCronExpression(autoSearchCron))
            return BadRequest(new { message = "Auto search: invalid 5-field cron expression." });

        if (request.AutoSearchEnabled && request.AutoSearchBatchSize < 1)
            return BadRequest(new { message = "Auto search: batch size must be at least 1." });

        if (request.AutoSearchMinScore is < 0 or > 100)
            return BadRequest(new { message = "Auto search: minimum score must be between 0 and 100." });

        if (request.BedethequeCatalogEnabled && !InkhoundManager.IsValidCronExpression(catalogCron))
            return BadRequest(new { message = "Bedetheque catalog: invalid 5-field cron expression." });

        if (request.BedethequeCatalogEnabled && request.BedethequeCatalogLetterCount < 1)
            return BadRequest(new { message = "Bedetheque catalog: letters per run must be at least 1." });

        if (request.NewsEnabled && !InkhoundManager.IsValidCronExpression(newsCron))
            return BadRequest(new { message = "News: invalid 5-field cron expression." });

        if (request.NewsEnrichBatchSize < 0)
            return BadRequest(new { message = "News: albums enriched per run must be at least 0." });

        var updates = new Dictionary<string, string>
        {
            ["ProcessDownloadsEnabled"]    = request.ProcessDownloadsEnabled.ToString().ToLowerInvariant(),
            ["ProcessDownloadsCron"]       = processCron,
            ["RollingRefreshEnabled"]      = request.RollingRefreshEnabled.ToString().ToLowerInvariant(),
            ["RollingRefreshCron"]         = rollingCron,
            ["RollingRefreshBatchSize"]    = request.RollingRefreshBatchSize.ToString(),
            ["AutoSearchEnabled"]          = request.AutoSearchEnabled.ToString().ToLowerInvariant(),
            ["AutoSearchCron"]             = autoSearchCron,
            ["AutoSearchBatchSize"]        = request.AutoSearchBatchSize.ToString(),
            ["AutoSearchMinScore"]         = request.AutoSearchMinScore.ToString(),
            ["BedethequeCatalogEnabled"]   = request.BedethequeCatalogEnabled.ToString().ToLowerInvariant(),
            ["BedethequeCatalogCron"]      = catalogCron,
            ["BedethequeCatalogLetterCount"] = request.BedethequeCatalogLetterCount.ToString(),
            ["NewsEnabled"]                = request.NewsEnabled.ToString().ToLowerInvariant(),
            ["NewsEnrichBatchSize"]        = request.NewsEnrichBatchSize.ToString()
        };

        if (!string.IsNullOrEmpty(newsCron))
            updates["NewsCron"] = newsCron;

        var success = await manager.UpdateOptionsForService("Scheduler", updates);
        if (!success)
            return StatusCode(503, new { message = "Database service unavailable." });

        return Ok(manager.GetSchedulerStatus());
    }

    // POST /api/scheduler/run/{key} — déclenche immédiatement une tâche (bouton « Run now »).
    [HttpPost("run/{key}")]
    public IActionResult RunNow(string key)
    {
        try
        {
            manager.RunSchedulerTaskNow(key);
            return Accepted(new { message = $"Scheduler task '{key}' triggered." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

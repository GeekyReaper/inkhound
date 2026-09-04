using Inkhound.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JobsController(InkhoundManager manager) : ControllerBase
{
    // GET /api/jobs/{jobId} — filet de rattrapage pour un client ayant manqué le
    // ManagerJobChanged terminal (SignalR déconnecté pendant l'exécution du job, ex: app mobile
    // mise en arrière-plan). Renvoie le JobContext brut, même forme que celle diffusée par AppHub
    // via SignalR, pour que le frontend le fusionne via le même chemin que l'event temps réel.
    // 404 si le job n'a jamais existé ou si sa fenêtre de rétention (JobRetention) est dépassée —
    // les deux cas sont indistinguables (pas de registre permanent).
    [HttpGet("{jobId:guid}")]
    public IActionResult GetStatus(Guid jobId)
    {
        var job = manager.TryGetJob(jobId);
        return job is null ? NotFound() : Ok(job);
    }
}

using Inkhound.Core;
using Inkhound.Core.Export;
using Inkhound.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

/// <summary>
/// Module Export : lancement des jobs (issue → PDF/CBZ, volume → ZIP), liste et suppression des
/// fichiers produits, et téléchargement par ticket. Le navigateur n'envoie pas le JWT sur un lien
/// natif : l'UI demande d'abord un ticket (authentifié), puis ouvre l'URL de téléchargement, seule
/// route anonyme du contrôleur — le ticket (aléatoire, 60 s, usage unique) tient lieu d'autorisation.
/// </summary>
[ApiController]
[Authorize]
public class ExportController(InkhoundManager manager) : ControllerBase
{
    public record ExportRequest(ExportFormat Format);

    // POST /api/issues/{issueId}/export — job d'export d'une issue ; renvoie { jobId }.
    [HttpPost("/api/issues/{issueId:guid}/export")]
    public async Task<IActionResult> ExportIssue(Guid issueId, [FromBody] ExportRequest request)
    {
        var job = await manager.LaunchJobExport(new ExportJobParameters
        { TargetType = ExportTargetType.Issue, TargetId = issueId, Format = request.Format });
        return Accepted(new { jobId = job.JobId });
    }

    // POST /api/volumes/{volumeId}/export — job d'export d'un volume (ZIP de ses issues) ; renvoie { jobId }.
    [HttpPost("/api/volumes/{volumeId:guid}/export")]
    public async Task<IActionResult> ExportVolume(Guid volumeId, [FromBody] ExportRequest request)
    {
        var job = await manager.LaunchJobExport(new ExportJobParameters
        { TargetType = ExportTargetType.Volume, TargetId = volumeId, Format = request.Format });
        return Accepted(new { jobId = job.JobId });
    }

    // GET /api/exports?targetType=Issue&targetId=… — exports encore présents pour une cible.
    [HttpGet("/api/exports")]
    public async Task<IActionResult> List([FromQuery] ExportTargetType targetType, [FromQuery] Guid targetId)
        => Ok(await manager.GetExportsAsync(targetType, targetId));

    // DELETE /api/exports/{id} — supprime le fichier et son entrée.
    [HttpDelete("/api/exports/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await manager.DeleteExportAsync(id);
        return NoContent();
    }

    // POST /api/exports/{id}/ticket — ticket à usage unique ; renvoie l'URL de téléchargement.
    [HttpPost("/api/exports/{id:guid}/ticket")]
    public async Task<IActionResult> CreateTicket(Guid id)
    {
        var ticket = await manager.CreateExportTicketAsync(id);
        return Ok(new { url = $"/api/exports/download/{ticket}" });
    }

    // GET /api/exports/download/{ticket} — sert le fichier (Range supporté). Anonyme : le ticket
    // est consommé à l'appel ; un ticket inconnu, expiré ou déjà utilisé donne 404.
    [AllowAnonymous]
    [HttpGet("/api/exports/download/{ticket}")]
    public async Task<IActionResult> Download(string ticket)
    {
        var file = await manager.OpenExportByTicketAsync(ticket);
        if (file is null) return NotFound(new { message = "Download link expired or already used." });

        return PhysicalFile(file.Value.Path, file.Value.ContentType, file.Value.FileName, enableRangeProcessing: true);
    }
}

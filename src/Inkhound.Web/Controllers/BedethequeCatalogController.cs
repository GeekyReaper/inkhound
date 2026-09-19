using Inkhound.Core;
using Inkhound.Core.Bedetheque.Catalog;
using Inkhound.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

/// <summary>
/// Catalogue local des séries Bedetheque (page <c>/settings/bedetheque</c>) : état par lettre et
/// lancement du job de rafraîchissement. La recherche multi-source Bedetheque ne renvoie rien tant
/// que ce catalogue n'a pas été chargé au moins une fois.
/// </summary>
[ApiController]
[Route("api/bedetheque/catalog")]
[Authorize(Roles = "admin")]
public class BedethequeCatalogController(InkhoundManager manager) : ControllerBase
{
    private record LetterStatusDto(string Letter, int Count, DateTime? FetchedAtUtc);

    private record CatalogStatusDto(
        bool Loaded, int TotalSeries, DateTime? OldestFetchUtc, DateTime? NewestFetchUtc,
        bool RefreshRunning, IEnumerable<LetterStatusDto> Letters);

    private static CatalogStatusDto ToDto(BedethequeCatalogStatus s) =>
        new(s.Loaded, s.TotalSeries, s.OldestFetchUtc, s.NewestFetchUtc, s.RefreshRunning,
            s.Letters.Select(l => new LetterStatusDto(l.Letter, l.Count, l.FetchedAtUtc)));

    /// <summary>
    /// Corps de <c>POST /api/bedetheque/catalog/refresh</c> : <c>Letters</c> explicites, sinon
    /// <c>LetterCount</c> lettres par rotation (les plus anciennes d'abord), sinon toutes.
    /// </summary>
    public record RefreshCatalogRequest(int? LetterCount = null, string[]? Letters = null);

    // GET /api/bedetheque/catalog — état du catalogue (totaux + 27 lettres).
    [HttpGet]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            return Ok(ToDto(await manager.GetBedethequeCatalogStatusAsync()));
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    // POST /api/bedetheque/catalog/refresh — lance le job de rafraîchissement ; 409 si un
    // rafraîchissement est déjà en cours.
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshCatalogRequest? request)
    {
        var parameters = new RefreshBedethequeCatalogJobParameters
        {
            LetterCount = request?.LetterCount,
            Letters = request?.Letters?.ToList()
        };
        if (!parameters.IsValid(out var errors))
            return BadRequest(new { message = string.Join(" ", errors) });

        try
        {
            var job = await manager.LaunchJobRefreshBedethequeCatalog(parameters);
            return Accepted(new { jobId = job.JobId });
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}

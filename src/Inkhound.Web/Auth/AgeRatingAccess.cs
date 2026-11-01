using System.Security.Claims;
using Inkhound.Core;
using Inkhound.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Inkhound.Web.Auth;

/// <summary>Plafond de classification d'âge de l'utilisateur courant (claim posé à chaque requête depuis la base).</summary>
public static class AgeRatingAccess
{
    public const string ClaimType = "max_age_rating";

    /// <summary><c>null</c> = aucune limite (admin, tokens API, mode ouvert, Guest sans plafond).</summary>
    public static AgeRating? GetMaxAgeRating(this ClaimsPrincipal user)
        => Enum.TryParse<AgeRating>(user.FindFirst(ClaimType)?.Value, out var max) ? max : null;
}

/// <summary>
/// Garde globale : pour un utilisateur plafonné, toute route adressée par <c>volumeId</c> ou <c>issueId</c>
/// dont le volume dépasse le plafond répond 404 — comme s'il n'existait pas. Les listes et agrégats sont
/// filtrés dans <see cref="InkhoundManager"/> ; ce filtre ferme l'accès direct par identifiant.
/// </summary>
public class AgeRatingAccessFilter(InkhoundManager manager) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.User.GetMaxAgeRating() is { } max)
        {
            var values = context.RouteData.Values;
            var ct = context.HttpContext.RequestAborted;
            var denied =
                (values.TryGetValue("volumeId", out var v) && Guid.TryParse(v?.ToString(), out var volumeId)
                    && !await manager.IsVolumeAccessibleAsync(volumeId, max, ct))
                || (values.TryGetValue("issueId", out var i) && Guid.TryParse(i?.ToString(), out var issueId)
                    && !await manager.IsIssueAccessibleAsync(issueId, max, ct));
            if (denied)
            {
                context.Result = new NotFoundResult();
                return;
            }
        }
        await next();
    }
}

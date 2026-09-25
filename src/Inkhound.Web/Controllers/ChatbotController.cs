using Inkhound.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

/// <summary>
/// État et pilotage manuel du module Chatbot (bot Matrix). La configuration elle-même passe par
/// <c>/api/options</c> comme pour tout autre module ; ces routes ne servent qu'à consulter l'état
/// d'exécution et à démarrer/arrêter ponctuellement la boucle de synchronisation.
/// </summary>
[ApiController]
[Route("api/chatbot")]
[Authorize(Roles = "admin")]
public class ChatbotController(InkhoundManager manager) : ControllerBase
{
    /// <summary>
    /// GET /api/chatbot/status — état d'exécution courant (connexion, room, compteurs, usage du
    /// modèle vision). Tout est en mémoire : rien n'est persisté, les compteurs repartent de zéro
    /// à chaque redémarrage.
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus() => Ok(manager.GetChatbotStatus());

    /// <summary>
    /// POST /api/chatbot/start — démarre la boucle sans modifier l'option <c>Enabled</c> : c'est un
    /// contrôle ponctuel, et une sauvegarde ultérieure des options réappliquera la valeur persistée.
    /// </summary>
    [HttpPost("start")]
    public async Task<IActionResult> Start()
    {
        await manager.StartChatbotAsync();
        return Ok(manager.GetChatbotStatus());
    }

    /// <inheritdoc cref="Start"/>
    [HttpPost("stop")]
    public async Task<IActionResult> Stop()
    {
        await manager.StopChatbotAsync();
        return Ok(manager.GetChatbotStatus());
    }
}

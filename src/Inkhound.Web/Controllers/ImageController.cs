using Inkhound.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/images")]
[Authorize]
public class ImageController(InkhoundManager manager) : ControllerBase
{
    private static readonly HashSet<string> AllowedTypes = ["image/jpeg", "image/png", "image/webp"];

    // POST /api/images
    [HttpPost]
    [RequestSizeLimit(5_242_880)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (!AllowedTypes.Contains(file.ContentType))
            return BadRequest(new { message = "Type de fichier non autorisé. Formats acceptés : JPEG, PNG, WebP." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        await using var stream = file.OpenReadStream();
        var url = await manager.UploadImageAsync(stream, ext, ct);
        return Ok(new { url });
    }
}

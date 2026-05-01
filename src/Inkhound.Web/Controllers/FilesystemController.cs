using Inkhound.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/filesystem")]
[Authorize(Roles = "admin")]
public class FilesystemController(InkhoundManager manager) : ControllerBase
{
    private record DirectoryDto(
        string Name,
        string FullPath,
        string? Parent,
        DateTime CreatedAt,
        DateTime ModifiedAt
    );

    private record FileDto(
        string Name,
        string FullPath,
        string Extension,
        long SizeBytes,
        DateTime CreatedAt,
        DateTime ModifiedAt
    );

    private static DirectoryDto ToDto(DirectoryInfo d) => new(
        d.Name,
        d.FullName,
        d.Parent?.FullName,
        d.CreationTimeUtc,
        d.LastWriteTimeUtc
    );

    private static FileDto ToDto(FileInfo f) => new(
        f.Name,
        f.FullName,
        f.Extension,
        f.Length,
        f.CreationTimeUtc,
        f.LastWriteTimeUtc
    );

    // GET /api/filesystem/directories?path={path}
    [HttpGet("directories")]
    public async Task<IActionResult> GetDirectories([FromQuery] string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest(new { message = "Query parameter 'path' is required." });

        try
        {
            var dirs = await manager.GetDirectories(path);
            if (dirs.Count == 0 && !Directory.Exists(path))
                return NotFound(new { message = $"Directory '{path}' does not exist." });

            return Ok(dirs.Select(ToDto));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // GET /api/filesystem/files?path={path}&filter={filter}
    [HttpGet("files")]
    public async Task<IActionResult> GetFiles([FromQuery] string? path, [FromQuery] string filter = "*")
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest(new { message = "Query parameter 'path' is required." });

        try
        {
            var files = await manager.GetFiles(path, filter);
            if (files.Count == 0 && !Directory.Exists(path))
                return NotFound(new { message = $"Directory '{path}' does not exist." });

            return Ok(files.Select(ToDto));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }
}

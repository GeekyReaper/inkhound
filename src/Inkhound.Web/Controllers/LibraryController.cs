using Inkhound.Core;
using Inkhound.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/libraries")]
[Authorize(Roles = "admin")]
public class LibraryController(InkhoundManager manager) : ControllerBase
{
    private record LibraryDto(Guid Id, string Name, string Path, int KavitaLibraryId, DateTime CreatedAt);
    public record CreateLibraryRequest(string Name, string Path, int KavitaLibraryId);
    public record UpdateLibraryRequest(string Name, string Path, int KavitaLibraryId);

    private static LibraryDto ToDto(Library l)
        => new(l.Id, l.Name, l.Path, l.KavitaLibraryId, l.CreatedAt);

    // GET /api/libraries
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var libraries = await manager.GetLibrariesAsync();
            return Ok(libraries.Select(ToDto));
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    // GET /api/libraries/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var library = await manager.GetLibraryAsync(id);
            return library is null ? NotFound() : Ok(ToDto(library));
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    // POST /api/libraries
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLibraryRequest request)
    {
        try
        {
            var library = await manager.CreateLibraryAsync(request.Name, request.Path, request.KavitaLibraryId);
            return CreatedAtAction(nameof(GetById), new { id = library.Id }, ToDto(library));
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    // PUT /api/libraries/{id}
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLibraryRequest request)
    {
        try
        {
            var library = await manager.UpdateLibraryAsync(id, request.Name, request.Path, request.KavitaLibraryId);
            return library is null ? NotFound() : Ok(ToDto(library));
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    // DELETE /api/libraries/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var deleted = await manager.DeleteLibraryAsync(id);
            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    public record AddVolumeFromComicVineRequest(int ComicVineVolumeId);
    private record AddedVolumeDto(Guid Id, Guid LibraryId, string SourceId, string Title, int? Year);

    // POST /api/libraries/{id}/volumes
    [HttpPost("{id:guid}/volumes")]
    public async Task<IActionResult> AddVolumeFromComicVine(
        Guid id, [FromBody] AddVolumeFromComicVineRequest request)
    {
        try
        {
            var volume = await manager.AddVolumeFromComicVineAsync(id, request.ComicVineVolumeId);
            return CreatedAtAction(null, new AddedVolumeDto(
                volume.Id, volume.LibraryId, volume.SourceId, volume.Title, volume.Year));
        }
        catch (KeyNotFoundException ex)      { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
                                             { return Conflict(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    // POST /api/libraries/{id}/sync
    [HttpPost("{id:guid}/sync")]
    public IActionResult Synchronize(Guid id)
    {
        _ = manager.LaunchJobSynchronizeLibrary(new SynchronizeLibraryJobParameters { LibraryId = id });
        return Accepted(new { message = $"Synchronization of library {id} started." });
    }
}

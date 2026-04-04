using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Inkhound.Web.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/job")]
[Authorize]
public class JobController(JobRunner jobRunner, IServiceProvider services) : ControllerBase
{
    // GET /api/job — current job state
    [HttpGet]
    public IActionResult GetCurrent() => Ok(jobRunner.Current);

    // POST /api/job/start
    [HttpPost("start")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Start([FromBody] StartJobRequest request)
    {
        // Resolve the handler by job type name
        var handler = services.GetKeyedService<IJobHandler>(request.JobType);
        if (handler is null)
            return NotFound(new { message = $"Unknown job type: '{request.JobType}'." });

        var (isValid, error, title) = await handler.ValidateAsync(request.Parameters);
        if (!isValid)
            return BadRequest(new { message = error });

        var requestedBy = User.FindFirstValue(ClaimTypes.Name)
                       ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                       ?? "unknown";

        var started = await jobRunner.StartAsync(
            jobType:     request.JobType,
            title:       title,
            requestedBy: requestedBy,
            parameters:  request.Parameters,
            work:        handler.RunAsync
        );

        if (!started)
            return Conflict(new
            {
                message = "A job is already running.",
                current = new
                {
                    jobRunner.Current?.JobType,
                    jobRunner.Current?.Title,
                    jobRunner.Current?.Status,
                    jobRunner.Current?.RequestedBy
                }
            });

        return Accepted(new { message = "Job started.", jobId = jobRunner.Current?.Id, title });
    }

    // POST /api/job/cancel
    [HttpPost("cancel")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Cancel()
    {
        if (!jobRunner.IsRunning)
            return BadRequest(new { message = "No job is currently running." });

        await jobRunner.CancelAsync();
        return Accepted(new { message = "Cancellation requested." });
    }
}

public record StartJobRequest(
    string                     JobType,
    Dictionary<string, string> Parameters
);

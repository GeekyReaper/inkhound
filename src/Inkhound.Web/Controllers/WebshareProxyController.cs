using Inkhound.Core;
using Inkhound.Core.WebshareProxy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inkhound.Web.Controllers;

[ApiController]
[Route("api/webshareproxy")]
[Authorize(Roles = "admin")]
public class WebshareProxyController(InkhoundManager manager) : ControllerBase
{
    private record ProxyDto(string Address, int Port, string? CountryCode, bool Valid, bool Failed, bool IsActive);

    private record WebshareProxyActivityDto(int RequestCount, long Bytes, int ErrorCount);

    private record WebshareStatisticsDto(
        string? Email, string? FirstName, string? LastName,
        string? PlanType, string? PlanSubtype, int? ProxyCount, double? BandwidthLimitGb,
        string? Term, DateTime? PeriodStart, DateTime? PeriodEnd,
        int RequestCount, long TotalBytes, int ErrorCount, double AverageDurationSeconds,
        double TotalBytesMb, double TotalBytesGb, double? BandwidthUsagePercent,
        Dictionary<string, WebshareProxyActivityDto>? ByProxy, Dictionary<string, long>? ByDomain);

    private static ProxyDto ToDto(ProxyInfo p, ProxyInfo? current)
        => new(p.Address, p.Port, p.CountryCode, p.Valid, p.Failed,
               current is not null && p.Address == current.Address && p.Port == current.Port);

    private static WebshareStatisticsDto ToDto(WebshareStatistics s)
        => new(
            s.Email, s.FirstName, s.LastName,
            s.PlanType, s.PlanSubtype, s.ProxyCount, s.BandwidthLimitGb,
            s.Term, s.PeriodStart, s.PeriodEnd,
            s.RequestCount, s.TotalBytes, s.ErrorCount, s.AverageDurationSeconds,
            s.TotalBytesMb, s.TotalBytesGb, s.BandwidthUsagePercent,
            s.ByProxy?.ToDictionary(kv => kv.Key, kv => new WebshareProxyActivityDto(kv.Value.RequestCount, kv.Value.Bytes, kv.Value.ErrorCount)),
            s.ByDomain?.ToDictionary(kv => kv.Key, kv => kv.Value));

    // GET /api/webshareproxy/proxies
    [HttpGet("proxies")]
    public IActionResult GetProxies()
    {
        var current = manager.GetCurrentWebshareProxy();
        var proxies = manager.GetWebshareProxies();
        return Ok(proxies.Select(p => ToDto(p, current)));
    }

    // POST /api/webshareproxy/next
    [HttpPost("next")]
    public IActionResult Next()
    {
        var next = manager.RotateWebshareProxy();
        return next is null
            ? StatusCode(503, new { message = "No available proxy to rotate to." })
            : Ok(ToDto(next, next));
    }

    // GET /api/webshareproxy/statistics
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics()
    {
        var stats = await manager.GetWebshareProxyStatisticsAsync();
        return Ok(ToDto(stats));
    }

    // GET /api/webshareproxy/services-using-proxy
    [HttpGet("services-using-proxy")]
    public IActionResult GetServicesUsingProxy()
        => Ok(manager.GetServicesUsingProxy());
}

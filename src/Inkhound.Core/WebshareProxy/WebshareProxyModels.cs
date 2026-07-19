using Foundation.Core.Model;

namespace Inkhound.Core.WebshareProxy;

public class ProxyInfo
{
    public required string Address { get; init; }
    public required int Port { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public string? CountryCode { get; init; }
    public bool Valid { get; init; }
    public bool Failed { get; set; }

    public string ToUrl() => $"http://{Username}:{Password}@{Address}:{Port}/";

    public ProxyEndpoint ToProxyEndpoint() => new(Address, Port, Username, Password);

    public override string ToString() =>
        $"{Address}:{Port} [{CountryCode ?? "?"}]{(Failed ? " (failed)" : !Valid ? " (invalid)" : "")}";
}

public class WebshareProxyActivity
{
    public int RequestCount { get; init; }
    public long Bytes { get; init; }
    public int ErrorCount { get; init; }
}

public class WebshareStatistics
{
    // ── Account ─────────────────────────────────────────────────────────────
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    // ── Plan ────────────────────────────────────────────────────────────────
    public string? PlanType { get; init; }
    public string? PlanSubtype { get; init; }
    public int? ProxyCount { get; init; }
    public double? BandwidthLimitGb { get; init; }

    // ── Subscription period ─────────────────────────────────────────────────
    public string? Term { get; init; }
    public DateTime? PeriodStart { get; init; }
    public DateTime? PeriodEnd { get; init; }

    // ── Aggregated activity (from /proxy/activity/) ────────────────────────
    public int RequestCount { get; init; }
    public long TotalBytes { get; init; }
    public int ErrorCount { get; init; }
    public double AverageDurationSeconds { get; init; }
    public IReadOnlyDictionary<string, WebshareProxyActivity>? ByProxy { get; init; }
    public IReadOnlyDictionary<string, long>? ByDomain { get; init; }

    public double TotalBytesMb => TotalBytes / 1_048_576.0;
    public double TotalBytesGb => TotalBytes / 1_073_741_824.0;
    public double? BandwidthUsagePercent =>
        BandwidthLimitGb is > 0 ? TotalBytesGb / BandwidthLimitGb.Value * 100.0 : null;
}

using Inkhound.Core;
using Inkhound.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Inkhound.Web.Startup;

public class InkhoundManagerInitializer(
    InkhoundManager manager,
    IHubContext<AppHub> hub) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await manager.AutomaticLoadServices();

        manager.OnJobUpdated = ctx =>
            _ = hub.Clients.All.SendAsync("ManagerJobChanged", ctx, ct);

        manager.OnTrace = trace =>
            _ = hub.Clients.All.SendAsync("ManagerTrace", trace, ct);

        manager.OnHealthcheck = health =>
            _ = hub.Clients.All.SendAsync("ManagerHealthcheck", health, ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

using Inkhound.Web.State;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Inkhound.Web.Hubs;

[Authorize]
public class AppHub(PlatformStateService platformState) : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Send full state snapshot to the connecting client only
        var snapshot = platformState.GetFullPatch();
        await Clients.Caller.SendAsync("StateChanged", snapshot);
        await base.OnConnectedAsync();
    }
}

using Inkhound.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Inkhound.Web.Hubs;

[Authorize]
public class AppHub(InkhoundManager manager) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var state = manager.GetCurrentState();
        await Clients.Caller.SendAsync("ManagerStateChanged", state);
        await base.OnConnectedAsync();
    }
}

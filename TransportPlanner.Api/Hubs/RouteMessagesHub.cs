using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TransportPlanner.Infrastructure.Identity;

namespace TransportPlanner.Api.Hubs;

[Authorize]
public class RouteMessagesHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var ownerIdClaim = Context.User?.FindFirst("ownerId")?.Value;
        if (int.TryParse(ownerIdClaim, out var ownerId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"owner-{ownerId}");
        }

        if (Context.User?.IsInRole(AppRoles.SuperAdmin) == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "superadmin");
        }

        await base.OnConnectedAsync();
    }
}

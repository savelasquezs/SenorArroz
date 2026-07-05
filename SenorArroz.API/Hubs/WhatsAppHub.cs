using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace SenorArroz.API.Hubs;

[Authorize]
[DisableRateLimiting]
public class WhatsAppHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var branchId = Context.User?.FindFirst("branch_id")?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (role == "Superadmin")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "WhatsApp_Superadmin");
        }

        if (!string.IsNullOrEmpty(branchId)
            && (role == "Admin" || role == "Cashier"))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Branch_{branchId}_WhatsApp");
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var branchId = Context.User?.FindFirst("branch_id")?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (role == "Superadmin")
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "WhatsApp_Superadmin");
        }

        if (!string.IsNullOrEmpty(branchId)
            && (role == "Admin" || role == "Cashier"))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Branch_{branchId}_WhatsApp");
        }

        await base.OnDisconnectedAsync(exception);
    }
}

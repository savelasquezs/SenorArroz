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
            branchId = ResolveSelectedBranchId();

        if (!string.IsNullOrEmpty(branchId)
            && (role == "Admin" || role == "Cashier" || role == "Superadmin"))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Branch_{branchId}_WhatsApp");
        }
        if (role is "Admin" or "Superadmin")
            await Groups.AddToGroupAsync(Context.ConnectionId, "Tenant_1_WhatsApp_Unassigned");
        if (role == "Superadmin")
            await Groups.AddToGroupAsync(Context.ConnectionId, "Tenant_1_WhatsApp_Superadmin");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var branchId = Context.User?.FindFirst("branch_id")?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (role == "Superadmin")
            branchId = ResolveSelectedBranchId();

        if (!string.IsNullOrEmpty(branchId)
            && (role == "Admin" || role == "Cashier" || role == "Superadmin"))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Branch_{branchId}_WhatsApp");
        }
        if (role is "Admin" or "Superadmin")
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Tenant_1_WhatsApp_Unassigned");
        if (role == "Superadmin")
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Tenant_1_WhatsApp_Superadmin");

        await base.OnDisconnectedAsync(exception);
    }

    private string? ResolveSelectedBranchId()
    {
        var raw = Context.GetHttpContext()?.Request.Query["branchId"].FirstOrDefault();
        return int.TryParse(raw, out var branchId) && branchId > 0
            ? branchId.ToString()
            : null;
    }
}

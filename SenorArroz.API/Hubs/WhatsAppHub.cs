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
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (role == "Superadmin")
            branchId = ResolveSelectedBranchId();

        if (int.TryParse(tenantId, out var parsedTenantId) && parsedTenantId > 0
            && int.TryParse(branchId, out var parsedBranchId) && parsedBranchId > 0
            && (role == "Admin" || role == "Cashier" || role == "Superadmin"))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantRealtimeGroups.BranchRole(parsedTenantId, parsedBranchId, "WhatsApp"));
        }
        if (parsedTenantId > 0 && role is "Admin" or "Cashier" or "Superadmin")
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantRealtimeGroups.TenantChannel(parsedTenantId, "WhatsApp_Unassigned"));
        if (parsedTenantId > 0 && role == "Superadmin")
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantRealtimeGroups.TenantChannel(parsedTenantId, "WhatsApp_Superadmin"));
        if (parsedTenantId > 0 && int.TryParse(userId, out var parsedUserId) && parsedUserId > 0)
            await Groups.AddToGroupAsync(Context.ConnectionId, WhatsAppRealtimeGroupResolver.User(parsedTenantId, parsedUserId));

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var branchId = Context.User?.FindFirst("branch_id")?.Value;
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (role == "Superadmin")
            branchId = ResolveSelectedBranchId();

        if (int.TryParse(tenantId, out var parsedTenantId) && parsedTenantId > 0
            && int.TryParse(branchId, out var parsedBranchId) && parsedBranchId > 0
            && (role == "Admin" || role == "Cashier" || role == "Superadmin"))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantRealtimeGroups.BranchRole(parsedTenantId, parsedBranchId, "WhatsApp"));
        }
        if (parsedTenantId > 0 && role is "Admin" or "Cashier" or "Superadmin")
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantRealtimeGroups.TenantChannel(parsedTenantId, "WhatsApp_Unassigned"));
        if (parsedTenantId > 0 && role == "Superadmin")
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantRealtimeGroups.TenantChannel(parsedTenantId, "WhatsApp_Superadmin"));
        if (parsedTenantId > 0 && int.TryParse(userId, out var parsedUserId) && parsedUserId > 0)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, WhatsAppRealtimeGroupResolver.User(parsedTenantId, parsedUserId));

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

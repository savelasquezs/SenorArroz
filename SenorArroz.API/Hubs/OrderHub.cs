using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace SenorArroz.API.Hubs;

[Authorize]
[DisableRateLimiting]
public class OrderHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var branchId = Context.User?.FindFirst("branch_id")?.Value;
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        if (role == "Superadmin")
            branchId = ResolveSelectedBranchId();
        
        if (int.TryParse(tenantId, out var parsedTenantId) && parsedTenantId > 0
            && int.TryParse(branchId, out var parsedBranchId) && parsedBranchId > 0)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantRealtimeGroups.Branch(parsedTenantId, parsedBranchId));
            
            // Agregar a grupo específico de rol
            if (role == "Kitchen")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, TenantRealtimeGroups.BranchRole(parsedTenantId, parsedBranchId, "Kitchen"));
            }
            else if (role == "Deliveryman")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, TenantRealtimeGroups.BranchRole(parsedTenantId, parsedBranchId, "Delivery"));
            }
            else if (role == "Admin" || role == "Superadmin" || role == "Cashier")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, TenantRealtimeGroups.BranchRole(parsedTenantId, parsedBranchId, "Admin"));
            }
        }
        
        await base.OnConnectedAsync();
    }
    
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var branchId = Context.User?.FindFirst("branch_id")?.Value;
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        if (role == "Superadmin")
            branchId = ResolveSelectedBranchId();
        
        if (int.TryParse(tenantId, out var parsedTenantId) && parsedTenantId > 0
            && int.TryParse(branchId, out var parsedBranchId) && parsedBranchId > 0)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantRealtimeGroups.Branch(parsedTenantId, parsedBranchId));
            
            if (role == "Kitchen")
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantRealtimeGroups.BranchRole(parsedTenantId, parsedBranchId, "Kitchen"));
            }
            else if (role == "Deliveryman")
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantRealtimeGroups.BranchRole(parsedTenantId, parsedBranchId, "Delivery"));
            }
            else if (role == "Admin" || role == "Superadmin" || role == "Cashier")
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantRealtimeGroups.BranchRole(parsedTenantId, parsedBranchId, "Admin"));
            }
        }
        
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


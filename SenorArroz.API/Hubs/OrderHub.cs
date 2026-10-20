using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;

namespace SenorArroz.API.Hubs;

[Authorize]
[DisableRateLimiting]
public class OrderHub(IApplicationDbContext db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantClaim = Context.User?.FindFirst("tenant_id")?.Value;
        var userClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(tenantClaim, out var tenantId) || !int.TryParse(userClaim, out var userId))
        { Context.Abort(); return; }
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.TenantId == tenantId && x.Active,
            Context.ConnectionAborted);
        if (user is null || user.Role == UserRole.Kitchen && !user.WebAccessEnabled)
        { Context.Abort(); return; }

        if (!user.IsTerminal)
            await Groups.AddToGroupAsync(Context.ConnectionId, EmployeeLeaveRealtime.UserGroup(tenantId, userId));

        // Un cocinero personal NO recibe eventos de pedidos ni datos de los grupos generales de sucursal.
        if (user.Role == UserRole.Kitchen && !user.IsTerminal)
        { await base.OnConnectedAsync(); return; }
        if (user.IsTerminal && user.Role != UserRole.Kitchen)
        { Context.Abort(); return; }

        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var branchId = role == "Superadmin" ? ResolveSelectedBranchId() : user.BranchId;
        if (branchId is > 0)
        {
            if (!await db.Branches.AnyAsync(x => x.Id == branchId.Value && x.TenantId == tenantId, Context.ConnectionAborted))
            { Context.Abort(); return; }
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantRealtimeGroups.Branch(tenantId, branchId.Value));
            if (role is "Kitchen" or "Admin" or "Superadmin")
                await Groups.AddToGroupAsync(Context.ConnectionId, TenantRealtimeGroups.BranchRole(tenantId, branchId.Value, "Kitchen"));
            if (role == "Deliveryman")
                await Groups.AddToGroupAsync(Context.ConnectionId, TenantRealtimeGroups.BranchRole(tenantId, branchId.Value, "Delivery"));
            if (role is "Admin" or "Superadmin" or "Cashier")
                await Groups.AddToGroupAsync(Context.ConnectionId, TenantRealtimeGroups.BranchRole(tenantId, branchId.Value, "Admin"));
        }
        await base.OnConnectedAsync();
    }

    // SignalR elimina todos los grupos de la conexión al desconectarse; no se reconstruye contexto obsoleto.
    private int? ResolveSelectedBranchId()
    {
        var raw = Context.GetHttpContext()?.Request.Query["branchId"].FirstOrDefault();
        return int.TryParse(raw, out var branchId) && branchId > 0 ? branchId : null;
    }
}

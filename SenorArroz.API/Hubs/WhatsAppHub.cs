using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Constants;

namespace SenorArroz.API.Hubs;

[Authorize]
[DisableRateLimiting]
public class WhatsAppHub(
    IApplicationDbContext db,
    ITenantCapabilityService capabilities,
    ITenantConnectionRegistry connections) : Hub
{
    private IDisposable? _registration;

    public override async Task OnConnectedAsync()
    {
        if (!await capabilities.HasAddonAsync(TenantAddons.WhatsAppAi, Context.ConnectionAborted))
        {
            Context.Abort();
            return;
        }

        var branchId = Context.User?.FindFirst("branch_id")?.Value;
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (role == "Superadmin")
            branchId = ResolveSelectedBranchId();

        if (int.TryParse(branchId, out var parsedBranchId) && int.TryParse(tenantId, out var parsedTenantId)
            && await db.Branches.AsNoTracking().AnyAsync(x => x.Id == parsedBranchId, Context.ConnectionAborted)
            && (role == "Admin" || role == "Cashier" || role == "Superadmin"))
        {
            _registration = connections.Register(parsedTenantId, Context.ConnectionId, Context.Abort);
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantHubGroups.Branch(parsedTenantId, parsedBranchId, "WhatsApp"));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _registration?.Dispose();
        var branchId = Context.User?.FindFirst("branch_id")?.Value;
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (role == "Superadmin")
            branchId = ResolveSelectedBranchId();

        if (int.TryParse(branchId, out var parsedBranchId) && int.TryParse(tenantId, out var parsedTenantId)
            && (role == "Admin" || role == "Cashier" || role == "Superadmin"))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantHubGroups.Branch(parsedTenantId, parsedBranchId, "WhatsApp"));
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

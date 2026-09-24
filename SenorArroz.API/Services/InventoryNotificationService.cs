using Microsoft.AspNetCore.SignalR;
using SenorArroz.API.Hubs;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Services;

public sealed class InventoryNotificationService(IHubContext<OrderHub> hub, ICurrentTenant tenant) : IInventoryNotificationService
{
    public Task NotifyChangedAsync(int branchId, string reason, CancellationToken ct)
        => hub.Clients.Group(TenantRealtimeGroups.Branch(tenant.TenantId, branchId)).SendAsync("InventoryChanged", new { branchId, reason }, ct);

    public Task NotifyTransferPendingAsync(int destinationBranchId, int transferId, CancellationToken ct)
        => hub.Clients.Group(TenantRealtimeGroups.BranchRole(tenant.TenantId, destinationBranchId, "Admin")).SendAsync("InventoryTransferPending", new { branchId = destinationBranchId, transferId }, ct);
}

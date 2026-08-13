using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SenorArroz.API.Hubs;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.API.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private readonly IHubContext<OrderHub> _hubContext;
    private readonly IFcmPushService _fcm;
    private readonly IFreeDeliverymanFcmTokenResolver _freeDeliverymanTokens;
    private readonly ILogger<OrderNotificationService> _logger;
    private readonly TenantHubGroupResolver _groups;

    public OrderNotificationService(
        IHubContext<OrderHub> hubContext,
        IFcmPushService fcm,
        IFreeDeliverymanFcmTokenResolver freeDeliverymanTokens,
        ILogger<OrderNotificationService> logger,
        TenantHubGroupResolver groups)
    {
        _hubContext = hubContext;
        _fcm = fcm;
        _freeDeliverymanTokens = freeDeliverymanTokens;
        _logger = logger;
        _groups = groups;
    }

    public async Task NotifyNewOrderToKitchen(OrderDto order)
    {
        await _hubContext.Clients
            .Group(TenantHubGroups.Branch(await _groups.TenantIdAsync(order.BranchId), order.BranchId, "Kitchen"))
            .SendAsync("NewOrder", order);
    }

    public async Task NotifyOrderReadyToDelivery(OrderDto order)
    {
        await _hubContext.Clients
            .Group(TenantHubGroups.Branch(await _groups.TenantIdAsync(order.BranchId), order.BranchId, "Delivery"))
            .SendAsync("OrderReady", order);

        await SendPushToFreeDeliverymenAsync(order);
    }

    public async Task NotifyReservationToKitchen(OrderDto order)
    {
        await _hubContext.Clients
            .Group(TenantHubGroups.Branch(await _groups.TenantIdAsync(order.BranchId), order.BranchId, "Kitchen"))
            .SendAsync("ReservationReady", order);
    }

    public async Task NotifyOrderAssignedToDelivery(OrderDto order)
    {
        await _hubContext.Clients
            .Group(TenantHubGroups.Branch(await _groups.TenantIdAsync(order.BranchId), order.BranchId, "Delivery"))
            .SendAsync("OrderAssigned", order);
    }

    public async Task NotifyDeliveryRoutingPlanChanged(int branchId, int planId, long version)
    {
        var payload = new { branchId, planId, version };
        var tenantId = await _groups.TenantIdAsync(branchId);
        await Task.WhenAll(
            _hubContext.Clients.Group(TenantHubGroups.Branch(tenantId, branchId, "Delivery")).SendAsync("DeliveryRoutingPlanChanged", payload),
            _hubContext.Clients.Group(TenantHubGroups.Branch(tenantId, branchId, "Admin")).SendAsync("DeliveryRoutingPlanChanged", payload));
    }

    public async Task NotifyRouteProposalClaimed(int branchId, int proposalId, int deliverymanId)
    {
        var payload = new { branchId, proposalId, deliverymanId };
        var tenantId = await _groups.TenantIdAsync(branchId);
        await Task.WhenAll(
            _hubContext.Clients.Group(TenantHubGroups.Branch(tenantId, branchId, "Delivery")).SendAsync("RouteProposalClaimed", payload),
            _hubContext.Clients.Group(TenantHubGroups.Branch(tenantId, branchId, "Admin")).SendAsync("RouteProposalClaimed", payload));
    }

    public async Task NotifyOrderModifiedToKitchen(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null)
    {
        await _hubContext.Clients
            .Group(TenantHubGroups.Branch(await _groups.TenantIdAsync(order.BranchId), order.BranchId, "Kitchen"))
            .SendAsync("OrderModified", new { order, modificationKind, kitchenChanges });
    }

    public async Task NotifyOrderModifiedToDelivery(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null)
    {
        await _hubContext.Clients
            .Group(TenantHubGroups.Branch(await _groups.TenantIdAsync(order.BranchId), order.BranchId, "Delivery"))
            .SendAsync("OrderModified", new { order, modificationKind, kitchenChanges });
    }

    public async Task NotifyOrderCancelledToKitchen(int branchId, int orderId, string? reasonPreview = null)
    {
        await _hubContext.Clients
            .Group(TenantHubGroups.Branch(await _groups.TenantIdAsync(branchId), branchId, "Kitchen"))
            .SendAsync("OrderCancelled", new { orderId, reasonPreview });
    }

    public async Task NotifyDeliverymanLocation(
        int branchId,
        int deliverymanId,
        int? deliveryRouteId,
        double latitude,
        double longitude,
        DateTime recordedAt)
    {
        await _hubContext.Clients
            .Group(TenantHubGroups.Branch(await _groups.TenantIdAsync(branchId), branchId, "Admin"))
            .SendAsync("DeliverymanLocationUpdate", new
            {
                deliverymanId,
                deliveryRouteId,
                latitude,
                longitude,
                recordedAt,
            });
    }

    private async Task SendPushToFreeDeliverymenAsync(OrderDto order)
    {
        var correlationId = $"order_ready:{order.Id}";
        try
        {
            _logger.LogInformation(
                "FCM_ORDER_READY [{Corr}] STEP resolve branchId={BranchId}",
                correlationId, order.BranchId);

            var resolved = await _freeDeliverymanTokens.ResolveAsync(order.BranchId);

            if (resolved.Tokens.Count == 0)
            {
                _logger.LogInformation(
                    "FCM_ORDER_READY [{Corr}] STEP skip no_tokens busyCount={Busy} atBranchCount={AtBranch}",
                    correlationId,
                    resolved.BusyDeliverymanCount,
                    resolved.AtBranchDeliverymanCount);
                return;
            }

            _logger.LogInformation(
                "FCM_ORDER_READY [{Corr}] STEP send tokens={Count} atBranchCount={AtBranch}",
                correlationId,
                resolved.Tokens.Count,
                resolved.AtBranchDeliverymanCount);

            await _fcm.SendToTokensAsync(
                resolved.Tokens,
                title: "\U0001F35A Pedido listo para entrega",
                body: $"Pedido #{order.Id} — {order.NeighborhoodName ?? order.AddressDescription ?? "Ver detalles"}",
                data: new Dictionary<string, string>
                {
                    ["orderId"] = order.Id.ToString(),
                    ["branchId"] = order.BranchId.ToString(),
                    ["type"] = "order_ready",
                },
                cancellationToken: default,
                correlationId: correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "FCM_ORDER_READY [{Corr}] STEP exception branchId={BranchId}",
                correlationId, order.BranchId);
        }
    }
}

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.API.Hubs;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.API.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private readonly IHubContext<OrderHub> _hubContext;
    private readonly IFcmPushService _fcm;
    private readonly IFreeDeliverymanFcmTokenResolver _freeDeliverymanTokens;
    private readonly ILogger<OrderNotificationService> _logger;
    private readonly ICurrentTenant _tenant;
    private readonly ApplicationDbContext _db;

    public OrderNotificationService(
        IHubContext<OrderHub> hubContext,
        IFcmPushService fcm,
        IFreeDeliverymanFcmTokenResolver freeDeliverymanTokens,
        ICurrentTenant tenant,
        ApplicationDbContext db,
        ILogger<OrderNotificationService> logger)
    {
        _hubContext = hubContext;
        _fcm = fcm;
        _freeDeliverymanTokens = freeDeliverymanTokens;
        _tenant = tenant;
        _db = db;
        _logger = logger;
    }

    public async Task NotifyOrderManagementChanged(int orderId, int branchId, int? originBranchId, long version)
    {
        var groups = new List<string> { Group(branchId, "Admin") };
        if (originBranchId is > 0 && originBranchId != branchId)
            groups.Add(Group(originBranchId.Value, "Admin"));
        await _hubContext.Clients.Groups(groups).SendAsync("OrderManagementChanged", new
        {
            orderId, branchId, originBranchId, managementVersion = version
        });
    }

    public async Task NotifyNewOrderToKitchen(OrderDto order)
    {
        await RecordKitchenVoiceEventAsync(order, "new");
        await _hubContext.Clients
            .Group(Group(order.BranchId, "Kitchen"))
            .SendAsync("NewOrder", order);
    }

    public async Task NotifyOrderReadyToDelivery(OrderDto order)
    {
        await _hubContext.Clients
            .Group(Group(order.BranchId, "Delivery"))
            .SendAsync("OrderReady", order);

        await SendPushToFreeDeliverymenAsync(order);
    }

    public async Task NotifyReservationToKitchen(OrderDto order)
    {
        await RecordKitchenVoiceEventAsync(order, "reservation");
        await _hubContext.Clients
            .Group(Group(order.BranchId, "Kitchen"))
            .SendAsync("ReservationReady", order);
    }

    public async Task NotifyOrderAssignedToDelivery(OrderDto order)
    {
        await _hubContext.Clients
            .Group(Group(order.BranchId, "Delivery"))
            .SendAsync("OrderAssigned", order);
    }

    public async Task NotifyDeliveryRoutingPlanChanged(int branchId, int planId, long version)
    {
        var payload = new { branchId, planId, version };
        await Task.WhenAll(
            _hubContext.Clients.Group(Group(branchId, "Delivery")).SendAsync("DeliveryRoutingPlanChanged", payload),
            _hubContext.Clients.Group(Group(branchId, "Admin")).SendAsync("DeliveryRoutingPlanChanged", payload));
    }

    public async Task NotifyRouteProposalClaimed(int branchId, int proposalId, int deliverymanId)
    {
        var payload = new { branchId, proposalId, deliverymanId };
        await Task.WhenAll(
            _hubContext.Clients.Group(Group(branchId, "Delivery")).SendAsync("RouteProposalClaimed", payload),
            _hubContext.Clients.Group(Group(branchId, "Admin")).SendAsync("RouteProposalClaimed", payload));
    }

    public async Task NotifyOrderModifiedToKitchen(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null)
    {
        await _hubContext.Clients
            .Group(Group(order.BranchId, "Kitchen"))
            .SendAsync("OrderModified", new { order, modificationKind, kitchenChanges });
    }

    public async Task NotifyOrderModifiedToDelivery(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null)
    {
        await _hubContext.Clients
            .Group(Group(order.BranchId, "Delivery"))
            .SendAsync("OrderModified", new { order, modificationKind, kitchenChanges });
    }

    public async Task NotifyOrderCancelledToKitchen(int branchId, int orderId, string? reasonPreview = null)
    {
        await _hubContext.Clients
            .Group(Group(branchId, "Kitchen"))
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
            .Group(Group(branchId, "Admin"))
            .SendAsync("DeliverymanLocationUpdate", new
            {
                deliverymanId,
                deliveryRouteId,
                latitude,
                longitude,
                recordedAt,
            });
    }

    public Task NotifyDeliveryTrackingAlert(int branchId, long alertId, int deliverymanId, string title, string message) =>
        _hubContext.Clients.Group(Group(branchId, "Admin"))
            .SendAsync("DeliveryTrackingAlertChanged", new { branchId, alertId, deliverymanId, title, message });

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

    // Durable inbox: it is stored before the realtime hint is sent. One entry per
    // order, regardless of retries or a reservation that was already announced.
    private async Task RecordKitchenVoiceEventAsync(OrderDto order, string kind)
    {
        if (!_tenant.HasTenant) throw new InvalidOperationException("Tenant context is required for kitchen alerts.");
        try
        {
            var exists = await _db.KitchenVoiceEvents.AsNoTracking().AnyAsync(e =>
                e.TenantId == _tenant.TenantId && e.OrderId == order.Id);
            if (exists) return;

            _db.KitchenVoiceEvents.Add(new KitchenVoiceEvent
            {
                TenantId = _tenant.TenantId,
                BranchId = order.BranchId,
                OrderId = order.Id,
                Kind = kind,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Preserve live notifications even before the schema rollout or if
            // persistence fails. The durable inbox failure must be visible in logs.
            _logger.LogError(ex, "Could not persist kitchen voice event for order {OrderId} branch {BranchId}", order.Id, order.BranchId);
        }
    }

    private string Group(int branchId, string role)
    {
        if (!_tenant.HasTenant) throw new InvalidOperationException("Tenant context is required for realtime notifications.");
        return TenantRealtimeGroups.BranchRole(_tenant.TenantId, branchId, role);
    }
}

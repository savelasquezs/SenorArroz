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

    public OrderNotificationService(
        IHubContext<OrderHub> hubContext,
        IFcmPushService fcm,
        IFreeDeliverymanFcmTokenResolver freeDeliverymanTokens,
        ILogger<OrderNotificationService> logger)
    {
        _hubContext = hubContext;
        _fcm = fcm;
        _freeDeliverymanTokens = freeDeliverymanTokens;
        _logger = logger;
    }

    public async Task NotifyNewOrderToKitchen(OrderDto order)
    {
        await _hubContext.Clients
            .Group($"Branch_{order.BranchId}_Kitchen")
            .SendAsync("NewOrder", order);
    }

    public async Task NotifyOrderReadyToDelivery(OrderDto order)
    {
        await _hubContext.Clients
            .Group($"Branch_{order.BranchId}_Delivery")
            .SendAsync("OrderReady", order);

        await SendPushToFreeDeliverymenAsync(order);
    }

    public async Task NotifyReservationToKitchen(OrderDto order)
    {
        await _hubContext.Clients
            .Group($"Branch_{order.BranchId}_Kitchen")
            .SendAsync("ReservationReady", order);
    }

    public async Task NotifyOrderAssignedToDelivery(OrderDto order)
    {
        await _hubContext.Clients
            .Group($"Branch_{order.BranchId}_Delivery")
            .SendAsync("OrderAssigned", order);
    }

    public async Task NotifyOrderModifiedToKitchen(OrderDto order, string modificationKind)
    {
        await _hubContext.Clients
            .Group($"Branch_{order.BranchId}_Kitchen")
            .SendAsync("OrderModified", new { order, modificationKind });
    }

    public async Task NotifyOrderModifiedToDelivery(OrderDto order, string modificationKind)
    {
        await _hubContext.Clients
            .Group($"Branch_{order.BranchId}_Delivery")
            .SendAsync("OrderModified", new { order, modificationKind });
    }

    public async Task NotifyDeliverymanLocation(
        int branchId,
        int deliverymanId,
        int deliveryRouteId,
        double latitude,
        double longitude,
        DateTime recordedAt)
    {
        await _hubContext.Clients
            .Group($"Branch_{branchId}_Admin")
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
                    "FCM_ORDER_READY [{Corr}] STEP skip no_tokens busyCount={Busy}",
                    correlationId, resolved.BusyDeliverymanCount);
                return;
            }

            _logger.LogInformation(
                "FCM_ORDER_READY [{Corr}] STEP send tokens={Count}",
                correlationId, resolved.Tokens.Count);

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

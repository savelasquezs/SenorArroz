using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.API.Hubs;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;

namespace SenorArroz.API.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private readonly IHubContext<OrderHub> _hubContext;
    private readonly IFcmPushService _fcm;
    private readonly IApplicationDbContext _db;

    public OrderNotificationService(
        IHubContext<OrderHub> hubContext,
        IFcmPushService fcm,
        IApplicationDbContext db)
    {
        _hubContext = hubContext;
        _fcm = fcm;
        _db = db;
    }

    public async Task NotifyNewOrderToKitchen(OrderDto order)
    {
        await _hubContext.Clients
            .Group($"Branch_{order.BranchId}_Kitchen")
            .SendAsync("NewOrder", order);
    }

    public async Task NotifyOrderReadyToDelivery(OrderDto order)
    {
        // 1. SignalR (igual que antes — para los que tengan la app abierta)
        await _hubContext.Clients
            .Group($"Branch_{order.BranchId}_Delivery")
            .SendAsync("OrderReady", order);

        // 2. Push FCM → solo domiciliarios LIBRES (sin pedidos onTheWay) de la misma sucursal
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

    // ─── Push FCM ────────────────────────────────────────────────────────────

    private async Task SendPushToFreeDeliverymenAsync(OrderDto order)
    {
        try
        {
            // Domiciliarios de la sucursal que NO tengan pedidos "onTheWay" asignados
            var busyDeliverymanIds = await _db.Orders
                .Where(o => o.BranchId == order.BranchId &&
                            o.Status == OrderStatus.OnTheWay &&
                            o.DeliveryManId != null)
                .Select(o => o.DeliveryManId!.Value)
                .Distinct()
                .ToListAsync();

            // Tokens de domiciliarios activos + libres de esa sucursal
            var tokens = await _db.UserDeviceTokens
                .Where(t =>
                    t.User.BranchId == order.BranchId &&
                    t.User.Role == UserRole.Deliveryman &&
                    t.User.Active &&
                    !busyDeliverymanIds.Contains(t.UserId))
                .Select(t => t.Token)
                .ToListAsync();

            if (tokens.Count == 0) return;

            await _fcm.SendToTokensAsync(
                tokens,
                title: "🍚 Pedido listo para entrega",
                body: $"Pedido #{order.Id} — {order.NeighborhoodName ?? order.AddressDescription ?? "Ver detalles"}",
                data: new Dictionary<string, string>
                {
                    ["orderId"] = order.Id.ToString(),
                    ["branchId"] = order.BranchId.ToString(),
                    ["type"] = "order_ready",
                });
        }
        catch
        {
            // El push es best-effort: si falla, no bloquea el flujo principal
        }
    }
}


using Microsoft.AspNetCore.SignalR;
using SenorArroz.API.Hubs;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.API.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private readonly IHubContext<OrderHub> _hubContext;

    public OrderNotificationService(IHubContext<OrderHub> hubContext)
    {
        _hubContext = hubContext;
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
    }

    public async Task NotifyReservationToKitchen(OrderDto order)
    {
        await _hubContext.Clients
            .Group($"Branch_{order.BranchId}_Kitchen")
            .SendAsync("ReservationReady", order);
    }
}


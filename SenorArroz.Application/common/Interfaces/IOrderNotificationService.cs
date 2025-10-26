using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Common.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyNewOrderToKitchen(OrderDto order);
    Task NotifyOrderReadyToDelivery(OrderDto order);
    Task NotifyReservationToKitchen(OrderDto order);
}


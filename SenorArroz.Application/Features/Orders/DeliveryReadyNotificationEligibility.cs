using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Orders;

public static class DeliveryReadyNotificationEligibility
{
    public static bool ShouldNotifyOwnDeliverymen(Order order) =>
        order.Type == OrderType.Delivery
        && string.IsNullOrWhiteSpace(order.ExternalFulfillmentProvider);
}

using SenorArroz.Application.Features.Orders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Tests;

public class DeliveryReadyNotificationEligibilityTests
{
    [Fact]
    public void OwnDelivery_IsEligible()
    {
        var order = new Order { Type = OrderType.Delivery };

        Assert.True(DeliveryReadyNotificationEligibility.ShouldNotifyOwnDeliverymen(order));
    }

    [Fact]
    public void Onsite_IsNotEligible()
    {
        var order = new Order { Type = OrderType.Onsite };

        Assert.False(DeliveryReadyNotificationEligibility.ShouldNotifyOwnDeliverymen(order));
    }

    [Fact]
    public void ExternalDelivery_IsNotEligibleForOwnDeliverymen()
    {
        var order = new Order
        {
            Type = OrderType.Delivery,
            ExternalFulfillmentProvider = "rappi",
        };

        Assert.False(DeliveryReadyNotificationEligibility.ShouldNotifyOwnDeliverymen(order));
    }
}

using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Tests;

public class OrderTotalsHelperTests
{
    [Fact]
    public void RecalculateFromOrderDetails_sets_total_including_delivery_fee()
    {
        var order = new Order
        {
            DeliveryFee = 5000,
            OrderDetails =
            [
                new OrderDetail { Quantity = 2, UnitPrice = 53000, Discount = 0 },
                new OrderDetail { Quantity = 1, UnitPrice = 31000, Discount = 0 },
            ],
        };

        OrderTotalsHelper.RecalculateFromOrderDetails(order);

        Assert.Equal(2 * 53000 + 31000, order.Subtotal);
        Assert.Equal(0, order.DiscountTotal);
        Assert.Equal(2 * 53000 + 31000 + 5000, order.Total);
    }

    [Fact]
    public void RecalculateFromOrderDetails_uses_line_subtotal_when_set()
    {
        var order = new Order
        {
            DeliveryFee = null,
            OrderDetails = [new OrderDetail { Quantity = 1, UnitPrice = 10000, Discount = 0, Subtotal = 9999 }],
        };

        OrderTotalsHelper.RecalculateFromOrderDetails(order);

        Assert.Equal(10000, order.Subtotal);
        Assert.Equal(9999, order.Total);
    }
}

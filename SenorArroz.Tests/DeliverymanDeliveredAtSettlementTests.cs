using Moq;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public class DeliverymanDeliveredAtSettlementTests
{
    [Fact]
    public void FilterOrdersForCycle_IncludesOrder_WhenDeliveredInRange_EvenIfCreatedEarlier()
    {
        var dayFrom = new DateTime(2026, 4, 19, 5, 0, 0, DateTimeKind.Utc);
        var dayTo = new DateTime(2026, 4, 20, 4, 59, 59, DateTimeKind.Utc);
        var deliveredAt = new DateTime(2026, 4, 19, 15, 30, 0, DateTimeKind.Utc);

        var order = new Order
        {
            Status = OrderStatus.Delivered,
            CreatedAt = dayFrom.AddDays(-5),
        };
        order.AddStatusTime(OrderStatus.Delivered, deliveredAt);

        var result = DeliverymanSettlementCycleHelper.FilterOrdersForCycle(
            new[] { order },
            dayFrom,
            dayTo,
            lastLiquidationAtUtc: null,
            useSettlementCycle: false);

        Assert.Single(result);
    }

    [Fact]
    public void FilterOrdersForCycle_WithPartialLiquidation_IncludesOnlyDeliveredAfterLiquidation()
    {
        var dayFrom = new DateTime(2026, 4, 19, 5, 0, 0, DateTimeKind.Utc);
        var dayTo = new DateTime(2026, 4, 20, 4, 59, 59, DateTimeKind.Utc);
        var liquidation = new DateTime(2026, 4, 19, 14, 0, 0, DateTimeKind.Utc);

        var before = new Order { Id = 1, Status = OrderStatus.Delivered, CreatedAt = dayFrom };
        before.AddStatusTime(OrderStatus.Delivered, new DateTime(2026, 4, 19, 10, 0, 0, DateTimeKind.Utc));

        var after = new Order { Id = 2, Status = OrderStatus.Delivered, CreatedAt = dayFrom };
        after.AddStatusTime(OrderStatus.Delivered, new DateTime(2026, 4, 19, 16, 0, 0, DateTimeKind.Utc));

        var result = DeliverymanSettlementCycleHelper.FilterOrdersForCycle(
            new[] { before, after },
            dayFrom,
            dayTo,
            liquidation,
            useSettlementCycle: true);

        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
    }

    [Fact]
    public async Task DeliverymanDeliveredOrdersQuery_CallsRepositoryForDeliveryAndOnsite()
    {
        var fromUtc = new DateTime(2026, 6, 1, 5, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 6, 2, 4, 59, 59, DateTimeKind.Utc);
        const int branchId = 10;
        const int dmId = 20;

        var repo = new Mock<IOrderRepository>();
        var oDelivery = new Order { Id = 1, Type = OrderType.Delivery };
        var oOnsite = new Order { Id = 2, Type = OrderType.Onsite };

        repo.Setup(r => r.SearchDeliveredOrdersByDeliveredAtRangeAsync(
                branchId,
                dmId,
                OrderType.Delivery,
                fromUtc,
                toUtc,
                1,
                500,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Order>
            {
                Items = new List<Order> { oDelivery },
                TotalCount = 1,
            });

        repo.Setup(r => r.SearchDeliveredOrdersByDeliveredAtRangeAsync(
                branchId,
                dmId,
                OrderType.Onsite,
                fromUtc,
                toUtc,
                1,
                500,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Order>
            {
                Items = new List<Order> { oOnsite },
                TotalCount = 1,
            });

        var list = await DeliverymanDeliveredOrdersQuery.LoadAllDeliveredInRangeAsync(
            repo.Object,
            branchId,
            dmId,
            fromUtc,
            toUtc,
            CancellationToken.None);

        Assert.Equal(2, list.Count);
        repo.Verify(
            r => r.SearchDeliveredOrdersByDeliveredAtRangeAsync(
                branchId,
                dmId,
                OrderType.Delivery,
                fromUtc,
                toUtc,
                It.IsAny<int>(),
                500,
                It.IsAny<CancellationToken>()),
            Times.Once);
        repo.Verify(
            r => r.SearchDeliveredOrdersByDeliveredAtRangeAsync(
                branchId,
                dmId,
                OrderType.Onsite,
                fromUtc,
                toUtc,
                It.IsAny<int>(),
                500,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

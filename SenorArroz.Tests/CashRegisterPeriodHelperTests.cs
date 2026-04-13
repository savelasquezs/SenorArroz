using SenorArroz.Application.Features.CashRegister.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Tests;

public class CashRegisterPeriodHelperTests
{
    [Fact]
    public void Uses_PrepareAt_when_set()
    {
        var since = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 1, 20, 23, 59, 59, DateTimeKind.Utc);
        var prepare = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Status = OrderStatus.Delivered,
            PrepareAt = prepare,
            CreatedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        Assert.True(CashRegisterPeriodHelper.IsDeliveredSaleInCashRegisterPeriod(order, since, now));
    }

    [Fact]
    public void Uses_CreatedAt_when_PrepareAt_null()
    {
        var since = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 1, 20, 23, 59, 59, DateTimeKind.Utc);
        var created = new DateTime(2026, 1, 12, 8, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Status = OrderStatus.Delivered,
            PrepareAt = null,
            CreatedAt = created,
            UpdatedAt = new DateTime(2026, 1, 18, 0, 0, 0, DateTimeKind.Utc),
        };
        Assert.True(CashRegisterPeriodHelper.IsDeliveredSaleInCashRegisterPeriod(order, since, now));
    }

    [Fact]
    public void Excludes_when_effective_instant_before_since_even_if_UpdatedAt_later()
    {
        var since = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 1, 20, 23, 59, 59, DateTimeKind.Utc);
        var order = new Order
        {
            Status = OrderStatus.Delivered,
            PrepareAt = null,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 18, 0, 0, 0, DateTimeKind.Utc),
        };
        Assert.False(CashRegisterPeriodHelper.IsDeliveredSaleInCashRegisterPeriod(order, since, now));
    }
}

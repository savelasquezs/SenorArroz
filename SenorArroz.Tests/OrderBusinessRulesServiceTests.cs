using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Tests;

public class OrderBusinessRulesServiceTests
{
    [Fact]
    public void IsSameDay_true_when_order_and_now_share_colombia_calendar_day()
    {
        var utcNow = new DateTime(2026, 4, 15, 4, 30, 0, DateTimeKind.Utc);
        var created = new DateTime(2026, 4, 14, 20, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(utcNow);
        var sut = new OrderBusinessRulesService(clock);
        Assert.True(sut.IsSameDay(created));
    }

    [Fact]
    public void IsSameDay_false_for_previous_colombia_day()
    {
        var utcNow = new DateTime(2026, 4, 15, 4, 30, 0, DateTimeKind.Utc);
        var created = new DateTime(2026, 4, 13, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(utcNow);
        var sut = new OrderBusinessRulesService(clock);
        Assert.False(sut.IsSameDay(created));
    }

    [Fact]
    public void CanModifyPayments_uses_colombia_same_day_not_utc_midnight()
    {
        var utcNow = new DateTime(2026, 4, 15, 4, 30, 0, DateTimeKind.Utc);
        var created = new DateTime(2026, 4, 14, 20, 0, 0, DateTimeKind.Utc);
        var order = new Order { Status = OrderStatus.Delivered, CreatedAt = created };
        var clock = new FakeClock(utcNow);
        var sut = new OrderBusinessRulesService(clock);
        Assert.True(sut.CanModifyPayments(order, Roles.Cashier));
    }
}

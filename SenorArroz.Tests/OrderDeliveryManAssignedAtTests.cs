using SenorArroz.Domain.Entities;
using Xunit;

namespace SenorArroz.Tests;

public class OrderDeliveryManAssignedAtTests
{
    [Fact]
    public void TouchDeliveryManAssignedAtUtc_preserves_other_status_times_and_sets_assignment_key()
    {
        var order = new Order { StatusTimes = """{"ready":"2026-04-30T15:00:00Z"}""" };
        var utc = new DateTime(2026, 4, 30, 16, 0, 0, DateTimeKind.Utc);

        order.TouchDeliveryManAssignedAtUtc(utc);

        var dict = order.GetStatusTimes();
        Assert.Equal(utc, dict[Order.DeliveryManAssignedStatusTimeKey]);
        Assert.True(dict.TryGetValue("ready", out var ready));
        Assert.Equal(new DateTime(2026, 4, 30, 15, 0, 0, DateTimeKind.Utc), DateTime.SpecifyKind(ready.ToUniversalTime(), DateTimeKind.Utc));
    }
}

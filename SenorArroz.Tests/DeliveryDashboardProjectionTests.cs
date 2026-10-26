using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public class DeliveryDashboardProjectionTests
{
    [Theory]
    [InlineData(null, 15d)]
    [InlineData("next_route", 0d)]
    [InlineData("return", 0d)]
    [InlineData("legacy", 0d)]
    [InlineData("offline_confirmation", 0d)]
    public async Task DeliveredOrders_LoadOnlyAnalyticsFieldsAndDriverName(string? source, double expectedMinutes)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options, currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
        var branch = new Branch { Name = "S", Address = "A", Phone1 = "1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var driver = new User { Name = "Driver", Email = "driver@local.test", BranchId = 1 };
        db.Branches.Add(branch);
        db.Users.Add(driver);
        await db.SaveChangesAsync();
        var delivered = new DateTime(2026, 10, 23, 14, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            BranchId = branch.Id, TakenById = driver.Id, DeliveryManId = driver.Id,
            Status = OrderStatus.Delivered, Type = OrderType.Delivery,
            CreatedAt = delivered.AddMinutes(-40), UpdatedAt = delivered,
            StatusTimes = JsonSerializer.Serialize(new Dictionary<string, DateTime>
            { ["ready"] = delivered.AddMinutes(-15), ["delivered"] = delivered }),
            Total = 20000, Subtotal = 20000, GuestName = "Do not return this field",
            DeliveryFee = 3000, DeliveryCompletionSource = source,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var repo = new OrderRepository(db, new SystemUtcClock());

        var items = await repo.GetDeliveredDeliveryOrdersForDashboardAsync(
            branch.Id, delivered.AddDays(-1), delivered.AddDays(1));
        var item = Assert.Single(items);
        Assert.Equal(driver.Id, item.DeliveryManId);
        Assert.Equal("Driver", item.DeliveryMan?.Name);
        Assert.Null(item.GuestName);
        Assert.Equal(3000, item.DeliveryFee);
        Assert.Contains("delivered", item.StatusTimes);
        Assert.Equal(source, item.DeliveryCompletionSource);
        var summary = SenorArroz.Application.Features.Dashboard.Services.DeliveryDashboardAggregator.Build(
            items, [], delivered.AddDays(-1), delivered.AddDays(1));
        Assert.Equal(expectedMinutes, summary.AvgDeliveryMinutes);
        Assert.Equal(3000, Assert.Single(summary.Deliverymen).DeliveryFeeTotal);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class KitchenPreparationPredictionTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 17, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("2026-10-17T17:00:00Z")]
    [InlineData("2026-10-17T17:00:00+00:00")]
    [InlineData("2026-10-17T12:00:00-05:00")]
    public void StatusTimeOffsetsAreNormalizedToUtc(string timestamp)
    {
        var order = new SenorArroz.Domain.Entities.Order { StatusTimes = "{\"ready\":\"" + timestamp + "\"}" };
        var ready = order.GetStatusTimes()["ready"];
        Assert.Equal(Now, ready);
        Assert.Equal(DateTimeKind.Utc, ready.Kind);
    }

    [Fact]
    public async Task RemainingTimeConditionsOnElapsedPreparationAndRejectsOtherBranchHistory()
    {
        await using var db = CreateDb();
        for (var i = 1; i <= 12; i++)
        {
            var order = Order(i, Now.AddDays(-1));
            order.Status = OrderStatus.Delivered;
            order.AddStatusTime(OrderStatus.InPreparation, order.CreatedAt.AddMinutes(5));
            order.AddStatusTime(OrderStatus.Ready, order.CreatedAt.AddMinutes(i <= 6 ? 10 : 25));
            db.Orders.Add(order);
            var other = Order(i + 100, Now.AddDays(-1));
            other.BranchId = 2;
            other.AddStatusTime(OrderStatus.Ready, other.CreatedAt.AddSeconds(1));
            db.Orders.Add(other);
        }
        var current = Order(30, Now.AddMinutes(-18));
        current.AddStatusTime(OrderStatus.InPreparation, Now.AddMinutes(-10));
        db.Orders.Add(current);
        await db.SaveChangesAsync();
        var result = (await Service(db).EstimateAsync(1, [30], Now))[30];
        Assert.Equal(Now.AddMinutes(10), result.EstimatedReadyAtUtc);
        Assert.Equal(12, result.SampleSize);
        Assert.InRange(result.ReadyWithinWindowProbability, 0, 1);
        Assert.Equal("high", result.Confidence);
    }

    [Fact]
    public async Task OverdueFoodNeverBecomesReadyWithoutTheKitchenTransition()
    {
        await using var db = CreateDb();
        db.Orders.Add(Order(1, Now.AddHours(-3)));
        await db.SaveChangesAsync();
        var first = (await Service(db).EstimateAsync(1, [1], Now))[1];
        var next = (await Service(db).EstimateAsync(1, [1], Now.AddMinutes(10)))[1];
        Assert.True(first.EstimatedReadyAtUtc > Now);
        Assert.True(next.EstimatedReadyAtUtc > Now.AddMinutes(10));
        Assert.Equal("low", next.Confidence);
        Assert.True(next.ReadyWithinWindowProbability < .6);
    }

    [Fact]
    public async Task OverdueFallbackIsStableBetweenReadsButNeverPretendsFoodIsReady()
    {
        await using var db = CreateDb();
        db.Orders.Add(Order(1, Now.AddMinutes(-65)));
        await db.SaveChangesAsync();
        var first = (await Service(db).EstimateAsync(1, [1], Now))[1];
        var next = (await Service(db).EstimateAsync(1, [1], Now.AddSeconds(5)))[1];
        Assert.Equal(first.EstimatedReadyAtUtc, next.EstimatedReadyAtUtc);
        Assert.Equal(first.ConservativeReadyAtUtc, next.ConservativeReadyAtUtc);
        Assert.True(next.EstimatedReadyAtUtc > Now.AddSeconds(5));
    }

    [Fact]
    public async Task OneFastHistoricalOrderCannotPromiseAHighChanceOfReadiness()
    {
        await using var db = CreateDb();
        var history = Order(1, Now.AddDays(-1));
        history.Status = OrderStatus.Delivered;
        history.AddStatusTime(OrderStatus.InPreparation, history.CreatedAt);
        history.AddStatusTime(OrderStatus.Ready, history.CreatedAt.AddMinutes(10));
        db.Orders.Add(history);
        db.Orders.Add(Order(2, Now.AddMinutes(-5)));
        await db.SaveChangesAsync();
        var estimate = (await Service(db).EstimateAsync(1, [2], Now))[2];
        Assert.Equal("low", estimate.Confidence);
        Assert.True(estimate.ReadyWithinWindowProbability < .6);
    }

    [Fact]
    public async Task FutureReservationUsesPreparationAnchorAndReadyHasConfirmedProbability()
    {
        await using var db = CreateDb();
        var reservation = Order(1, Now.AddDays(-1));
        reservation.Type = OrderType.Reservation;
        reservation.PrepareAt = Now.AddHours(3);
        db.Orders.Add(reservation);
        var ready = Order(2, Now.AddMinutes(-20));
        ready.Status = OrderStatus.Ready;
        db.Orders.Add(ready);
        await db.SaveChangesAsync();
        var result = await Service(db).EstimateAsync(1, [1, 2], Now);
        Assert.True(result[1].EstimatedReadyAtUtc > reservation.PrepareAt);
        Assert.Equal(1, result[2].ReadyWithinWindowProbability);
        Assert.Equal("confirmed", result[2].Confidence);
    }

    private static Order Order(int id, DateTime created) => new()
    { Id = id, BranchId = 1, TakenById = 1, Type = OrderType.Delivery, Status = OrderStatus.InPreparation, CreatedAt = created };
    private static KitchenPreparationEstimator Service(ApplicationDbContext db) => new(db, Options.Create(new DeliveryRoutingOptions()));
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
}

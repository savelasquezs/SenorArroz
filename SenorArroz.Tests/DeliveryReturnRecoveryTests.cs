using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class DeliveryReturnRecoveryTests
{
    [Fact]
    public async Task ReturnRecoversVisitedStopsAndKeepsUnvisitedOrderPending()
    {
        await using var f = await Fixture.Create();
        var review = await f.Service.ReconcileReturnAsync(f.Latest);
        Assert.NotNull(review);
        Assert.Equal(new[] { 3 }, review.PendingOrderIds);
        Assert.Equal(new[] { 1, 2 }, f.Commands.Select(c => c.Id));
        Assert.All(f.Commands, c => Assert.True(c.IsAutomaticDelivery));
        Assert.Equal(OrderStatus.OnTheWay, f.Db.Orders.Single(x => x.Id == 3).Status);
        Assert.Equal(DeliveryRouteStatus.InProgress, f.Db.DeliveryRoutes.Single().Status);
    }

    [Fact]
    public async Task HumanConfirmationUsesNormalDeliveryAndCompletesRoute()
    {
        await using var f = await Fixture.Create();
        var review = await f.Service.ReconcileReturnAsync(f.Latest);
        await f.Service.ConfirmReturnedOrdersAsync(8, 7, 1, review!.ReturnLocationId, [3]);
        Assert.Equal(3, f.Commands.Count);
        Assert.False(f.Commands.Last().IsAutomaticDelivery);
        Assert.Equal(DeliveryRouteStatus.Completed, f.Db.DeliveryRoutes.Single().Status);
        await f.Service.ConfirmReturnedOrdersAsync(8, 7, 1, review.ReturnLocationId, [3]);
        Assert.Equal(3, f.Commands.Count);
    }

    [Fact]
    public async Task ReturnReviewCannotDeliverReassignedOrderOrAnotherDriver()
    {
        await using var f = await Fixture.Create();
        var review = await f.Service.ReconcileReturnAsync(f.Latest);
        await Assert.ThrowsAsync<BusinessException>(() =>
            f.Service.ConfirmReturnedOrdersAsync(8, 100, 1, review!.ReturnLocationId, [3]));
        f.Db.Orders.Single(x => x.Id == 3).DeliveryManId = 100;
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() =>
            f.Service.ConfirmReturnedOrdersAsync(8, 7, 1, review!.ReturnLocationId, [3]));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; private init; } = null!;
        public DeliveryAutoCompletionService Service { get; private set; } = null!;
        public DeliverymanLocation Latest { get; private set; } = null!;
        public List<ChangeOrderStatusCommand> Commands { get; } = [];

        public static async Task<Fixture> Create()
        {
            var start = DateTime.UtcNow.AddMinutes(10);
            var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                currentTenant: TestTenantContext.Default,
                tenantExecutionContext: TestTenantContext.Default);
            var f = new Fixture { Db = db };
            db.Branches.Add(new Branch
            {
                Id = 1, Name = "Origin", Address = "A", Phone1 = "3000000000",
                Latitude = Lat(-500), Longitude = -75m,
                DeliveryTrackingAllowedDistanceMeters = 50,
            });
            db.DeliveryRoutes.Add(new DeliveryRoute
            {
                Id = 8, BranchId = 1, DeliverymanId = 7,
                Status = DeliveryRouteStatus.InProgress, RouteStartedAtUtc = start,
                LastAssignmentAtUtc = start
            });
            db.DeliveryWorkSessions.Add(new DeliveryWorkSession
            {
                Id = 9, BranchId = 1, DeliverymanId = 7,
                Status = DeliveryWorkSessionStatus.Active,
                StartedAt = start, AutoCloseAt = start.AddHours(4),
                DeviceInstallationId = "test", DevicePlatform = "android",
                LastCommunicationAt = start
            });
            foreach (var (id, meters) in new[] { (1, 0), (2, 300), (3, 3000) })
            {
                db.Addresses.Add(new Address
                {
                    Id = id, CustomerId = 10, AddressText = "Test",
                    Latitude = Lat(meters), Longitude = -75m
                });
                db.Orders.Add(new Order
                {
                    Id = id, AddressId = id, BranchId = 1, TakenById = 7,
                    DeliveryManId = 7, DeliveryRouteId = 8,
                    Type = OrderType.Delivery, Status = OrderStatus.OnTheWay,
                    StatusTimes = "{}"
                });
                db.DeliveryRouteStops.Add(new DeliveryRouteStop
                {
                    Id = id, OrderId = id, DeliveryRouteId = 8, StopSequence = id
                });
            }
            foreach (var (seconds, meters) in new[]
                { (0, -500), (30, -200), (60, 0), (90, 10), (120, 300),
                  (150, 310), (180, 600), (240, -500), (270, -495) })
            {
                f.Latest = new DeliverymanLocation
                {
                    Id = seconds + 100, DeliverymanId = 7, DeliveryRouteId = 8,
                    WorkSessionId = 9, RecordedAt = start.AddSeconds(seconds),
                    SyncedAt = start.AddSeconds(270), Latitude = Lat(meters),
                    Longitude = -75m, AccuracyMeters = 10, GpsEnabled = true,
                    ClientPointId = Guid.NewGuid()
                };
                db.DeliverymanLocations.Add(f.Latest);
            }
            await db.SaveChangesAsync();
            var sender = new Mock<ISender>();
            sender.Setup(s => s.Send(It.IsAny<ChangeOrderStatusCommand>(),
                It.IsAny<CancellationToken>()))
                .Returns<ChangeOrderStatusCommand, CancellationToken>(async (command, ct) =>
                {
                    f.Commands.Add(command);
                    var order = (await db.Orders.FindAsync([command.Id], ct))!;
                    order.Status = command.StatusChange.Status;
                    var stop = await db.DeliveryRouteStops.SingleAsync(
                        s => s.OrderId == order.Id, ct);
                    if (command.IsAutomaticDelivery)
                    {
                        stop.AutoDeliveredAtUtc = command.AutoDeliveredAtUtc;
                        stop.AutoDeliveryTriggerLocationId = command.AutoDeliveryTriggerLocationId;
                        stop.AutoDeliveryDepartureDistanceMeters = command.AutoDeliveryDepartureDistanceMeters;
                    }
                    await db.SaveChangesAsync(ct);
                    return new OrderDto { Id = order.Id, Status = order.Status };
                });
            f.Service = new DeliveryAutoCompletionService(db, sender.Object,
                new Clock(start.AddSeconds(270)),
                NullLogger<DeliveryAutoCompletionService>.Instance,
                new InlineRouteLock());
            return f;
        }

        private static decimal Lat(double meters) =>
            6m + (decimal)(meters / 111_195d);
        private sealed class Clock(DateTime now) : IClock
        {
            public DateTime UtcNow => now;
        }
        private sealed class InlineRouteLock : IDeliveryAutoCompletionRouteLock
        {
            public Task ExecuteAsync(int id, Func<CancellationToken, Task> action,
                CancellationToken ct = default) => action(ct);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}

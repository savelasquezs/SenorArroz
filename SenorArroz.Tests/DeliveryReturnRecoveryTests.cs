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
using SenorArroz.Application.Features.Auth.Commands;
using SenorArroz.Application.Features.Deliverymen.Commands;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Tests;

public sealed class DeliveryReturnRecoveryTests
{
    [Fact]
    public async Task ReturnKeepsPendingOrdersAndRouteOpen()
    {
        await using var f = await Fixture.Create();
        Assert.Null(await f.Service.ReconcileReturnAsync(f.Latest));
        Assert.Empty(f.Commands);
        Assert.All(f.Db.Orders, o => Assert.Equal(OrderStatus.OnTheWay, o.Status));
        Assert.Equal(DeliveryRouteStatus.InProgress, f.Db.DeliveryRoutes.Single().Status);
    }

    [Fact]
    public async Task RepeatedReturnAndLegacyConfirmationHaveNoDuplicateEffects()
    {
        await using var f = await Fixture.Create();
        await f.DeliverAll();
        await f.Service.ReconcileReturnAsync(f.Latest);
        await f.Service.ReconcileReturnAsync(f.Latest);
        await f.Service.ConfirmReturnedOrdersAsync(8, 7, 1, f.Latest.Id, [3]);
        Assert.Empty(f.Commands);
        Assert.Equal("return", f.Db.DeliveryRoutes.Single().ClosureSource);
        Assert.Single(f.Db.EntityAuditLogs);
    }

    [Fact]
    public async Task ReturnPreservesReassignedOrdersAndRejectsAnotherDriver()
    {
        await using var f = await Fixture.Create();
        f.Db.Orders.Single(x => x.Id == 3).DeliveryManId = 100;
        await f.Db.SaveChangesAsync();
        await f.Service.ReconcileReturnAsync(f.Latest);
        Assert.Equal(OrderStatus.OnTheWay, f.Db.Orders.Single(x => x.Id == 3).Status);
        Assert.DoesNotContain(f.Commands, c => c.Id == 3);
        await Assert.ThrowsAsync<BusinessException>(() =>
            f.Service.ConfirmReturnedOrdersAsync(8, 100, 1, f.Latest.Id, [3]));
    }

    [Fact]
    public async Task PresenceAtBranchWithoutPriorDepartureCannotCloseRoute()
    {
        await using var f = await Fixture.Create();
        await f.DeliverAll();
        var branch = await f.Db.Branches.SingleAsync();
        f.Db.DeliverymanLocations.RemoveRange(f.Db.DeliverymanLocations.Where(p => p.Latitude != branch.Latitude));
        await f.Db.SaveChangesAsync();
        await f.Service.ReconcileReturnAsync(f.Latest);
        Assert.Equal(DeliveryRouteStatus.InProgress, f.Db.DeliveryRoutes.Single().Status);
        Assert.Empty(f.Db.EntityAuditLogs);
    }

    [Theory]
    [InlineData("next_route")]
    [InlineData("logout")]
    public async Task OperationalClosureRequiresDeliveredOrdersAndDoesNotInventGps(string source)
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<BusinessException>(() => f.Operations.CompleteActiveAsync(source, null, default));
        Assert.All(f.Db.Orders, o => Assert.Equal(OrderStatus.OnTheWay, o.Status));
        Assert.Equal(DeliveryRouteStatus.InProgress, f.Db.DeliveryRoutes.Single().Status);
        Assert.Empty(f.Db.EntityAuditLogs);
        await f.DeliverAll();
        await f.Operations.CompleteActiveAsync(source, null, default);
        await f.Operations.CompleteActiveAsync(source, null, default);
        var route = f.Db.DeliveryRoutes.Single();
        Assert.Equal(DeliveryRouteStatus.Completed, route.Status);
        Assert.Equal(source, route.ClosureSource);
        Assert.Null(route.ActualDurationSeconds);
        Assert.Null(route.MetSla);
        Assert.Null(SenorArroz.Application.Features.Dashboard.Services.DeliveryPerformanceAnalytics.RouteTiming(route).Start);
        Assert.Empty(f.Commands);
        Assert.Single(f.Db.EntityAuditLogs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogoutChecksDeliveriesBeforeRevokingTokenOrClosingWorkSession(bool delivered)
    {
        await using var f = await Fixture.Create();
        if (delivered) await f.DeliverAll();
        var owner = f.Db.Users.Single();
        owner.ActiveSessionId = Guid.NewGuid();
        await f.Db.SaveChangesAsync();
        var token = new RefreshToken { UserId = 7, TenantId = 1, SessionId = owner.ActiveSessionId,
            Token = "test", ExpiresAt = f.Latest.RecordedAt.AddDays(1) };
        var refresh = new Mock<IRefreshTokenRepository>();
        refresh.Setup(x => x.GetByTokenAsync("test", It.IsAny<CancellationToken>())).ReturnsAsync(token);
        var auth = new Mock<IAuthRepository>();
        var handler = new LogoutHandler(refresh.Object, auth.Object, new FakeClock(f.Latest.RecordedAt),
            f.Db, f.Operations, f.Actor);
        var command = new LogoutCommand { RefreshToken = "test" };
        if (delivered) Assert.True(await handler.Handle(command, default));
        else await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(command, default));
        Assert.Equal(delivered, token.IsRevoked);
        Assert.Equal(delivered ? DeliveryWorkSessionStatus.Closed : DeliveryWorkSessionStatus.Active,
            f.Db.DeliveryWorkSessions.Single().Status);
        Assert.Equal(delivered ? DeliveryRouteStatus.Completed : DeliveryRouteStatus.InProgress,
            f.Db.DeliveryRoutes.Single().Status);
        auth.Verify(x => x.EndSessionIfCurrentAsync(7, owner.ActiveSessionId!.Value, It.IsAny<CancellationToken>()),
            delivered ? Times.Once() : Times.Never());
        Assert.Empty(f.Commands);
    }

    [Fact]
    public async Task ClosingWorkSessionCannotBypassPendingDeliveries()
    {
        await using var f = await Fixture.Create();
        var handler = new CloseMyDeliveryWorkSessionHandler(f.Db, f.Actor, new FakeClock(f.Latest.RecordedAt),
            operations: f.Operations);
        await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(new(), default));
        Assert.Equal(DeliveryWorkSessionStatus.Active, f.Db.DeliveryWorkSessions.Single().Status);
        await f.DeliverAll();
        await handler.Handle(new(), default);
        Assert.Equal(DeliveryWorkSessionStatus.Closed, f.Db.DeliveryWorkSessions.Single().Status);
        Assert.Equal("logout", f.Db.DeliveryRoutes.Single().ClosureSource);
    }

    [Fact]
    public async Task LogoutFromReplacedSessionCannotCloseCurrentRouteOrWorkSession()
    {
        await using var f = await Fixture.Create();
        await f.DeliverAll();
        f.Db.Users.Single().ActiveSessionId = Guid.NewGuid();
        await f.Db.SaveChangesAsync();
        var token = new RefreshToken { UserId = 7, TenantId = 1, SessionId = Guid.NewGuid(),
            ExpiresAt = f.Latest.RecordedAt.AddDays(1), Token = "old" };
        var refresh = new Mock<IRefreshTokenRepository>();
        refresh.Setup(x => x.GetByTokenAsync("old", It.IsAny<CancellationToken>())).ReturnsAsync(token);
        var handler = new LogoutHandler(refresh.Object, Mock.Of<IAuthRepository>(), new FakeClock(f.Latest.RecordedAt),
            f.Db, f.Operations, f.Actor);
        Assert.True(await handler.Handle(new() { RefreshToken = "old" }, default));
        Assert.Equal(DeliveryRouteStatus.InProgress, f.Db.DeliveryRoutes.Single().Status);
        Assert.Equal(DeliveryWorkSessionStatus.Active, f.Db.DeliveryWorkSessions.Single().Status);
        Assert.Empty(f.Db.EntityAuditLogs);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; private init; } = null!;
        public DeliveryAutoCompletionService Service { get; private set; } = null!;
        public DeliverymanLocation Latest { get; private set; } = null!;
        public List<ChangeOrderStatusCommand> Commands { get; } = [];
        public DeliveryOperationsService Operations { get; private set; } = null!;
        public ICurrentUser Actor { get; private set; } = null!;
        public async Task DeliverAll()
        {
            foreach (var order in Db.Orders) order.Status = OrderStatus.Delivered;
            await Db.SaveChangesAsync();
        }

        public static async Task<Fixture> Create()
        {
            var start = DateTime.UtcNow.AddMinutes(10);
            var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                currentTenant: TestTenantContext.Default,
                tenantExecutionContext: TestTenantContext.Default);
            var f = new Fixture { Db = db };
            db.Users.Add(new User { Id = 7, BranchId = 1, Role = UserRole.Deliveryman });
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
            var actor = new Mock<ICurrentUser>();
            actor.SetupGet(x => x.Id).Returns(7);
            actor.SetupGet(x => x.TenantId).Returns(1);
            actor.SetupGet(x => x.BranchId).Returns(1);
            var operations = new DeliveryOperationsService(db, actor.Object, new Clock(start.AddSeconds(270)),
                sender.Object, Mock.Of<AutoMapper.IMapper>(), new InlineRouteLock());
            f.Operations = operations;
            f.Actor = actor.Object;
            f.Service = new DeliveryAutoCompletionService(db, sender.Object,
                new Clock(start.AddSeconds(270)),
                NullLogger<DeliveryAutoCompletionService>.Instance,
                new InlineRouteLock(), operations: operations);
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

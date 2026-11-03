using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
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

public sealed class DeliveryOperationsNextRouteConfirmationTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task NewRouteCannotImplicitlyDeliverPreviousOrders()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<OrderConcurrencyException>(() =>
            f.Service.ExecuteAsync(f.Take(), default));
        Assert.Equal(OrderStatus.OnTheWay, (await f.Db.Orders.SingleAsync(x => x.Id == 30)).Status);
        Assert.Empty(f.DeliveredIds);
        Assert.Equal(0, f.NewRouteAttempts);
        Assert.Empty(f.Db.EntityAuditLogs);
    }

    [Fact]
    public async Task ExplicitMatchingConfirmationDeliversAndAcceptsNewRouteExactlyOnce()
    {
        await using var f = await Fixture.CreateAsync();
        var request = f.Take([new ConfirmedPreviousDelivery(30, 3)]);
        var first = await f.Service.ExecuteAsync(request, default);
        Assert.False(first.Replayed);
        Assert.Equal([30], f.DeliveredIds);
        Assert.Equal(1, f.NewRouteAttempts);
        var completed = await f.Db.Orders.SingleAsync(x => x.Id == 30);
        Assert.Equal(OrderStatus.Delivered, completed.Status);
        Assert.Equal("next_route", completed.DeliveryCompletionSource);
        Assert.Single(f.Db.EntityAuditLogs.Where(x => x.EntityType == "delivery_operation"));

        var replay = await f.Service.ExecuteAsync(request, default);
        Assert.True(replay.Replayed);
        Assert.Equal([30], f.DeliveredIds);
        Assert.Equal(1, f.NewRouteAttempts);
    }

    [Fact]
    public async Task ChangedVersionCannotConfirmOrTake()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<OrderConcurrencyException>(() =>
            f.Service.ExecuteAsync(f.Take([new ConfirmedPreviousDelivery(30, 2)]), default));
        Assert.Empty(f.DeliveredIds);
        Assert.Equal(0, f.NewRouteAttempts);
    }

    [Fact]
    public async Task UnlistedPreviousOrdersCannotBeSilentlyCompleted()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.Orders.Add(new Order
        {
            Id = 32, TakenById = 7, BranchId = 7, DeliveryManId = 2,
            DeliveryRouteId = 20, Type = OrderType.Delivery,
            Status = OrderStatus.OnTheWay, ManagementVersion = 4, StatusTimes = "{}"
        });
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<OrderConcurrencyException>(() =>
            f.Service.ExecuteAsync(f.Take([new ConfirmedPreviousDelivery(30, 3)]), default));
        Assert.Empty(f.DeliveredIds);
        Assert.Equal(0, f.NewRouteAttempts);
    }

    [Fact]
    public async Task OtherDriversAndDuplicateIdsCannotBeConfirmed()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<OrderConcurrencyException>(() =>
            f.Service.ExecuteAsync(f.Take([
                new ConfirmedPreviousDelivery(30, 3),
                new ConfirmedPreviousDelivery(31, 1)
            ]), default));
        await Assert.ThrowsAsync<OrderConcurrencyException>(() =>
            f.Service.ExecuteAsync(f.Take([
                new ConfirmedPreviousDelivery(30, 3),
                new ConfirmedPreviousDelivery(30, 3)
            ]), default));
        Assert.Empty(f.DeliveredIds);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; private init; } = null!;
        public DeliveryOperationsService Service { get; private init; } = null!;
        public List<int> DeliveredIds { get; } = [];
        public int NewRouteAttempts { get; private set; }

        public DeliveryOperationRequest Take(IReadOnlyList<ConfirmedPreviousDelivery>? confirmed = null) =>
            new(Guid.NewGuid(), "take", Now, RouteId: 20, WorkSessionId: 10,
                Selection: new SenorArroz.Application.Features.Orders.DTOs.SelfAssignOrdersDto
                { OrderIds = [31] }, ConfirmedPreviousDeliveries: confirmed);

        public static async Task<Fixture> CreateAsync()
        {
            var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                currentTenant: TestTenantContext.Default,
                tenantExecutionContext: TestTenantContext.Default);
            db.Branches.Add(new Branch { Id = 7, Name = "Santander", Address = "Sucursal" });
            db.Users.Add(new User
            {
                Id = 2, BranchId = 7, Role = UserRole.Deliveryman,
                Active = true, Name = "Courier", Email = "courier@example.invalid"
            });
            db.DeliveryWorkSessions.Add(new DeliveryWorkSession
            {
                Id = 10, BranchId = 7, DeliverymanId = 2,
                StartedAt = Now.AddHours(-1), AutoCloseAt = Now.AddHours(6),
                LastCommunicationAt = Now.AddMinutes(-3),
                Status = DeliveryWorkSessionStatus.Active,
                DeviceInstallationId = "device-a", DevicePlatform = "android"
            });
            db.DeliveryRoutes.Add(new DeliveryRoute
            {
                Id = 20, BranchId = 7, DeliverymanId = 2,
                WorkSessionId = 10, Status = DeliveryRouteStatus.InProgress,
                LastAssignmentAtUtc = Now.AddMinutes(-40)
            });
            db.Orders.AddRange(
                new Order
                {
                    Id = 30, TakenById = 2, BranchId = 7, DeliveryManId = 2,
                    DeliveryRouteId = 20, Type = OrderType.Delivery,
                    Status = OrderStatus.OnTheWay, ManagementVersion = 3, StatusTimes = "{}"
                },
                new Order
                {
                    Id = 31, TakenById = 2, BranchId = 7, Type = OrderType.Delivery,
                    Status = OrderStatus.Ready, StatusTimes = "{}"
                });
            db.DeliveryRouteStops.Add(new DeliveryRouteStop
                { Id = 40, OrderId = 30, DeliveryRouteId = 20, StopSequence = 1 });
            await db.SaveChangesAsync();
            var f = new Fixture { Db = db };
            var sender = new Mock<ISender>();
            sender.Setup(x => x.Send(It.IsAny<ChangeOrderStatusCommand>(), It.IsAny<CancellationToken>()))
                .Returns<ChangeOrderStatusCommand, CancellationToken>(async (command, ct) =>
                {
                    f.DeliveredIds.Add(command.Id);
                    var order = await db.Orders.SingleAsync(o => o.Id == command.Id, ct);
                    order.Status = command.StatusChange.Status;
                    order.DeliveryCompletionSource = command.RouteCompletionSource;
                    await db.SaveChangesAsync(ct);
                    return new OrderDto { Id = order.Id, Status = order.Status };
                });
            sender.Setup(x => x.Send(It.IsAny<SelfAssignOrdersCommand>(), It.IsAny<CancellationToken>()))
                .Returns<SelfAssignOrdersCommand, CancellationToken>((command, ct) =>
                {
                    Assert.Equal([31], command.OrderIds);
                    Assert.True(command.ReplaceActiveRoute);
                    Assert.Equal(OrderStatus.Delivered,
                        db.Orders.Single(x => x.Id == 30).Status);
                    f.NewRouteAttempts++;
                    return Task.FromResult(new List<OrderDto>());
                });
            var actor = new Mock<ICurrentUser>();
            actor.SetupGet(x => x.Id).Returns(2);
            actor.SetupGet(x => x.TenantId).Returns(1);
            actor.SetupGet(x => x.BranchId).Returns(7);
            var mapper = new Mock<IMapper>();
            mapper.Setup(x => x.Map<List<OrderDto>>(It.IsAny<object>()))
                .Returns<object>(source => ((IEnumerable<Order>)source)
                    .Select(o => new OrderDto { Id = o.Id, Status = o.Status }).ToList());
            f.Service = new DeliveryOperationsService(db, actor.Object,
                new FixedClock(), sender.Object, mapper.Object, new InlineRouteLock());
            return f;
        }

        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow => Now;
        }

        private sealed class InlineRouteLock : IDeliveryAutoCompletionRouteLock
        {
            public Task ExecuteAsync(int id, Func<CancellationToken, Task> action,
                CancellationToken ct = default) => action(ct);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}

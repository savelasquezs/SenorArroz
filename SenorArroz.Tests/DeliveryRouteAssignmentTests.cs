using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class DeliveryRouteAssignmentTests
{
    private static readonly DateTime Now = new(2026, 7, 21, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Assignment_WakesConsolidationAndSchedulesExactDueTime()
    {
        await using var db = CreateDb();
        db.Branches.Add(Branch());
        db.Orders.Add(DeliveryOrder(10, 7));
        await db.SaveChangesAsync();
        var signal = new Mock<IBackgroundWorkSignal<DeliveryRouteConsolidationWork>>();
        var service = new DeliveryRouteWorkflowService(db, Mock.Of<IGoogleRoutesDrivingMetricsService>(),
            Options.Create(new DeliveryRouteOptions { ConsolidationDelaySeconds = 180 }),
            NullLogger<DeliveryRouteWorkflowService>.Instance, new FakeClock(Now), signal.Object);
        await service.OnOrderAssignedToDeliverymanAsync(db.Orders.Single());
        Assert.Equal(Now.AddSeconds(180), await service.GetNextPendingConsolidationAtAsync());
        signal.Verify(x => x.Pulse(), Times.Once);
    }

    [Fact]
    public async Task Consolidation_UsesOneRoundTripRequest_AndAddsPerOrderBuffer()
    {
        await using var db = CreateDb();
        db.Branches.Add(new Branch { Id = 1, Name = "Centro", Address = "Sucursal", Latitude = 7.125m, Longitude = -73.120m });
        db.Addresses.Add(new Address { Id = 50, CustomerId = 1, NeighborhoodId = 1,
            AddressText = "Calle 119 # 64C-47", Latitude = 7.135m, Longitude = -73.130m });
        var order = DeliveryOrder(10, 7, 30);
        order.AddressId = 50;
        db.Orders.Add(order);
        db.DeliveryRoutes.Add(new DeliveryRoute { Id = 30, DeliverymanId = 7, BranchId = 1,
            Status = DeliveryRouteStatus.Open, LastAssignmentAtUtc = Now.AddMinutes(-5) });
        db.DeliveryRouteStops.Add(Stop(40, 30, 10, 1));
        await db.SaveChangesAsync();
        var google = new Mock<IGoogleRoutesDrivingMetricsService>();
        google.Setup(g => g.ComputeRouteAsync(It.Is<IReadOnlyList<(double Latitude, double Longitude)>>(p => p.Count == 3),
            It.IsAny<CancellationToken>())).ReturnsAsync(new DrivingRouteMetrics(4000, 900, 2000, 450));
        var service = new DeliveryRouteWorkflowService(db, google.Object, Options.Create(new DeliveryRouteOptions()),
            NullLogger<DeliveryRouteWorkflowService>.Instance, new FakeClock(Now));
        await service.ConsolidatePendingRoutesAsync();
        var planned = await db.DeliveryRoutes.SingleAsync();
        Assert.Equal(DeliveryRouteStatus.InProgress, planned.Status);
        Assert.Equal(2000, planned.PlannedDistanceMeters);
        Assert.Equal(2000, planned.ReturnToBranchMeters);
        Assert.Equal(900, planned.PlannedDrivingDurationSeconds);
        Assert.Equal(1140, planned.MetaDurationSeconds);
        Assert.Null(planned.DepartedAtUtc);
        google.Verify(g => g.ComputeRouteAsync(It.IsAny<IReadOnlyList<(double Latitude, double Longitude)>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assignment_DoesNotReuseInProgressRouteFromPreviousColombiaDay()
    {
        await using var db = CreateDb();
        SeedTrip(db, OrderStatus.OnTheWay, Now.AddDays(-10));
        db.DeliveryRoutes.Single().LastAssignmentAtUtc = Now;
        db.Orders.Add(DeliveryOrder(11, 7));
        await db.SaveChangesAsync();
        await CreateService(db).OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 11));
        var assigned = await db.Orders.SingleAsync(o => o.Id == 11);
        Assert.NotEqual(30, assigned.DeliveryRouteId);
        Assert.Equal(2, await db.DeliveryRoutes.CountAsync());
        Assert.Equal(DeliveryRouteStatus.Open, db.DeliveryRoutes.Single(r => r.Id == assigned.DeliveryRouteId).Status);
    }

    [Fact]
    public async Task Assignment_AppendsOrderToInProgressRoute_WithoutRestartingClock()
    {
        await using var db = CreateDb();
        var start = Now.AddMinutes(-20);
        SeedTrip(db, OrderStatus.OnTheWay, start);
        db.Orders.Add(DeliveryOrder(11, 7));
        await db.SaveChangesAsync();
        await CreateService(db).OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 11));
        var route = await db.DeliveryRoutes.Include(r => r.Stops).SingleAsync();
        Assert.Equal(30, db.Orders.Single(o => o.Id == 11).DeliveryRouteId);
        Assert.Equal(2, route.Stops.Count);
        Assert.Equal(start, route.RouteStartedAtUtc);
        Assert.Equal(Now, route.LastAssignmentAtUtc);
        Assert.Equal(2, route.StopCount);
        Assert.Null(route.DepartedAtUtc);
    }

    [Fact]
    public async Task Assignment_ClosesTerminalRouteAdministratively_WithoutInventingReturnTime()
    {
        await using var db = CreateDb();
        SeedTrip(db, OrderStatus.Delivered, Now.AddMinutes(-20));
        db.DeliveryRoutes.Single().MetaDurationSeconds = 900;
        db.Orders.Add(DeliveryOrder(11, 7));
        await db.SaveChangesAsync();
        await CreateService(db).OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 11));
        var routes = await db.DeliveryRoutes.Include(r => r.Stops).ToListAsync();
        var previous = routes.Single(r => r.Id == 30);
        var next = routes.Single(r => r.Id != 30);
        Assert.Equal(DeliveryRouteStatus.Completed, previous.Status);
        Assert.Null(previous.CompletedAtUtc);
        Assert.Null(previous.ActualDurationSeconds);
        Assert.Null(previous.MetSla);
        Assert.Equal(10, Assert.Single(previous.Stops).OrderId);
        Assert.Equal(DeliveryRouteStatus.Open, next.Status);
        Assert.Equal(11, Assert.Single(next.Stops).OrderId);
        Assert.Equal(next.Id, db.Orders.Single(o => o.Id == 11).DeliveryRouteId);
        Assert.Equal("administrative_closure", Assert.Single(db.EntityAuditLogs).OperationType);
    }

    [Fact]
    public async Task Assignment_AppendsToMixedRoute_WhenAnotherOrderIsOnTheWay()
    {
        await using var db = CreateDb();
        var start = Now.AddMinutes(-20);
        SeedTrip(db, OrderStatus.Delivered, start);
        db.Orders.AddRange(DeliveryOrder(11, 7), DeliveryOrder(12, 7, 30));
        db.DeliveryRouteStops.Add(Stop(41, 30, 12, 2));
        await db.SaveChangesAsync();
        await CreateService(db).OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 11));
        var route = await db.DeliveryRoutes.Include(r => r.Stops).SingleAsync();
        Assert.Equal(DeliveryRouteStatus.InProgress, route.Status);
        Assert.Equal(30, db.Orders.Single(o => o.Id == 11).DeliveryRouteId);
        Assert.Equal(3, route.Stops.Count);
        Assert.Equal(start, route.RouteStartedAtUtc);
        Assert.Equal(Now, route.LastAssignmentAtUtc);
    }

    [Fact]
    public async Task Assignment_LeavesFullyCancelledRouteCancelled_WithoutPretendingGpsReturn()
    {
        await using var db = CreateDb();
        SeedTrip(db, OrderStatus.Cancelled, Now.AddMinutes(-20));
        db.Orders.Add(DeliveryOrder(11, 7));
        await db.SaveChangesAsync();
        await CreateService(db).OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 11));
        var routes = await db.DeliveryRoutes.Include(r => r.Stops).ToListAsync();
        var previous = routes.Single(r => r.Id == 30);
        Assert.Equal(DeliveryRouteStatus.Cancelled, previous.Status);
        Assert.Null(previous.CompletedAtUtc);
        Assert.Single(previous.Stops);
        var next = routes.Single(r => r.Id != 30);
        Assert.Equal(DeliveryRouteStatus.Open, next.Status);
        Assert.Equal(11, next.Stops.Single().OrderId);
    }

    [Fact]
    public async Task ConsecutiveAssignments_GroupNewOrdersInSingleNewRoute()
    {
        await using var db = CreateDb();
        SeedTrip(db, OrderStatus.Delivered, Now.AddMinutes(-20));
        db.Orders.AddRange(DeliveryOrder(11, 7), DeliveryOrder(12, 7));
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await service.OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 11));
        await service.OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 12));
        var routes = await db.DeliveryRoutes.Include(r => r.Stops).ToListAsync();
        Assert.Equal(DeliveryRouteStatus.Completed, routes.Single(r => r.Id == 30).Status);
        var next = routes.Single(r => r.Id != 30);
        Assert.Equal(DeliveryRouteStatus.Open, next.Status);
        Assert.Equal(new[] { 11, 12 }, next.Stops.OrderBy(s => s.StopSequence).Select(s => s.OrderId));
    }

    private static Branch Branch() => new() { Id = 1, Name = "Centro", Address = "Sucursal" };
    private static DeliveryRouteStop Stop(int id, int routeId, int orderId, int sequence) =>
        new() { Id = id, DeliveryRouteId = routeId, OrderId = orderId, StopSequence = sequence };
    private static void SeedTrip(ApplicationDbContext db, OrderStatus status, DateTime start)
    {
        db.Branches.Add(Branch());
        db.Orders.Add(DeliveryOrder(10, 7, 30, status));
        db.DeliveryRoutes.Add(new DeliveryRoute { Id = 30, BranchId = 1, DeliverymanId = 7,
            Status = DeliveryRouteStatus.InProgress, LastAssignmentAtUtc = start.AddMinutes(-3), RouteStartedAtUtc = start });
        db.DeliveryRouteStops.Add(Stop(40, 30, 10, 1));
    }
    private static DeliveryRouteWorkflowService CreateService(ApplicationDbContext db) =>
        new(db, Mock.Of<IGoogleRoutesDrivingMetricsService>(), Options.Create(new DeliveryRouteOptions()),
            NullLogger<DeliveryRouteWorkflowService>.Instance, new FakeClock(Now));
    private static Order DeliveryOrder(int id, int deliverymanId, int? routeId = null,
        OrderStatus status = OrderStatus.OnTheWay) => new()
    { Id = id, BranchId = 1, TakenById = 1, Type = OrderType.Delivery, Status = status,
        DeliveryManId = deliverymanId, DeliveryRouteId = routeId };
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
}

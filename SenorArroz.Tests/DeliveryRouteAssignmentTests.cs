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
    [Fact]
    public async Task Consolidation_UsesOneRoundTripRequest_AndAddsPerOrderBuffer()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 21, 18, 0, 0, DateTimeKind.Utc);
        db.Branches.Add(new Branch
        {
            Id = 1,
            Name = "Centro",
            Address = "Sucursal",
            Latitude = 7.125m,
            Longitude = -73.120m,
        });
        db.Addresses.Add(new Address
        {
            Id = 50,
            CustomerId = 1,
            NeighborhoodId = 1,
            AddressText = "Calle 119 # 64C-47",
            Latitude = 7.135m,
            Longitude = -73.130m,
        });
        var order = DeliveryOrder(10, deliverymanId: 7, routeId: 30);
        order.AddressId = 50;
        db.Orders.Add(order);
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 30,
            DeliverymanId = 7,
            BranchId = 1,
            Status = DeliveryRouteStatus.Open,
            LastAssignmentAtUtc = now.AddMinutes(-5),
        });
        db.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            Id = 40,
            DeliveryRouteId = 30,
            OrderId = 10,
            StopSequence = 1,
        });
        await db.SaveChangesAsync();
        var routes = new Mock<IGoogleRoutesDrivingMetricsService>();
        routes.Setup(x => x.ComputeRouteAsync(
                It.Is<IReadOnlyList<(double Latitude, double Longitude)>>(p => p.Count == 3),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DrivingRouteMetrics(4000, 900, 2000, 450));
        var service = new DeliveryRouteWorkflowService(
            db,
            routes.Object,
            Options.Create(new DeliveryRouteOptions()),
            NullLogger<DeliveryRouteWorkflowService>.Instance,
            new FakeClock(now));

        await service.ConsolidatePendingRoutesAsync();

        var planned = await db.DeliveryRoutes.SingleAsync();
        Assert.Equal(DeliveryRouteStatus.InProgress, planned.Status);
        Assert.Equal(2000, planned.PlannedDistanceMeters);
        Assert.Equal(2000, planned.ReturnToBranchMeters);
        Assert.Equal(900, planned.PlannedDrivingDurationSeconds);
        Assert.Equal(1140, planned.MetaDurationSeconds);
        routes.Verify(x => x.ComputeRouteAsync(
            It.IsAny<IReadOnlyList<(double Latitude, double Longitude)>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assignment_DoesNotReuseInProgressRouteFromPreviousColombiaDay()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 21, 17, 30, 0, DateTimeKind.Utc);
        db.Branches.Add(new Branch { Id = 1, Name = "Centro", Address = "Sucursal" });
        db.Orders.AddRange(
            DeliveryOrder(10, deliverymanId: 7, routeId: 30),
            DeliveryOrder(11, deliverymanId: 7));
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 30,
            DeliverymanId = 7,
            BranchId = 1,
            Status = DeliveryRouteStatus.InProgress,
            LastAssignmentAtUtc = now,
            RouteStartedAtUtc = now.AddDays(-10),
        });
        db.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            Id = 40,
            DeliveryRouteId = 30,
            OrderId = 10,
            StopSequence = 1,
        });
        await db.SaveChangesAsync();
        var service = new DeliveryRouteWorkflowService(
            db,
            Mock.Of<IGoogleRoutesDrivingMetricsService>(),
            Options.Create(new DeliveryRouteOptions()),
            NullLogger<DeliveryRouteWorkflowService>.Instance,
            new FakeClock(now));

        await service.OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 11));

        var assigned = await db.Orders.SingleAsync(o => o.Id == 11);
        Assert.NotEqual(30, assigned.DeliveryRouteId);
        Assert.Equal(2, await db.DeliveryRoutes.CountAsync());
        Assert.Equal(DeliveryRouteStatus.Open,
            (await db.DeliveryRoutes.SingleAsync(r => r.Id == assigned.DeliveryRouteId)).Status);
    }

    [Fact]
    public async Task Assignment_AppendsOrderToInProgressRoute_WithoutRestartingClock()
    {
        await using var db = CreateDb();
        var originalStart = new DateTime(2026, 7, 21, 15, 0, 0, DateTimeKind.Utc);
        var now = originalStart.AddMinutes(20);

        db.Branches.Add(new Branch { Id = 1, Name = "Centro", Address = "Sucursal" });
        db.Orders.AddRange(
            DeliveryOrder(10, deliverymanId: 7, routeId: 30),
            DeliveryOrder(11, deliverymanId: 7));
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 30,
            DeliverymanId = 7,
            BranchId = 1,
            Status = DeliveryRouteStatus.InProgress,
            LastAssignmentAtUtc = originalStart.AddMinutes(-3),
            RouteStartedAtUtc = originalStart,
        });
        db.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            Id = 40,
            DeliveryRouteId = 30,
            OrderId = 10,
            StopSequence = 1,
        });
        await db.SaveChangesAsync();

        var service = new DeliveryRouteWorkflowService(
            db,
            Mock.Of<IGoogleRoutesDrivingMetricsService>(),
            Options.Create(new DeliveryRouteOptions()),
            NullLogger<DeliveryRouteWorkflowService>.Instance,
            new FakeClock(now));

        await service.OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 11));

        var routes = await db.DeliveryRoutes.Include(r => r.Stops).ToListAsync();
        var assigned = await db.Orders.SingleAsync(o => o.Id == 11);
        Assert.Single(routes);
        Assert.Equal(30, assigned.DeliveryRouteId);
        Assert.Equal(2, routes[0].Stops.Count);
        Assert.Equal(originalStart, routes[0].RouteStartedAtUtc);
        Assert.Equal(now, routes[0].LastAssignmentAtUtc);
        Assert.Equal(2, routes[0].StopCount);
    }

    [Fact]
    public async Task Assignment_CompletesTerminalInProgressRoute_AndCreatesNewOpenRoute()
    {
        await using var db = CreateDb();
        var originalStart = new DateTime(2026, 7, 21, 15, 0, 0, DateTimeKind.Utc);
        var now = originalStart.AddMinutes(20);

        db.Branches.Add(new Branch { Id = 1, Name = "Centro", Address = "Sucursal" });
        db.Orders.AddRange(
            DeliveryOrder(10, deliverymanId: 7, routeId: 30, status: OrderStatus.Delivered),
            DeliveryOrder(11, deliverymanId: 7));
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 30,
            DeliverymanId = 7,
            BranchId = 1,
            Status = DeliveryRouteStatus.InProgress,
            LastAssignmentAtUtc = originalStart.AddMinutes(-3),
            RouteStartedAtUtc = originalStart,
            MetaDurationSeconds = 900,
        });
        db.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            Id = 40,
            DeliveryRouteId = 30,
            OrderId = 10,
            StopSequence = 1,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, now);

        await service.OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 11));

        var routes = await db.DeliveryRoutes
            .Include(r => r.Stops)
            .OrderBy(r => r.Id)
            .ToListAsync();
        var oldRoute = routes.Single(r => r.Id == 30);
        var newRoute = routes.Single(r => r.Id != 30);
        var newOrder = await db.Orders.SingleAsync(o => o.Id == 11);

        Assert.Equal(DeliveryRouteStatus.Completed, oldRoute.Status);
        Assert.Equal(now, oldRoute.CompletedAtUtc);
        Assert.Equal(1200, oldRoute.ActualDurationSeconds);
        Assert.False(oldRoute.MetSla);
        Assert.Single(oldRoute.Stops);
        Assert.Equal(10, oldRoute.Stops.Single().OrderId);

        Assert.Equal(DeliveryRouteStatus.Open, newRoute.Status);
        Assert.Single(newRoute.Stops);
        Assert.Equal(11, newRoute.Stops.Single().OrderId);
        Assert.Equal(newRoute.Id, newOrder.DeliveryRouteId);
    }

    [Fact]
    public async Task Assignment_AppendsToMixedRoute_WhenAnotherOrderIsOnTheWay()
    {
        await using var db = CreateDb();
        var originalStart = new DateTime(2026, 7, 21, 15, 0, 0, DateTimeKind.Utc);
        var now = originalStart.AddMinutes(20);

        db.Branches.Add(new Branch { Id = 1, Name = "Centro", Address = "Sucursal" });
        db.Orders.AddRange(
            DeliveryOrder(10, deliverymanId: 7, routeId: 30, status: OrderStatus.Delivered),
            DeliveryOrder(11, deliverymanId: 7),
            DeliveryOrder(12, deliverymanId: 7, routeId: 30));
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 30,
            DeliverymanId = 7,
            BranchId = 1,
            Status = DeliveryRouteStatus.InProgress,
            LastAssignmentAtUtc = originalStart.AddMinutes(-3),
            RouteStartedAtUtc = originalStart,
        });
        db.DeliveryRouteStops.AddRange(
            new DeliveryRouteStop
            {
                Id = 40,
                DeliveryRouteId = 30,
                OrderId = 10,
                StopSequence = 1,
            },
            new DeliveryRouteStop
            {
                Id = 41,
                DeliveryRouteId = 30,
                OrderId = 12,
                StopSequence = 2,
            });
        await db.SaveChangesAsync();

        var service = CreateService(db, now);

        await service.OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 11));

        var route = await db.DeliveryRoutes.Include(r => r.Stops).SingleAsync();
        var assigned = await db.Orders.SingleAsync(o => o.Id == 11);
        Assert.Equal(DeliveryRouteStatus.InProgress, route.Status);
        Assert.Equal(30, assigned.DeliveryRouteId);
        Assert.Equal(3, route.Stops.Count);
        Assert.Equal(originalStart, route.RouteStartedAtUtc);
        Assert.Equal(now, route.LastAssignmentAtUtc);
    }

    [Fact]
    public async Task Assignment_LeavesFullyCancelledRouteCancelled_AndCreatesNewRoute()
    {
        await using var db = CreateDb();
        var originalStart = new DateTime(2026, 7, 21, 15, 0, 0, DateTimeKind.Utc);
        var now = originalStart.AddMinutes(20);

        db.Branches.Add(new Branch { Id = 1, Name = "Centro", Address = "Sucursal" });
        db.Orders.AddRange(
            DeliveryOrder(10, deliverymanId: 7, routeId: 30, status: OrderStatus.Cancelled),
            DeliveryOrder(11, deliverymanId: 7));
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 30,
            DeliverymanId = 7,
            BranchId = 1,
            Status = DeliveryRouteStatus.InProgress,
            LastAssignmentAtUtc = originalStart.AddMinutes(-3),
            RouteStartedAtUtc = originalStart,
        });
        db.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            Id = 40,
            DeliveryRouteId = 30,
            OrderId = 10,
            StopSequence = 1,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, now);

        await service.OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 11));

        var routes = await db.DeliveryRoutes.Include(r => r.Stops).ToListAsync();
        var oldRoute = routes.Single(r => r.Id == 30);
        var newRoute = routes.Single(r => r.Id != 30);
        Assert.Equal(DeliveryRouteStatus.Cancelled, oldRoute.Status);
        Assert.Equal(now, oldRoute.CompletedAtUtc);
        Assert.Single(oldRoute.Stops);
        Assert.Equal(DeliveryRouteStatus.Open, newRoute.Status);
        Assert.Equal(11, newRoute.Stops.Single().OrderId);
    }

    [Fact]
    public async Task ConsecutiveAssignments_GroupNewOrdersInSingleNewRoute()
    {
        await using var db = CreateDb();
        var originalStart = new DateTime(2026, 7, 21, 15, 0, 0, DateTimeKind.Utc);
        var now = originalStart.AddMinutes(20);

        db.Branches.Add(new Branch { Id = 1, Name = "Centro", Address = "Sucursal" });
        db.Orders.AddRange(
            DeliveryOrder(10, deliverymanId: 7, routeId: 30, status: OrderStatus.Delivered),
            DeliveryOrder(11, deliverymanId: 7),
            DeliveryOrder(12, deliverymanId: 7));
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 30,
            DeliverymanId = 7,
            BranchId = 1,
            Status = DeliveryRouteStatus.InProgress,
            LastAssignmentAtUtc = originalStart.AddMinutes(-3),
            RouteStartedAtUtc = originalStart,
        });
        db.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            Id = 40,
            DeliveryRouteId = 30,
            OrderId = 10,
            StopSequence = 1,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, now);

        await service.OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 11));
        await service.OnOrderAssignedToDeliverymanAsync(db.Orders.Single(o => o.Id == 12));

        var routes = await db.DeliveryRoutes.Include(r => r.Stops).ToListAsync();
        var oldRoute = routes.Single(r => r.Id == 30);
        var newRoute = routes.Single(r => r.Id != 30);
        Assert.Equal(DeliveryRouteStatus.Completed, oldRoute.Status);
        Assert.Equal(DeliveryRouteStatus.Open, newRoute.Status);
        Assert.Equal(new[] { 11, 12 }, newRoute.Stops.OrderBy(s => s.StopSequence).Select(s => s.OrderId));
    }

    private static DeliveryRouteWorkflowService CreateService(ApplicationDbContext db, DateTime now) =>
        new(
            db,
            Mock.Of<IGoogleRoutesDrivingMetricsService>(),
            Options.Create(new DeliveryRouteOptions()),
            NullLogger<DeliveryRouteWorkflowService>.Instance,
            new FakeClock(now));

    private static Order DeliveryOrder(
        int id,
        int deliverymanId,
        int? routeId = null,
        OrderStatus status = OrderStatus.OnTheWay) => new()
    {
        Id = id,
        BranchId = 1,
        TakenById = 1,
        Type = OrderType.Delivery,
        Status = status,
        DeliveryManId = deliverymanId,
        DeliveryRouteId = routeId,
    };

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}

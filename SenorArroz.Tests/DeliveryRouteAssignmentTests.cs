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

    private static Order DeliveryOrder(int id, int deliverymanId, int? routeId = null) => new()
    {
        Id = id,
        BranchId = 1,
        TakenById = 1,
        Type = OrderType.Delivery,
        Status = OrderStatus.OnTheWay,
        DeliveryManId = deliverymanId,
        DeliveryRouteId = routeId,
    };

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}

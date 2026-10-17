using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Deliverymen.Commands;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class RoutingFleetSessionTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FreeCourierProducesOneIdentifiedSlotNotAnonymousCapacity()
    {
        await using var f = new Fixture();
        f.AddPoint(10, Now, 6m, DeliveryTrackingMode.Light);
        await f.Db.SaveChangesAsync();
        var capacity = await f.Capacity();
        var slot = Assert.Single(capacity.Slots);
        Assert.Equal(7, slot.DeliverymanId);
        Assert.Equal(10, slot.WorkSessionId);
        Assert.Equal(0, slot.AvailableAtSeconds);
        Assert.Equal("available", Assert.Single(capacity.Couriers).State);
    }

    [Fact]
    public async Task OldLightReadingIsProbableRatherThanMissingOrConfirmedFree()
    {
        await using var f = new Fixture();
        f.AddPoint(10, Now.AddMinutes(-4), 6m, DeliveryTrackingMode.Light);
        await f.Db.SaveChangesAsync();
        var capacity = await f.Capacity();
        Assert.Empty(capacity.Slots);
        Assert.Equal("probableAtBranch", Assert.Single(capacity.Couriers).State);
        Assert.Equal("probable", capacity.Couriers[0].Confidence);
    }

    [Fact]
    public async Task BadLatestFixCannotBeReplacedByAnOlderGoodPoint()
    {
        await using var f = new Fixture();
        f.AddPoint(10, Now.AddSeconds(-30), 6m);
        f.AddPoint(10, Now, 6m, accuracy: 100);
        await f.Db.SaveChangesAsync();
        var result = await f.Capacity();
        Assert.Empty(result.Slots);
        Assert.Equal("unknown", result.Couriers.Single().State);
    }

    [Fact]
    public async Task LoadingTripIsNotCountedAsAnAdditionalFreeCourier()
    {
        await using var f = new Fixture();
        f.AddTrip(20, Now.AddMinutes(-5), OrderStatus.OnTheWay);
        f.AddPoint(10, Now, 6m);
        await f.Db.SaveChangesAsync();
        var result = await f.Capacity();
        Assert.Empty(result.Slots);
        Assert.Equal("loading", result.Couriers.Single().State);
        Assert.Equal(20, result.Couriers.Single().CurrentRouteId);
        Assert.Equal(new[] { 30 }, result.Couriers.Single().PendingOrderIds);
    }

    [Fact]
    public async Task ReturnEtaHasUncertaintyAndGoogleFailureKeepsApproximateAvailability()
    {
        await using var f = new Fixture();
        f.AddTrip(20, Now.AddMinutes(-20), OrderStatus.Delivered);
        f.AddPoint(10, Now, 6.002m);
        f.Google.Setup(g => g.ComputeRouteAsync(It.IsAny<IReadOnlyList<(double Latitude, double Longitude)>>(),
            It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("not available"));
        await f.Db.SaveChangesAsync();
        var result = await f.Capacity();
        Assert.Equal("returning", result.Couriers.Single().State);
        Assert.Equal("probable", result.Couriers.Single().Confidence);
        Assert.Equal("approximate", result.Couriers.Single().Source);
        Assert.True(result.Slots.Single().AvailableAtSeconds > 0);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task OldPendingTripBlocksFalseFreeCapacityButCannotBecomeCurrentGpsTrip()
    {
        await using var f = new Fixture();
        f.AddTrip(20, Now.AddDays(-2), OrderStatus.OnTheWay);
        f.AddPoint(10, Now.AddSeconds(-30), 6m);
        await f.Db.SaveChangesAsync();
        Assert.Empty(await DeliveryRouteScope.Current(f.Db, 1, 7, Now).ToListAsync());
        Assert.Equal("inconsistent", (await f.Capacity()).Couriers.Single().State);
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.Id).Returns(7);
        user.SetupGet(u => u.BranchId).Returns(1);
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        var handler = new RecordLocationHandler(f.Db, user.Object, Mock.Of<IOrderNotificationService>(), new FakeClock(Now));
        var response = await handler.Handle(new RecordLocationCommand
        {
            WorkSessionId = 10, DeliveryRouteId = 20, ClientPointId = Guid.NewGuid(),
            Latitude = 6m, Longitude = -75m, AccuracyMeters = 10, GpsEnabled = true, RecordedAt = Now
        }, default);
        Assert.Null(response.DeliveryRouteId);
        Assert.Null(f.Db.DeliverymanLocations.OrderByDescending(p => p.RecordedAt).First().DeliveryRouteId);
        Assert.Equal(Now.AddDays(-2), f.Db.DeliveryRoutes.Single().RouteStartedAtUtc);
        Assert.Equal(OrderStatus.OnTheWay, f.Db.Orders.Single().Status);
    }

    [Fact]
    public async Task LatestWorkSessionWinsAndNeverReusesOtherSessionsGps()
    {
        await using var f = new Fixture();
        f.Db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 11, DeliverymanId = 7, BranchId = 1, StartedAt = Now.AddMinutes(-10),
            AutoCloseAt = Now.AddHours(5), Status = DeliveryWorkSessionStatus.Active,
            DeviceInstallationId = "new-device", DevicePlatform = "android", LastCommunicationAt = Now
        });
        f.AddPoint(10, Now, 6m);
        await f.Db.SaveChangesAsync();
        var result = await f.Capacity();
        Assert.Empty(result.Slots);
        Assert.Equal(11, Assert.Single(result.Couriers).WorkSessionId);
        Assert.Equal("unknown", result.Couriers[0].State);
    }

    [Fact]
    public async Task PhysicalDepartureNeedsInsideAndTwoOutsideFixesAndIsIdempotent()
    {
        await using var f = new Fixture();
        f.AddTrip(20, Now.AddMinutes(-5), OrderStatus.OnTheWay);
        await f.Db.SaveChangesAsync();
        // CreatedAt is database/system time; choose observations after actual creation.
        var at = DateTime.UtcNow.AddMinutes(1);
        var route = f.Db.DeliveryRoutes.Single();
        var session = f.Db.DeliveryWorkSessions.Single();
        session.StartedAt = at.AddHours(-1);
        session.AutoCloseAt = at.AddHours(6);
        route.LastAssignmentAtUtc = at;
        route.RouteStartedAtUtc = at;
        route.WorkSessionId = session.Id;
        f.AddPoint(10, at, 6m, routeId: 20);
        var firstOutside = f.AddPoint(10, at.AddSeconds(30), 6.002m, routeId: 20);
        await f.Db.SaveChangesAsync();
        await DeliveryRouteScope.ObserveDepartureAsync(f.Db, firstOutside, session, firstOutside.RecordedAt, default);
        Assert.Null(route.DepartedAtUtc);
        var secondOutside = f.AddPoint(10, at.AddSeconds(60), 6.003m, routeId: 20);
        await f.Db.SaveChangesAsync();
        await DeliveryRouteScope.ObserveDepartureAsync(f.Db, secondOutside, session, secondOutside.RecordedAt, default);
        await DeliveryRouteScope.ObserveDepartureAsync(f.Db, secondOutside, session, secondOutside.RecordedAt, default);
        Assert.Equal(firstOutside.RecordedAt, route.DepartedAtUtc);
        Assert.Equal("gps", route.DepartureSource);
        Assert.Single(f.Db.EntityAuditLogs.Where(e => e.OperationType == "departure_observed"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly ApplicationDbContext Db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        internal readonly Mock<IGoogleRoutesDrivingMetricsService> Google = new();
        internal Fixture()
        {
            Db.Branches.Add(new Branch { Id = 1, Name = "Branch", Address = "Store", Phone1 = "3000000000",
                Latitude = 6m, Longitude = -75m, DeliveryTrackingAllowedDistanceMeters = 50 });
            Db.Users.Add(new User { Id = 7, BranchId = 1, Role = UserRole.Deliveryman,
                Active = true, Name = "Courier", Email = "courier@test", PasswordHash = "test" });
            Db.DeliveryWorkSessions.Add(new DeliveryWorkSession { Id = 10, BranchId = 1, DeliverymanId = 7,
                StartedAt = Now.AddHours(-1), AutoCloseAt = Now.AddHours(6), Status = DeliveryWorkSessionStatus.Active,
                DeviceInstallationId = "device", DevicePlatform = "android", LastCommunicationAt = Now });
            Google.Setup(g => g.ComputeRouteAsync(It.IsAny<IReadOnlyList<(double Latitude, double Longitude)>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(new DrivingRouteMetrics(200, 60, 0, 0));
        }
        internal DeliverymanLocation AddPoint(int sessionId, DateTime recorded, decimal lat,
            DeliveryTrackingMode mode = DeliveryTrackingMode.ActiveDelivery, double accuracy = 10, int? routeId = null)
        {
            var point = new DeliverymanLocation { DeliverymanId = 7, WorkSessionId = sessionId,
                DeliveryRouteId = routeId, RecordedAt = recorded, Latitude = lat, Longitude = -75m,
                AccuracyMeters = accuracy, GpsEnabled = true, TrackingMode = mode, ClientPointId = Guid.NewGuid() };
            Db.DeliverymanLocations.Add(point);
            return point;
        }
        internal void AddTrip(int id, DateTime assigned, OrderStatus status)
        {
            Db.DeliveryRoutes.Add(new DeliveryRoute { Id = id, BranchId = 1, DeliverymanId = 7,
                Status = DeliveryRouteStatus.InProgress, LastAssignmentAtUtc = assigned, RouteStartedAtUtc = assigned });
            Db.Orders.Add(new Order { Id = 30, BranchId = 1, TakenById = 7, DeliveryManId = 7,
                DeliveryRouteId = id, Type = OrderType.Delivery, Status = status });
            Db.DeliveryRouteStops.Add(new DeliveryRouteStop { Id = 40, DeliveryRouteId = id, OrderId = 30, StopSequence = 1 });
        }
        internal Task<DeliveryRoutingCapacity> Capacity() => new DeliverymanAvailabilityService(Db,
            Google.Object, Options.Create(new DeliveryRoutingOptions())).GetCapacityAsync(1, 6, -75, Now);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}

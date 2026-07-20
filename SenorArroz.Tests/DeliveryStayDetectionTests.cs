using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class DeliveryStayDetectionTests
{
    private static readonly DateTime BaseTime =
        new(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Detect_WithThreeReliablePointsForTenMinutes_CreatesStay()
    {
        var points = new[]
        {
            Point(1, 0, 4.609710m, -74.081750m),
            Point(2, 5, 4.609760m, -74.081730m),
            Point(3, 10, 4.609700m, -74.081780m),
        };

        var stay = Assert.Single(DeliveryStayDetectionService.Detect(points, 10, 50));

        Assert.Equal(1, stay.FirstLocationId);
        Assert.Equal(3, stay.LastLocationId);
        Assert.Equal(600, stay.DurationSeconds);
        Assert.Equal(3, stay.PointCount);
        Assert.InRange(stay.RadiusMeters, 0, 50);
        Assert.Equal(8, stay.AverageAccuracyMeters);
    }

    [Fact]
    public void Detect_WithInaccuratePointBreakingSequence_DoesNotCreateStay()
    {
        var points = new[]
        {
            Point(1, 0, 4.609710m, -74.081750m),
            Point(2, 5, 4.609720m, -74.081750m, accuracy: 51),
            Point(3, 10, 4.609700m, -74.081750m),
            Point(4, 15, 4.609710m, -74.081760m),
        };

        Assert.Empty(DeliveryStayDetectionService.Detect(points, 10, 50));
    }

    [Fact]
    public void Detect_WithMovementOutsideConfiguredRadius_DoesNotCreateStay()
    {
        var points = new[]
        {
            Point(1, 0, 4.609710m, -74.081750m),
            Point(2, 5, 4.609720m, -74.081750m),
            Point(3, 10, 4.611000m, -74.081750m),
        };

        Assert.Empty(DeliveryStayDetectionService.Detect(points, 10, 50));
    }

    [Fact]
    public async Task ProcessPendingSessions_UpdatesOngoingStayWithoutDuplicatingIt()
    {
        await using var db = CreateDb();
        db.Branches.Add(new Branch
        {
            Id = 7,
            Name = "Centro",
            Address = "Calle 1 # 2-3",
            Phone1 = "3001234567",
            Latitude = 4.609710m,
            Longitude = -74.081750m,
            DeliveryTrackingStayThresholdMinutes = 10,
            DeliveryTrackingStayRadiusMeters = 50,
        });
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 10,
            DeliverymanId = 1,
            BranchId = 7,
            DeviceInstallationId = "device-a",
            DevicePlatform = "android",
            StartedAt = BaseTime,
            AutoCloseAt = BaseTime.AddHours(8),
            LastCommunicationAt = BaseTime,
            Status = DeliveryWorkSessionStatus.Active,
        });
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 20,
            DeliverymanId = 1,
            BranchId = 7,
            LastAssignmentAtUtc = BaseTime,
            Status = DeliveryRouteStatus.InProgress,
        });
        db.Addresses.Add(new Address
        {
            Id = 30,
            CustomerId = 2,
            NeighborhoodId = 3,
            AddressText = "Destino",
            Latitude = 4.609800m,
            Longitude = -74.081750m,
        });
        db.Orders.Add(new Order
        {
            Id = 40,
            BranchId = 7,
            TakenById = 2,
            AddressId = 30,
            DeliveryRouteId = 20,
            DeliveryManId = 1,
            Type = OrderType.Delivery,
            Status = OrderStatus.OnTheWay,
        });
        db.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            Id = 50,
            DeliveryRouteId = 20,
            OrderId = 40,
            StopSequence = 1,
        });
        db.DeliverymanLocations.AddRange(
            Location(101, 0, 20),
            Location(102, 5, 20),
            Location(103, 10, 20));
        await db.SaveChangesAsync();
        var clock = new FakeClock(BaseTime.AddMinutes(11));
        var detector = new DeliveryStayDetectionService(db, clock);

        Assert.Equal(1, await detector.ProcessPendingSessionsAsync());

        var firstResult = Assert.Single(db.DeliveryStays);
        Assert.Equal(3, firstResult.PointCount);
        Assert.Equal(600, firstResult.DurationSeconds);
        Assert.InRange(firstResult.DistanceToBranchMeters!.Value, 0, 20);
        Assert.Equal(20, firstResult.DeliveryRouteId);
        Assert.Equal(40, firstResult.NearestOrderId);
        Assert.InRange(firstResult.DistanceToNearestOrderMeters!.Value, 0, 20);
        Assert.Equal(103, db.DeliveryWorkSessions.Single().StayAnalysisLastLocationId);

        db.DeliverymanLocations.Add(Location(104, 15, 20));
        await db.SaveChangesAsync();
        clock.UtcNow = BaseTime.AddMinutes(16);

        Assert.Equal(1, await detector.ProcessPendingSessionsAsync());

        var updated = Assert.Single(db.DeliveryStays);
        Assert.Equal(firstResult.Id, updated.Id);
        Assert.Equal(4, updated.PointCount);
        Assert.Equal(900, updated.DurationSeconds);
        Assert.Equal(104, updated.LastLocationId);
        Assert.Equal(104, db.DeliveryWorkSessions.Single().StayAnalysisLastLocationId);
        Assert.Equal(0, await detector.ProcessPendingSessionsAsync());
    }

    private static DeliveryStayPoint Point(
        long id,
        int minutes,
        decimal latitude,
        decimal longitude,
        double accuracy = 8,
        int? routeId = null) => new(
        id,
        latitude,
        longitude,
        accuracy,
        true,
        routeId,
        BaseTime.AddMinutes(minutes));

    private static DeliverymanLocation Location(long id, int minutes, int? routeId = null) => new()
    {
        Id = id,
        DeliverymanId = 1,
        WorkSessionId = 10,
        DeliveryRouteId = routeId,
        Latitude = 4.609710m + (minutes % 2 == 0 ? 0.000010m : 0),
        Longitude = -74.081750m,
        AccuracyMeters = 8,
        GpsEnabled = true,
        RecordedAt = BaseTime.AddMinutes(minutes),
        SyncedAt = BaseTime.AddMinutes(minutes),
    };

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}

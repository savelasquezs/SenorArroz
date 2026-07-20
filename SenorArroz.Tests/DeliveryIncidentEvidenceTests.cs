using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class DeliveryIncidentEvidenceTests
{
    private static readonly DateTime BaseTime =
        new(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ProcessPendingStays_CopiesStableEvidenceWithConfiguredMargin()
    {
        await using var db = CreateDb();
        db.Branches.Add(new Branch
        {
            Id = 7,
            Name = "Centro",
            Address = "Calle 1 # 2-3",
            Phone1 = "3001234567",
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
        db.Addresses.Add(new Address
        {
            Id = 30,
            CustomerId = 2,
            NeighborhoodId = 3,
            AddressText = "Carrera 10 # 20-30",
            Latitude = 4.610000m,
            Longitude = -74.082000m,
        });
        db.Orders.Add(new Order
        {
            Id = 40,
            BranchId = 7,
            TakenById = 2,
            AddressId = 30,
            DeliveryManId = 1,
            Type = OrderType.Delivery,
            Status = OrderStatus.OnTheWay,
        });
        for (var index = 0; index < 7; index++)
            db.DeliverymanLocations.Add(Location(101 + index, index * 5));
        db.DeliveryDeviceEvents.AddRange(
            DeviceEvent(201, 15, DeliveryDeviceEventType.InternetLost),
            DeviceEvent(202, 35, DeliveryDeviceEventType.InternetRecovered));
        db.DeliveryStays.AddRange(
            new DeliveryStay
            {
                Id = 50,
                DeliverymanId = 1,
                WorkSessionId = 10,
                NearestOrderId = 40,
                FirstLocationId = 103,
                LastLocationId = 105,
                StartedAt = BaseTime.AddMinutes(10),
                EndedAt = BaseTime.AddMinutes(20),
                DurationSeconds = 600,
                CenterLatitude = 4.609710m,
                CenterLongitude = -74.081750m,
                RadiusMeters = 12,
                AverageAccuracyMeters = 8,
                DistanceToBranchMeters = 900,
                DistanceToNearestOrderMeters = 30,
                PointCount = 3,
                Classification = DeliveryStayClassification.PendingReview,
                ClassificationReason = "route_context_requires_review",
                ClassifiedAt = BaseTime.AddMinutes(21),
                UpdatedAt = BaseTime.AddMinutes(20),
            },
            new DeliveryStay
            {
                Id = 51,
                DeliverymanId = 1,
                WorkSessionId = 10,
                FirstLocationId = 101,
                LastLocationId = 103,
                StartedAt = BaseTime,
                EndedAt = BaseTime.AddMinutes(10),
                DurationSeconds = 600,
                CenterLatitude = 4.609710m,
                CenterLongitude = -74.081750m,
                RadiusMeters = 10,
                AverageAccuracyMeters = 8,
                PointCount = 3,
                Classification = DeliveryStayClassification.Branch,
                ClassificationReason = "within_branch_tolerance",
                ClassifiedAt = BaseTime.AddMinutes(11),
                UpdatedAt = BaseTime.AddMinutes(10),
            });
        await db.SaveChangesAsync();
        var service = new DeliveryIncidentEvidenceService(
            db,
            new FakeClock(BaseTime.AddMinutes(31)));

        Assert.Equal(1, await service.ProcessPendingStaysAsync());

        var incident = Assert.Single(db.DeliveryTrackingIncidents);
        Assert.Equal(50, incident.DeliveryStayId);
        Assert.Equal(DeliveryStayClassification.PendingReview, incident.StayClassification);
        Assert.Equal("Carrera 10 # 20-30", incident.OrderAddressSnapshot);
        Assert.Equal(4.610000m, incident.OrderLatitudeSnapshot);
        Assert.Equal(-74.082000m, incident.OrderLongitudeSnapshot);
        Assert.Equal(nameof(OrderStatus.OnTheWay), incident.OrderStatusSnapshot);
        Assert.True(incident.EvidenceComplete);
        var locationEvidence = db.DeliveryIncidentLocationEvidence.OrderBy(x => x.SourceLocationId).ToList();
        Assert.Equal(7, locationEvidence.Count);
        Assert.Equal([101L, 102, 103, 104, 105, 106, 107],
            locationEvidence.Select(x => x.SourceLocationId).ToArray());
        Assert.Equal([103L, 104, 105],
            locationEvidence.Where(x => x.IsCorePoint).Select(x => x.SourceLocationId).ToArray());
        Assert.Equal(201, Assert.Single(db.DeliveryIncidentDeviceEventEvidence).SourceDeviceEventId);

        Assert.Equal(0, await service.ProcessPendingStaysAsync());
        Assert.Single(db.DeliveryTrackingIncidents);
        Assert.Equal(7, db.DeliveryIncidentLocationEvidence.Count());

        db.DeliverymanLocations.RemoveRange(db.DeliverymanLocations);
        db.DeliveryDeviceEvents.RemoveRange(db.DeliveryDeviceEvents);
        await db.SaveChangesAsync();
        Assert.Equal(7, db.DeliveryIncidentLocationEvidence.Count());
        Assert.Equal("Carrera 10 # 20-30", db.DeliveryTrackingIncidents.Single().OrderAddressSnapshot);
    }

    private static DeliverymanLocation Location(long id, int minutes) => new()
    {
        Id = id,
        DeliverymanId = 1,
        WorkSessionId = 10,
        Latitude = 4.609710m,
        Longitude = -74.081750m,
        AccuracyMeters = 8,
        BatteryLevelPercent = 70,
        InternetAvailable = true,
        GpsEnabled = true,
        TrackingMode = DeliveryTrackingMode.ActiveDelivery,
        RecordedAt = BaseTime.AddMinutes(minutes),
        SyncedAt = BaseTime.AddMinutes(minutes).AddSeconds(2),
    };

    private static DeliveryDeviceEvent DeviceEvent(
        long id,
        int minutes,
        DeliveryDeviceEventType eventType) => new()
    {
        Id = id,
        DeliverymanId = 1,
        WorkSessionId = 10,
        EventType = eventType,
        RecordedAt = BaseTime.AddMinutes(minutes),
        SyncedAt = BaseTime.AddMinutes(minutes).AddSeconds(2),
    };

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}

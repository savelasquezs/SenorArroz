using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class DeliveryStayClassificationTests
{
    private static readonly DateTime BaseTime =
        new(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ProcessPendingStays_AppliesConservativeOperationalRules()
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
            DeliveryTrackingAllowedDistanceMeters = 50,
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
        db.DeliveryAuthorizedPlaces.Add(new DeliveryAuthorizedPlace
        {
            Id = 30,
            BranchId = 7,
            Name = "Proveedor autorizado",
            Latitude = 4.630000m,
            Longitude = -74.081750m,
            RadiusMeters = 80,
            Active = true,
        });
        db.DeliveryStays.AddRange(
            Stay(1, 4.609720m),
            Stay(2, 4.620000m, routeId: 20, nearestOrderId: 40, orderDistance: 20),
            Stay(3, 4.630100m),
            Stay(4, 4.640000m, routeId: 20),
            Stay(5, 4.650000m),
            Stay(6, 4.660000m, accuracy: 51));
        await db.SaveChangesAsync();
        var detector = new DeliveryStayClassificationService(
            db,
            new FakeClock(BaseTime.AddHours(1)));

        Assert.Equal(6, await detector.ProcessPendingStaysAsync());

        var stays = db.DeliveryStays.OrderBy(x => x.Id).ToList();
        Assert.Equal(DeliveryStayClassification.Branch, stays[0].Classification);
        Assert.Equal(DeliveryStayClassification.OrderDestination, stays[1].Classification);
        Assert.Equal(DeliveryStayClassification.AuthorizedPlace, stays[2].Classification);
        Assert.Equal(30, stays[2].AuthorizedPlaceId);
        Assert.InRange(stays[2].DistanceToAuthorizedPlaceMeters!.Value, 0, 80);
        Assert.Equal(DeliveryStayClassification.PendingReview, stays[3].Classification);
        Assert.Equal("route_context_requires_review", stays[3].ClassificationReason);
        Assert.Equal(DeliveryStayClassification.UnexpectedPlace, stays[4].Classification);
        Assert.Equal(DeliveryStayClassification.GpsUnreliable, stays[5].Classification);
        Assert.All(stays, stay => Assert.Equal(BaseTime.AddHours(1), stay.ClassifiedAt));
        Assert.Equal(0, await detector.ProcessPendingStaysAsync());
    }

    private static DeliveryStay Stay(
        long id,
        decimal latitude,
        int? routeId = null,
        int? nearestOrderId = null,
        double? orderDistance = null,
        double accuracy = 8) => new()
    {
        Id = id,
        DeliverymanId = 1,
        WorkSessionId = 10,
        DeliveryRouteId = routeId,
        NearestOrderId = nearestOrderId,
        FirstLocationId = id * 10,
        LastLocationId = id * 10 + 2,
        StartedAt = BaseTime.AddMinutes(id),
        EndedAt = BaseTime.AddMinutes(id + 10),
        DurationSeconds = 600,
        CenterLatitude = latitude,
        CenterLongitude = -74.081750m,
        RadiusMeters = 10,
        AverageAccuracyMeters = accuracy,
        DistanceToNearestOrderMeters = orderDistance,
        PointCount = 3,
        UpdatedAt = BaseTime,
    };

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.API.Controllers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public class DeliveryTrackingPlaybackControllerTests
{
    private static readonly DateTime From = new(2026, 7, 26, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Get_ReturnsOrderedPointsAndEventsForAccessibleDeliverymen()
    {
        await using var db = CreateDb();
        Seed(db);
        db.DeliverymanLocations.AddRange(Location(2, 11, 2), Location(1, 11, 1), Location(3, 12, 1));
        db.DeliveryDeviceEvents.Add(new DeliveryDeviceEvent
        {
            Id = 1, DeliverymanId = 11, WorkSessionId = 1, EventType = DeliveryDeviceEventType.GpsEnabled,
            RecordedAt = From.AddMinutes(3), SyncedAt = From.AddMinutes(4),
        });
        await db.SaveChangesAsync();

        var action = await Controller(db, 7).Get([11, 12], From, From.AddHours(1));

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<DeliveryTrackingPlaybackDto>>(ok.Value);
        Assert.Equal(2, response.Data!.Deliverymen.Count);
        Assert.Equal([1L, 2L], response.Data.Deliverymen.Single(x => x.DeliverymanId == 11).Points.Select(x => x.Id));
        Assert.Single(response.Data.Deliverymen.Single(x => x.DeliverymanId == 11).Events);
    }

    [Fact]
    public async Task Get_RejectsDeliverymanFromAnotherBranch()
    {
        await using var db = CreateDb();
        Seed(db);
        var action = await Controller(db, 7).Get([13], From, From.AddHours(1));
        Assert.IsType<ForbidResult>(action.Result);
    }

    [Fact]
    public async Task Get_ValidatesRangeAndRequiredDeliverymen()
    {
        await using var db = CreateDb();
        Seed(db);
        Assert.IsType<BadRequestObjectResult>((await Controller(db, 7).Get([], From, From.AddHours(1))).Result);
        Assert.IsType<BadRequestObjectResult>((await Controller(db, 7).Get([11], From, From)).Result);
        Assert.IsType<BadRequestObjectResult>((await Controller(db, 7).Get([11], From, From.AddHours(25))).Result);
    }

    [Fact]
    public async Task Get_ExcludesPointsOutsideInclusiveRangeAndPreservesRecordedAt()
    {
        await using var db = CreateDb();
        Seed(db);
        db.DeliverymanLocations.AddRange(Location(1, 11, 0), Location(2, 11, 60), Location(3, 11, 61));
        await db.SaveChangesAsync();
        var action = await Controller(db, 7).Get([11], From, From.AddHours(1));
        var response = Assert.IsType<ApiResponse<DeliveryTrackingPlaybackDto>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal([From, From.AddHours(1)], response.Data!.Deliverymen[0].Points.Select(x => x.RecordedAt));
    }

    [Fact]
    public async Task Get_ReturnsActivePersistedStayWithChronologicalOrderContext()
    {
        await using var db = CreateDb();
        Seed(db);
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 20,
            DeliverymanId = 11,
            BranchId = 7,
            DeviceInstallationId = "device-11",
            DevicePlatform = "android",
            StartedAt = From,
            AutoCloseAt = From.AddHours(8),
            LastCommunicationAt = From.AddMinutes(25),
            Status = DeliveryWorkSessionStatus.Active,
        });
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 30,
            DeliverymanId = 11,
            BranchId = 7,
            LastAssignmentAtUtc = From,
            Status = DeliveryRouteStatus.InProgress,
        });
        db.Addresses.AddRange(
            new Address { Id = 101, AddressText = "Anterior", Latitude = 6.24m, Longitude = -75.57m },
            new Address { Id = 102, AddressText = "Relacionado", Latitude = 6.25m, Longitude = -75.58m });
        var previous = DeliveredOrder(201, 101, 10);
        var relatedAndNext = DeliveredOrder(202, 102, 40);
        db.Orders.AddRange(previous, relatedAndNext);
        db.DeliveryRouteStops.AddRange(
            new DeliveryRouteStop { Id = 301, DeliveryRouteId = 30, OrderId = 201, StopSequence = 1 },
            new DeliveryRouteStop { Id = 302, DeliveryRouteId = 30, OrderId = 202, StopSequence = 2 });
        db.DeliverymanLocations.AddRange(Location(500, 11, 20), Location(501, 11, 22), Location(502, 11, 25));
        db.DeliverymanLocations.Local.ToList().ForEach(x =>
        {
            x.WorkSessionId = 20;
            x.DeliveryRouteId = 30;
        });
        db.DeliveryStays.Add(new DeliveryStay
        {
            Id = 401,
            DeliverymanId = 11,
            WorkSessionId = 20,
            DeliveryRouteId = 30,
            NearestOrderId = 202,
            FirstLocationId = 500,
            LastLocationId = 502,
            StartedAt = From.AddMinutes(20),
            EndedAt = From.AddMinutes(25),
            DurationSeconds = 300,
            CenterLatitude = 6.25m,
            CenterLongitude = -75.58m,
            RadiusMeters = 18,
            AverageAccuracyMeters = 6,
            PointCount = 3,
            Classification = DeliveryStayClassification.PendingReview,
        });
        await db.SaveChangesAsync();

        var action = await Controller(db, 7).Get([11], From, From.AddHours(1));
        var response = Assert.IsType<ApiResponse<DeliveryTrackingPlaybackDto>>(
            Assert.IsType<OkObjectResult>(action.Result).Value);
        var stay = Assert.Single(response.Data!.Deliverymen[0].Stays);

        Assert.True(stay.IsActive);
        Assert.Null(stay.EndedAt);
        Assert.Equal(2400, stay.DurationSeconds);
        Assert.Equal(2, stay.Orders.Count);
        Assert.Contains(stay.Orders, x => x.OrderId == 201 && x.Roles.SequenceEqual(["previous"]));
        Assert.Contains(stay.Orders, x => x.OrderId == 202 && x.Roles.SequenceEqual(["related", "next"]));

        var laterAction = await Controller(db, 7).Get([11], From.AddMinutes(26), From.AddHours(1));
        var laterResponse = Assert.IsType<ApiResponse<DeliveryTrackingPlaybackDto>>(
            Assert.IsType<OkObjectResult>(laterAction.Result).Value);
        Assert.Single(laterResponse.Data!.Deliverymen[0].Stays);
    }

    private static DeliveryTrackingPlaybackController Controller(ApplicationDbContext db, int branchId) =>
        new(db, new TestBranchContext(branchId), new FakeClock(From.AddHours(1)));

    private static DeliverymanLocation Location(long id, int deliverymanId, int minute) => new()
    {
        Id = id, DeliverymanId = deliverymanId, Latitude = 6.25m, Longitude = -75.58m,
        RecordedAt = From.AddMinutes(minute), SyncedAt = From.AddMinutes(minute + 5),
        InternetAvailable = minute != 0,
    };

    private static Order DeliveredOrder(int id, int addressId, int minute)
    {
        var order = new Order
        {
            Id = id,
            BranchId = 7,
            TakenById = 11,
            AddressId = addressId,
            DeliveryRouteId = 30,
            DeliveryManId = 11,
            Type = OrderType.Delivery,
            Status = OrderStatus.Delivered,
        };
        order.AddStatusTime(OrderStatus.Delivered, From.AddMinutes(minute));
        return order;
    }

    private static void Seed(ApplicationDbContext db)
    {
        db.Branches.AddRange(
            new Branch { Id = 7, Name = "Centro", Address = "A", Phone1 = "1" },
            new Branch { Id = 8, Name = "Norte", Address = "B", Phone1 = "2" });
        db.Users.AddRange(
            new User { Id = 11, BranchId = 7, Name = "Ana", Email = "a@a.co", Phone = "1", PasswordHash = "x", Role = UserRole.Deliveryman },
            new User { Id = 12, BranchId = 7, Name = "Beto", Email = "b@a.co", Phone = "2", PasswordHash = "x", Role = UserRole.Deliveryman },
            new User { Id = 13, BranchId = 8, Name = "Caro", Email = "c@a.co", Phone = "3", PasswordHash = "x", Role = UserRole.Deliveryman });
        db.SaveChanges();
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}

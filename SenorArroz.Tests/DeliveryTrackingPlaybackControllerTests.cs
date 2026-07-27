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

    private static DeliveryTrackingPlaybackController Controller(ApplicationDbContext db, int branchId) =>
        new(db, new TestBranchContext(branchId));

    private static DeliverymanLocation Location(long id, int deliverymanId, int minute) => new()
    {
        Id = id, DeliverymanId = deliverymanId, Latitude = 6.25m, Longitude = -75.58m,
        RecordedAt = From.AddMinutes(minute), SyncedAt = From.AddMinutes(minute + 5),
        InternetAvailable = minute != 0,
    };

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

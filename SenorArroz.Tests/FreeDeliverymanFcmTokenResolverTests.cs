using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class FreeDeliverymanFcmTokenResolverTests
{
    private static readonly DateTime NowUtc =
        new(2026, 7, 21, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ResolveAsync_RequiresFreeAndAtBranchAtTheSameTime()
    {
        await using var db = CreateDb();
        db.Branches.Add(Branch());
        db.Users.AddRange(
            Deliveryman(1),
            Deliveryman(2),
            Deliveryman(3),
            Deliveryman(4));
        db.UserDeviceTokens.AddRange(
            Token(1),
            Token(2),
            Token(3),
            Token(4));
        db.DeliveryWorkSessions.AddRange(
            Session(11, 1),
            Session(12, 2),
            Session(13, 3),
            Session(14, 4));
        db.DeliverymanLocations.AddRange(
            Location(101, 1, 11, 4.609710m, NowUtc.AddMinutes(-1)),
            Location(102, 2, 12, 4.609710m, NowUtc.AddMinutes(-1)),
            Location(103, 3, 13, 4.620000m, NowUtc.AddMinutes(-1)),
            Location(104, 4, 14, 4.609710m, NowUtc.AddMinutes(-7)));
        db.Orders.Add(new Order
        {
            Id = 50,
            BranchId = 7,
            TakenById = 1,
            DeliveryManId = 2,
            Type = OrderType.Delivery,
            Status = OrderStatus.OnTheWay,
        });
        await db.SaveChangesAsync();

        var resolver = new FreeDeliverymanFcmTokenResolver(
            db,
            new FakeClock(NowUtc));

        var result = await resolver.ResolveAsync(7);

        Assert.Equal(["token-1"], result.Tokens);
        Assert.Equal(1, result.BusyDeliverymanCount);
        Assert.Equal(1, result.AtBranchDeliverymanCount);
    }

    [Fact]
    public async Task ResolveAsync_ExcludesEveryDeliverymanWhenBranchHasNoCoordinates()
    {
        await using var db = CreateDb();
        var branch = Branch();
        branch.Latitude = null;
        branch.Longitude = null;
        db.Branches.Add(branch);
        db.Users.Add(Deliveryman(1));
        db.UserDeviceTokens.Add(Token(1));
        await db.SaveChangesAsync();

        var resolver = new FreeDeliverymanFcmTokenResolver(
            db,
            new FakeClock(NowUtc));

        var result = await resolver.ResolveAsync(7);

        Assert.Empty(result.Tokens);
        Assert.Equal(0, result.AtBranchDeliverymanCount);
    }

    private static Branch Branch() => new()
    {
        Id = 7,
        Name = "Centro",
        Address = "Calle 1 # 2-3",
        Phone1 = "3001234567",
        Latitude = 4.609710m,
        Longitude = -74.081750m,
        DeliveryTrackingAllowedDistanceMeters = 50,
        DeliveryTrackingLightIntervalSeconds = 300,
    };

    private static User Deliveryman(int id) => new()
    {
        Id = id,
        BranchId = 7,
        Role = UserRole.Deliveryman,
        Name = $"Domiciliario {id}",
        Email = $"domiciliario{id}@example.com",
        Phone = $"300000000{id}",
        PasswordHash = "hash",
        Active = true,
    };

    private static UserDeviceToken Token(int userId) => new()
    {
        Id = userId,
        UserId = userId,
        Token = $"token-{userId}",
        Platform = "android",
        LastSeenAt = NowUtc,
    };

    private static DeliveryWorkSession Session(int id, int deliverymanId) => new()
    {
        Id = id,
        DeliverymanId = deliverymanId,
        BranchId = 7,
        DeviceInstallationId = $"device-{deliverymanId}",
        DevicePlatform = "android",
        StartedAt = NowUtc.AddHours(-1),
        AutoCloseAt = NowUtc.AddHours(5),
        LastCommunicationAt = NowUtc.AddMinutes(-1),
        Status = DeliveryWorkSessionStatus.Active,
    };

    private static DeliverymanLocation Location(
        long id,
        int deliverymanId,
        int sessionId,
        decimal latitude,
        DateTime recordedAt) => new()
    {
        Id = id,
        DeliverymanId = deliverymanId,
        WorkSessionId = sessionId,
        ClientPointId = Guid.NewGuid(),
        Latitude = latitude,
        Longitude = -74.081750m,
        AccuracyMeters = 8,
        GpsEnabled = true,
        TrackingMode = DeliveryTrackingMode.Light,
        RecordedAt = recordedAt,
        SyncedAt = recordedAt,
    };

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}

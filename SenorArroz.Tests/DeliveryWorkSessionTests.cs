using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Deliverymen.Commands;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class DeliveryWorkSessionTests
{
    [Fact]
    public async Task Start_CreatesOneSessionWithColombiaCutoff()
    {
        await using var db = CreateDb();
        db.Branches.Add(CreateBranch());
        await db.SaveChangesAsync();
        var handler = CreateStartHandler(db, new DateTime(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc));

        var result = await handler.Handle(StartCommand("device-a"), default);

        Assert.Equal(new DateTime(2026, 7, 21, 2, 0, 0, DateTimeKind.Utc), result.AutoCloseAt);
        Assert.Equal(DeliveryWorkSessionStatus.Active, result.Status);
        Assert.Equal(300, result.Tracking.LightIntervalSeconds);
        Assert.Single(db.DeliveryWorkSessions);
    }

    [Fact]
    public async Task Start_AfterConfiguredCutoff_IsRejected()
    {
        await using var db = CreateDb();
        db.Branches.Add(CreateBranch());
        await db.SaveChangesAsync();
        var handler = CreateStartHandler(db, new DateTime(2026, 7, 21, 3, 0, 0, DateTimeKind.Utc));

        var error = await Assert.ThrowsAsync<BusinessException>(
            () => handler.Handle(StartCommand("device-a"), default));

        Assert.Contains("21:00", error.Message);
        Assert.Empty(db.DeliveryWorkSessions);
    }

    [Fact]
    public async Task Start_AfterFullSettlementBlock_IsRejected()
    {
        await using var db = CreateDb();
        db.Branches.Add(CreateBranch());
        db.DeliverymanDayStates.Add(new DeliverymanDayState
        {
            BranchId = 7,
            DeliverymanId = 1,
            Date = new DateOnly(2026, 7, 20),
            LiquidationMode = DeliverymanDayLiquidationMode.FullLiquidation,
            Blocked = true,
        });
        await db.SaveChangesAsync();
        var handler = CreateStartHandler(db, new DateTime(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc));

        var error = await Assert.ThrowsAsync<BusinessException>(
            () => handler.Handle(StartCommand("device-a"), default));

        Assert.Contains("liquidación total", error.Message);
        Assert.Empty(db.DeliveryWorkSessions);
    }

    [Fact]
    public async Task Start_OnAnotherDevice_ClosesPreviousSession()
    {
        await using var db = CreateDb();
        db.Branches.Add(CreateBranch());
        await db.SaveChangesAsync();
        var clock = new FakeClock(new DateTime(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc));
        var handler = CreateStartHandler(db, clock);
        var first = await handler.Handle(StartCommand("device-a"), default);

        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        var second = await handler.Handle(StartCommand("device-b"), default);

        Assert.NotEqual(first.Id, second.Id);
        var sessions = await db.DeliveryWorkSessions.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.Equal(DeliveryWorkSessionStatus.Closed, sessions[0].Status);
        Assert.Equal(DeliveryWorkSessionEndReason.UserChange, sessions[0].EndReason);
        Assert.Equal(DeliveryWorkSessionStatus.Active, sessions[1].Status);
    }

    [Fact]
    public async Task RecordLocation_WithoutActiveRoute_IsSavedAsLightTracking()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc);
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 10,
            DeliverymanId = 1,
            BranchId = 7,
            DeviceInstallationId = "device-a",
            DevicePlatform = "android",
            StartedAt = now,
            AutoCloseAt = now.AddHours(8),
            LastCommunicationAt = now,
            Status = DeliveryWorkSessionStatus.Active,
        });
        await db.SaveChangesAsync();
        var notifications = new Mock<IOrderNotificationService>();
        var handler = new RecordLocationHandler(
            db,
            CurrentUser().Object,
            notifications.Object,
            new FakeClock(now));

        await handler.Handle(new RecordLocationCommand
        {
            WorkSessionId = 10,
            Latitude = 4.60971m,
            Longitude = -74.08175m,
            RecordedAt = now,
        }, default);

        var point = Assert.Single(db.DeliverymanLocations);
        Assert.Equal(10, point.WorkSessionId);
        Assert.Null(point.DeliveryRouteId);
        notifications.Verify(x => x.NotifyDeliverymanLocation(
            7, 1, null, 4.60971, -74.08175, now), Times.Once);
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Branch CreateBranch() => new()
    {
        Id = 7,
        Name = "Centro",
        Address = "Calle 1 # 2-3",
        Phone1 = "3001234567",
        DeliveryTrackingAutoCloseTime = new TimeOnly(21, 0),
    };

    private static StartDeliveryWorkSessionCommand StartCommand(string deviceId) => new()
    {
        DeviceInstallationId = deviceId,
        DevicePlatform = "android",
        DeviceDescription = "Android test",
        AppVersion = "1.2.1+5",
    };

    private static StartDeliveryWorkSessionHandler CreateStartHandler(
        ApplicationDbContext db,
        DateTime now) => CreateStartHandler(db, new FakeClock(now));

    private static StartDeliveryWorkSessionHandler CreateStartHandler(
        ApplicationDbContext db,
        FakeClock clock) => new(db, CurrentUser().Object, clock);

    private static Mock<ICurrentUser> CurrentUser()
    {
        var current = new Mock<ICurrentUser>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.Id).Returns(1);
        current.SetupGet(x => x.BranchId).Returns(7);
        current.SetupGet(x => x.Role).Returns("deliveryman");
        return current;
    }
}

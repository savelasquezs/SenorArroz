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
    public async Task RecordLocation_CompletesTerminalRouteOnlyAfterReturnToBranch()
    {
        await using var db = CreateDb();
        var startedAt = new DateTime(2026, 7, 21, 16, 0, 0, DateTimeKind.Utc);
        var now = startedAt.AddMinutes(30);
        db.Branches.Add(new Branch
        {
            Id = 7,
            Name = "Centro",
            Address = "Sucursal",
            Latitude = 4.60971m,
            Longitude = -74.08175m,
            DeliveryTrackingAllowedDistanceMeters = 50,
        });
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 10,
            DeliverymanId = 1,
            BranchId = 7,
            DeviceInstallationId = "device-a",
            DevicePlatform = "android",
            StartedAt = startedAt,
            AutoCloseAt = now.AddHours(5),
            LastCommunicationAt = startedAt,
            Status = DeliveryWorkSessionStatus.Active,
        });
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 20,
            DeliverymanId = 1,
            BranchId = 7,
            Status = DeliveryRouteStatus.InProgress,
            LastAssignmentAtUtc = startedAt,
            RouteStartedAtUtc = startedAt,
            MetaDurationSeconds = 2400,
        });
        db.Orders.Add(new Order
        {
            Id = 30,
            BranchId = 7,
            TakenById = 2,
            DeliveryManId = 1,
            DeliveryRouteId = 20,
            Type = OrderType.Delivery,
            Status = OrderStatus.Delivered,
        });
        db.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            Id = 40,
            DeliveryRouteId = 20,
            OrderId = 30,
            StopSequence = 1,
        });
        await db.SaveChangesAsync();
        var handler = new RecordLocationHandler(
            db,
            CurrentUser().Object,
            Mock.Of<IOrderNotificationService>(),
            new FakeClock(now));

        var outside = await handler.Handle(new RecordLocationCommand
        {
            WorkSessionId = 10,
            ClientPointId = Guid.NewGuid(),
            Latitude = 4.61971m,
            Longitude = -74.08175m,
            RecordedAt = now,
        }, default);

        Assert.True(outside.ContinueActiveTracking);
        Assert.Equal(DeliveryRouteStatus.InProgress, db.DeliveryRoutes.Single().Status);

        var arrivedAt = now.AddMinutes(7);
        var arrived = await handler.Handle(new RecordLocationCommand
        {
            WorkSessionId = 10,
            ClientPointId = Guid.NewGuid(),
            DeliveryRouteId = 20,
            Latitude = 4.60971m,
            Longitude = -74.08175m,
            RecordedAt = arrivedAt,
        }, default);

        var route = db.DeliveryRoutes.Single();
        Assert.False(arrived.ContinueActiveTracking);
        Assert.Equal(DeliveryRouteStatus.Completed, route.Status);
        Assert.Equal(arrivedAt, route.CompletedAtUtc);
        Assert.Equal(2220, route.ActualDurationSeconds);
        Assert.True(route.MetSla);
    }

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
        Assert.NotNull(point.ClientPointId);
        Assert.Equal(DeliveryTrackingMode.Light, point.TrackingMode);
        Assert.True(point.InternetAvailable);
        Assert.True(point.GpsEnabled);
        Assert.Equal(now, point.SyncedAt);
        notifications.Verify(x => x.NotifyDeliverymanLocation(
            7, 1, null, 4.60971, -74.08175, now), Times.Once);
    }

    [Fact]
    public async Task RecordLocation_WithOrderOnTheWay_UsesActiveDeliveryTracking()
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
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 20,
            DeliverymanId = 1,
            BranchId = 7,
            LastAssignmentAtUtc = now,
            Status = DeliveryRouteStatus.InProgress,
        });
        db.Orders.Add(new Order
        {
            Id = 30,
            BranchId = 7,
            TakenById = 2,
            DeliveryManId = 1,
            DeliveryRouteId = 20,
            Type = OrderType.Delivery,
            Status = OrderStatus.OnTheWay,
        });
        await db.SaveChangesAsync();
        var handler = new RecordLocationHandler(
            db,
            CurrentUser().Object,
            Mock.Of<IOrderNotificationService>(),
            new FakeClock(now));

        await handler.Handle(new RecordLocationCommand
        {
            WorkSessionId = 10,
            Latitude = 4.60971m,
            Longitude = -74.08175m,
            RecordedAt = now,
        }, default);

        var point = Assert.Single(db.DeliverymanLocations);
        Assert.Equal(20, point.DeliveryRouteId);
        Assert.Equal(DeliveryTrackingMode.ActiveDelivery, point.TrackingMode);
    }

    [Fact]
    public async Task RecordOfflinePointCapturedBeforeCutoff_ClosesExpiredSessionAndKeepsPoint()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 21, 2, 5, 0, DateTimeKind.Utc);
        var cutoff = now.AddMinutes(-5);
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 10,
            DeliverymanId = 1,
            BranchId = 7,
            DeviceInstallationId = "device-a",
            DevicePlatform = "android",
            StartedAt = now.AddHours(-8),
            AutoCloseAt = cutoff,
            LastCommunicationAt = now.AddMinutes(-10),
            Status = DeliveryWorkSessionStatus.Active,
        });
        await db.SaveChangesAsync();
        var handler = new RecordLocationHandler(
            db,
            CurrentUser().Object,
            Mock.Of<IOrderNotificationService>(),
            new FakeClock(now));

        await handler.Handle(new RecordLocationCommand
        {
            WorkSessionId = 10,
            ClientPointId = Guid.NewGuid(),
            Latitude = 4.60971m,
            Longitude = -74.08175m,
            InternetAvailable = false,
            TrackingMode = DeliveryTrackingMode.Offline,
            RecordedAt = cutoff.AddSeconds(-30),
        }, default);

        var session = db.DeliveryWorkSessions.Single();
        Assert.Equal(DeliveryWorkSessionStatus.Closed, session.Status);
        Assert.Equal(DeliveryWorkSessionEndReason.AutomaticClosure, session.EndReason);
        Assert.Equal(DeliveryTrackingMode.Offline, Assert.Single(db.DeliverymanLocations).TrackingMode);
        Assert.Equal(DeliveryDeviceEventType.AutomaticClosure, Assert.Single(db.DeliveryDeviceEvents).EventType);
    }

    [Fact]
    public async Task RecordOfflineLocation_IsIdempotentAndKeepsCapturedRouteAfterSessionClosed()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 20, 23, 0, 0, DateTimeKind.Utc);
        var capturedAt = now.AddMinutes(-10);
        var pointId = Guid.NewGuid();
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 10,
            DeliverymanId = 1,
            BranchId = 7,
            DeviceInstallationId = "device-a",
            DevicePlatform = "android",
            StartedAt = now.AddHours(-4),
            AutoCloseAt = now.AddHours(3),
            EndedAt = now.AddMinutes(-5),
            EndReason = DeliveryWorkSessionEndReason.TotalSettlement,
            LastCommunicationAt = now.AddMinutes(-5),
            Status = DeliveryWorkSessionStatus.Closed,
        });
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 20,
            DeliverymanId = 1,
            BranchId = 7,
            Status = DeliveryRouteStatus.Completed,
            LastAssignmentAtUtc = now.AddHours(-2),
            CompletedAtUtc = now.AddMinutes(-6),
        });
        await db.SaveChangesAsync();
        var notifications = new Mock<IOrderNotificationService>();
        var autoCompletion = new Mock<IDeliveryAutoCompletionService>();
        var handler = new RecordLocationHandler(
            db,
            CurrentUser().Object,
            notifications.Object,
            new FakeClock(now),
            autoCompletion.Object);
        var command = new RecordLocationCommand
        {
            WorkSessionId = 10,
            ClientPointId = pointId,
            DeliveryRouteId = 20,
            Latitude = 4.60971m,
            Longitude = -74.08175m,
            AccuracyMeters = 8.5,
            HeadingDegrees = 125,
            BatteryLevelPercent = 42,
            InternetAvailable = false,
            GpsEnabled = true,
            TrackingMode = DeliveryTrackingMode.Offline,
            RecordedAt = capturedAt,
        };

        await handler.Handle(command, default);
        await handler.Handle(command, default);

        var point = Assert.Single(db.DeliverymanLocations);
        Assert.Equal(pointId, point.ClientPointId);
        Assert.Equal(20, point.DeliveryRouteId);
        Assert.Equal(8.5, point.AccuracyMeters);
        Assert.Equal(125, point.HeadingDegrees);
        Assert.Equal(42, point.BatteryLevelPercent);
        Assert.False(point.InternetAvailable);
        Assert.True(point.GpsEnabled);
        Assert.Equal(DeliveryTrackingMode.Offline, point.TrackingMode);
        Assert.Equal(capturedAt, point.RecordedAt);
        Assert.Equal(now, point.SyncedAt);
        notifications.Verify(x => x.NotifyDeliverymanLocation(
            7, 1, 20, 4.60971, -74.08175, capturedAt), Times.Once);
        autoCompletion.Verify(x => x.EvaluateLocationAsync(
            It.Is<DeliverymanLocation>(location => location.ClientPointId == pointId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordLocation_CapturedAfterSessionEnd_IsRejected()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 20, 23, 0, 0, DateTimeKind.Utc);
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 10,
            DeliverymanId = 1,
            BranchId = 7,
            DeviceInstallationId = "device-a",
            DevicePlatform = "android",
            StartedAt = now.AddHours(-4),
            AutoCloseAt = now.AddHours(3),
            EndedAt = now.AddMinutes(-5),
            EndReason = DeliveryWorkSessionEndReason.TotalSettlement,
            LastCommunicationAt = now.AddMinutes(-5),
            Status = DeliveryWorkSessionStatus.Closed,
        });
        await db.SaveChangesAsync();
        var handler = new RecordLocationHandler(
            db,
            CurrentUser().Object,
            Mock.Of<IOrderNotificationService>(),
            new FakeClock(now));

        var error = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new RecordLocationCommand
            {
                WorkSessionId = 10,
                ClientPointId = Guid.NewGuid(),
                Latitude = 4.60971m,
                Longitude = -74.08175m,
                RecordedAt = now.AddMinutes(-4),
            },
            default));

        Assert.Contains("fuera de la jornada", error.Message);
        Assert.Empty(db.DeliverymanLocations);
    }

    [Fact]
    public async Task RecordDeviceEvent_IsIdempotentAndKeepsCaptureTimeForClosedSession()
    {
        await using var db = CreateDb();
        var syncedAt = new DateTime(2026, 7, 20, 23, 0, 0, DateTimeKind.Utc);
        var recordedAt = syncedAt.AddMinutes(-8);
        var clientEventId = Guid.NewGuid();
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 10,
            DeliverymanId = 1,
            BranchId = 7,
            DeviceInstallationId = "device-a",
            DevicePlatform = "android",
            StartedAt = syncedAt.AddHours(-4),
            AutoCloseAt = syncedAt.AddHours(3),
            EndedAt = syncedAt.AddMinutes(-5),
            EndReason = DeliveryWorkSessionEndReason.TotalSettlement,
            LastCommunicationAt = syncedAt.AddMinutes(-5),
            Status = DeliveryWorkSessionStatus.Closed,
        });
        await db.SaveChangesAsync();
        var handler = new RecordDeliveryDeviceEventHandler(
            db,
            CurrentUser().Object,
            new FakeClock(syncedAt));
        var command = new RecordDeliveryDeviceEventCommand
        {
            WorkSessionId = 10,
            ClientEventId = clientEventId,
            EventType = DeliveryDeviceEventType.InternetLost,
            InternetAvailable = false,
            BatteryLevelPercent = 42,
            RecordedAt = recordedAt,
            Details = "  detected_while_offline  ",
        };

        await handler.Handle(command, default);
        await handler.Handle(command, default);

        var deviceEvent = Assert.Single(db.DeliveryDeviceEvents);
        Assert.Equal(clientEventId, deviceEvent.ClientEventId);
        Assert.Equal(DeliveryDeviceEventType.InternetLost, deviceEvent.EventType);
        Assert.False(deviceEvent.InternetAvailable);
        Assert.Equal(42, deviceEvent.BatteryLevelPercent);
        Assert.Equal(recordedAt, deviceEvent.RecordedAt);
        Assert.Equal(syncedAt, deviceEvent.SyncedAt);
        Assert.Equal("detected_while_offline", deviceEvent.Details);
        Assert.Equal(syncedAt, db.DeliveryWorkSessions.Single().LastCommunicationAt);
    }

    [Fact]
    public async Task RecordDeviceEvent_WithInvalidBatteryLevel_IsRejected()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 20, 23, 0, 0, DateTimeKind.Utc);
        var handler = new RecordDeliveryDeviceEventHandler(
            db,
            CurrentUser().Object,
            new FakeClock(now));

        await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new RecordDeliveryDeviceEventCommand
            {
                WorkSessionId = 10,
                EventType = DeliveryDeviceEventType.BatteryLow,
                BatteryLevelPercent = 101,
                RecordedAt = now,
            },
            default));

        Assert.Empty(db.DeliveryDeviceEvents);
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

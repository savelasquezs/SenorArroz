using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Deliverymen.Commands;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class DeliveryTrackingHistoryTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task OlderPoint_IsStoredButCannotCompleteRouteOrRunGeofences()
    {
        await using var db = await CreateDb();
        var notifications = new Mock<IOrderNotificationService>();
        var autoCompletion = new Mock<IDeliveryAutoCompletionService>();
        var handler = Handler(db, notifications.Object, autoCompletion.Object);
        var current = Point(Now, latitude: 4.61971m);
        await handler.Handle(current, default);
        var old = Point(Now.AddSeconds(-30));
        await handler.Handle(old, default);
        await handler.Handle(old, default);

        Assert.Equal(2, await db.DeliverymanLocations.CountAsync());
        Assert.Equal(DeliveryRouteStatus.InProgress, db.DeliveryRoutes.Single().Status);
        Assert.Null(db.DeliveryRoutes.Single().CompletedAtUtc);
        Assert.Equal(old.RecordedAt, db.DeliverymanLocations.Single(x => x.ClientPointId == old.ClientPointId).RecordedAt);
        autoCompletion.Verify(x => x.EvaluateLocationAsync(
            It.Is<DeliverymanLocation>(p => p.ClientPointId == current.ClientPointId),
            It.IsAny<CancellationToken>()), Times.Once);
        autoCompletion.Verify(x => x.EvaluateLocationAsync(
            It.Is<DeliverymanLocation>(p => p.ClientPointId == old.ClientPointId),
            It.IsAny<CancellationToken>()), Times.Never);
        notifications.Verify(x => x.NotifyDeliverymanLocation(
            7, 1, 20, 4.60971, -74.08175, old.RecordedAt), Times.Once);
    }

    [Fact]
    public async Task StalePoint_WithoutAnyNewerPosition_CannotCompleteCurrentRoute()
    {
        await using var db = await CreateDb();
        var command = Point(Now.AddMinutes(-10));
        var result = await Handler(db).Handle(command, default);

        Assert.Single(db.DeliverymanLocations);
        Assert.True(result.ContinueActiveTracking);
        Assert.Equal(DeliveryRouteStatus.InProgress, db.DeliveryRoutes.Single().Status);
        Assert.Null(db.DeliveryRoutes.Single().CompletedAtUtc);
    }

    [Fact]
    public async Task OldLightPoint_IsNotAssignedToCurrentRoute()
    {
        await using var db = await CreateDb();
        var command = Point(Now.AddMinutes(-10));
        command.DeliveryRouteId = null;
        await Handler(db).Handle(command, default);

        Assert.Null(db.DeliverymanLocations.Single().DeliveryRouteId);
        Assert.Equal(DeliveryTrackingMode.Light, db.DeliverymanLocations.Single().TrackingMode);
        Assert.Equal(DeliveryRouteStatus.InProgress, db.DeliveryRoutes.Single().Status);
    }

    [Fact]
    public async Task FreshReturn_StillCompletesRoute()
    {
        await using var db = await CreateDb();
        var result = await Handler(db).Handle(Point(Now), default);

        Assert.False(result.ContinueActiveTracking);
        Assert.Equal(DeliveryRouteStatus.Completed, db.DeliveryRoutes.Single().Status);
        Assert.Equal(Now, db.DeliveryRoutes.Single().CompletedAtUtc);
    }

    private static RecordLocationCommand Point(DateTime at, decimal latitude = 4.60971m) => new()
    {
        WorkSessionId = 10,
        ClientPointId = Guid.NewGuid(),
        DeliveryRouteId = 20,
        Latitude = latitude,
        Longitude = -74.08175m,
        AccuracyMeters = 5,
        GpsEnabled = true,
        RecordedAt = at,
    };

    private static RecordLocationHandler Handler(ApplicationDbContext db,
        IOrderNotificationService? notifications = null,
        IDeliveryAutoCompletionService? autoCompletion = null)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.Id).Returns(1);
        user.SetupGet(x => x.BranchId).Returns(7);
        user.SetupGet(x => x.Role).Returns("deliveryman");
        return new RecordLocationHandler(db, user.Object,
            notifications ?? Mock.Of<IOrderNotificationService>(), new FakeClock(Now), autoCompletion);
    }

    private static async Task<ApplicationDbContext> CreateDb()
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
        db.Branches.Add(new Branch
        {
            Id = 7, Name = "Centro", Address = "Sucursal",
            Latitude = 4.60971m, Longitude = -74.08175m,
            DeliveryTrackingAllowedDistanceMeters = 50,
        });
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 10, DeliverymanId = 1, BranchId = 7,
            DeviceInstallationId = "device-a", DevicePlatform = "android",
            StartedAt = Now.AddHours(-2), AutoCloseAt = Now.AddHours(5),
            LastCommunicationAt = Now.AddHours(-2), Status = DeliveryWorkSessionStatus.Active,
        });
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 20, DeliverymanId = 1, BranchId = 7, Status = DeliveryRouteStatus.InProgress,
            LastAssignmentAtUtc = Now.AddHours(-1), RouteStartedAtUtc = Now.AddHours(-1),
            MetaDurationSeconds = 7200,
        });
        db.Orders.Add(new Order
        {
            Id = 30, BranchId = 7, TakenById = 2, DeliveryManId = 1,
            DeliveryRouteId = 20, Type = OrderType.Delivery, Status = OrderStatus.Delivered,
        });
        db.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            Id = 40, DeliveryRouteId = 20, OrderId = 30, StopSequence = 1,
        });
        await db.SaveChangesAsync();
        return db;
    }
}

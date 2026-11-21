using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;
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

    [Theory]
    [InlineData(20, true)]
    [InlineData(450, true)]
    [InlineData(451, false)]
    [InlineData(2100, false)]
    public async Task Get_PreservesSupportedStayDurationAndOrdersWhenEvidenceAges(int secondsSinceLastPoint, bool expectedActive)
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
        var firstRouteOrder = RouteOrder(201, 101, 30, OrderStatus.OnTheWay);
        var relatedRouteOrder = RouteOrder(202, 102, 30, OrderStatus.Ready);
        db.Orders.AddRange(firstRouteOrder, relatedRouteOrder);
        db.DeliveryRouteStops.AddRange(
            new DeliveryRouteStop { Id = 301, DeliveryRouteId = 30, OrderId = 201, StopSequence = 1 },
            new DeliveryRouteStop { Id = 302, DeliveryRouteId = 30, OrderId = 202, StopSequence = 2 });
        db.DeliverymanLocations.AddRange(Location(500, 11, 20), Location(501, 11, 22), Location(502, 11, 25));
        db.DeliverymanLocations.Local.ToList().ForEach(x =>
        {
            x.WorkSessionId = 20;
            x.DeliveryRouteId = 30;
            x.TrackingMode = DeliveryTrackingMode.Light;
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

        var now = From.AddMinutes(25).AddSeconds(secondsSinceLastPoint);
        var action = await Controller(db, 7, now).Get([11], From, From.AddHours(1));
        var response = Assert.IsType<ApiResponse<DeliveryTrackingPlaybackDto>>(
            Assert.IsType<OkObjectResult>(action.Result).Value);
        var stay = Assert.Single(response.Data!.Deliverymen[0].Stays);

        Assert.Equal(expectedActive, stay.IsActive);
        Assert.Equal(From.AddMinutes(25), stay.EndedAt);
        Assert.Equal(300, stay.DurationSeconds);
        Assert.Equal(2, stay.Orders.Count);
        Assert.Contains(stay.Orders, x => x.OrderId == 201 && x.Roles.SequenceEqual(["current_route"]));
        Assert.Contains(stay.Orders, x => x.OrderId == 202 && x.Roles.SequenceEqual(["current_route", "related"]));

        var laterAction = await Controller(db, 7).Get([11], From.AddMinutes(26), From.AddHours(1));
        var laterResponse = Assert.IsType<ApiResponse<DeliveryTrackingPlaybackDto>>(
            Assert.IsType<OkObjectResult>(laterAction.Result).Value);
        Assert.Single(laterResponse.Data!.Deliverymen[0].Stays);
    }

    [Fact]
    public async Task Get_UsesPreviousRouteOrdersWhenStayRouteHasNoStops()
    {
        await using var db = CreateDb();
        Seed(db);
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 21,
            DeliverymanId = 11,
            BranchId = 7,
            DeviceInstallationId = "device-11",
            DevicePlatform = "android",
            StartedAt = From,
            AutoCloseAt = From.AddHours(8),
            LastCommunicationAt = From.AddMinutes(35),
            Status = DeliveryWorkSessionStatus.Closed,
        });
        db.DeliveryRoutes.AddRange(
            new DeliveryRoute
            {
                Id = 31,
                DeliverymanId = 11,
                BranchId = 7,
                LastAssignmentAtUtc = From.AddMinutes(5),
                CompletedAtUtc = From.AddMinutes(15),
                Status = DeliveryRouteStatus.Completed,
            },
            new DeliveryRoute
            {
                Id = 32,
                DeliverymanId = 11,
                BranchId = 7,
                LastAssignmentAtUtc = From.AddMinutes(20),
                Status = DeliveryRouteStatus.InProgress,
            });
        db.Addresses.Add(new Address
        {
            Id = 103,
            AddressText = "Ruta anterior",
            Latitude = 6.26m,
            Longitude = -75.59m,
        });
        db.Orders.Add(RouteOrder(203, 103, 31, OrderStatus.Delivered, 12));
        db.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            Id = 303,
            DeliveryRouteId = 31,
            OrderId = 203,
            StopSequence = 1,
        });
        db.DeliveryStays.Add(new DeliveryStay
        {
            Id = 402,
            DeliverymanId = 11,
            WorkSessionId = 21,
            DeliveryRouteId = 32,
            FirstLocationId = 600,
            LastLocationId = 602,
            StartedAt = From.AddMinutes(25),
            EndedAt = From.AddMinutes(35),
            DurationSeconds = 600,
            CenterLatitude = 6.25m,
            CenterLongitude = -75.58m,
            RadiusMeters = 20,
            AverageAccuracyMeters = 7,
            PointCount = 3,
            Classification = DeliveryStayClassification.PendingReview,
        });
        await db.SaveChangesAsync();

        var action = await Controller(db, 7).Get([11], From, From.AddHours(1));
        var response = Assert.IsType<ApiResponse<DeliveryTrackingPlaybackDto>>(
            Assert.IsType<OkObjectResult>(action.Result).Value);
        var order = Assert.Single(Assert.Single(response.Data!.Deliverymen[0].Stays).Orders);

        Assert.Equal(203, order.OrderId);
        Assert.Equal(["previous_route"], order.Roles);
    }

    [Fact]
    public async Task Get_UsesLatestRecordedPointAndIdToDetermineActiveStay()
    {
        await using var db = CreateDb();
        Seed(db);
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 20, DeliverymanId = 11, BranchId = 7,
            DeviceInstallationId = "unit-device", DevicePlatform = "android",
            StartedAt = From, AutoCloseAt = From.AddHours(8),
            LastCommunicationAt = From.AddMinutes(25),
            Status = DeliveryWorkSessionStatus.Active,
        });
        db.DeliverymanLocations.AddRange(
            Location(501, 11, 25), Location(502, 11, 25), Location(503, 11, 24));
        foreach (var location in db.DeliverymanLocations.Local)
        {
            location.WorkSessionId = 20;
            location.TrackingMode = location.Id == 502 ? DeliveryTrackingMode.Light : DeliveryTrackingMode.ActiveDelivery;
        }
        var stay = new DeliveryStay
        {
            Id = 601, DeliverymanId = 11, WorkSessionId = 20,
            FirstLocationId = 503, LastLocationId = 502,
            StartedAt = From.AddMinutes(24), EndedAt = From.AddMinutes(25),
            DurationSeconds = 60, CenterLatitude = 6.25m, CenterLongitude = -75.58m,
            RadiusMeters = 10, AverageAccuracyMeters = 5, PointCount = 3,
            Classification = DeliveryStayClassification.PendingReview,
        };
        db.DeliveryStays.Add(stay);
        await db.SaveChangesAsync();

        var now = From.AddMinutes(25).AddSeconds(100);
        var result = await Controller(db, 7, now).Get([11], From, From.AddMinutes(24));
        var data = Assert.IsType<ApiResponse<DeliveryTrackingPlaybackDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).Data!;
        Assert.True(Assert.Single(Assert.Single(data.Deliverymen).Stays).IsActive);

        // Identical timestamps use the greater ID; an older stay must be inactive.
        stay.LastLocationId = 501;
        await db.SaveChangesAsync();
        result = await Controller(db, 7, now).Get([11], From, From.AddMinutes(24));
        data = Assert.IsType<ApiResponse<DeliveryTrackingPlaybackDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).Data!;
        Assert.False(Assert.Single(Assert.Single(data.Deliverymen).Stays).IsActive);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Get_LatestLocationLookupIsGroupedBySessionInPostgreSql()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var commands = new PlaybackSqlCommands();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).AddInterceptors(commands).Options;
        await using var db = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new Tenant { Id = 1, Name = "Playback perf", Slug = "playback-perf" });
        await db.SaveChangesAsync();
        Seed(db);

        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 20, DeliverymanId = 11, BranchId = 7,
            DeviceInstallationId = "postgres-device", DevicePlatform = "android",
            StartedAt = From.AddHours(-3), AutoCloseAt = From.AddHours(8),
            LastCommunicationAt = From.AddMinutes(25),
            Status = DeliveryWorkSessionStatus.Active,
        });
        for (var i = 0; i < 120; i++)
        {
            var location = Location(1000 + i, 11, -120 + i);
            location.WorkSessionId = 20;
            location.TrackingMode = DeliveryTrackingMode.ActiveDelivery;
            db.DeliverymanLocations.Add(location);
        }
        var latest = Location(2001, 11, 25);
        latest.WorkSessionId = 20;
        latest.TrackingMode = DeliveryTrackingMode.Light;
        db.DeliverymanLocations.Add(latest);
        db.DeliveryStays.Add(new DeliveryStay
        {
            Id = 601, DeliverymanId = 11, WorkSessionId = 20,
            FirstLocationId = 1000, LastLocationId = 2001,
            StartedAt = From.AddMinutes(20), EndedAt = From.AddMinutes(25),
            DurationSeconds = 300, CenterLatitude = 6.25m, CenterLongitude = -75.58m,
            RadiusMeters = 10, AverageAccuracyMeters = 5, PointCount = 121,
            Classification = DeliveryStayClassification.PendingReview,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        commands.Sql.Clear();

        var action = await Controller(db, 7, From.AddMinutes(25).AddSeconds(30))
            .Get([11], From.AddMinutes(20), From.AddMinutes(24));
        var data = Assert.IsType<ApiResponse<DeliveryTrackingPlaybackDto>>(
            Assert.IsType<OkObjectResult>(action.Result).Value).Data!;
        Assert.Empty(Assert.Single(data.Deliverymen).Points); // The latest point is outside the requested interval.
        Assert.True(Assert.Single(Assert.Single(data.Deliverymen).Stays).IsActive);
        // The old query selected every historical location. The new relational SQL
        // must perform latest-per-session selection in PostgreSQL before materializing.
        Assert.Contains(commands.Sql, sql =>
            sql.Contains("deliveryman_location", StringComparison.OrdinalIgnoreCase)
            && (sql.Contains("ROW_NUMBER", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("LATERAL", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("DISTINCT ON", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class PlaybackSqlCommands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private static DeliveryTrackingPlaybackController Controller(ApplicationDbContext db, int branchId, DateTime? now = null) =>
        new(db, new TestBranchContext(branchId), new FakeClock(now ?? From.AddHours(1)));

    private static DeliverymanLocation Location(long id, int deliverymanId, int minute) => new()
    {
        Id = id, DeliverymanId = deliverymanId, Latitude = 6.25m, Longitude = -75.58m,
        RecordedAt = From.AddMinutes(minute), SyncedAt = From.AddMinutes(minute + 5),
        InternetAvailable = minute != 0,
    };

    private static Order RouteOrder(
        int id,
        int addressId,
        int routeId,
        OrderStatus status,
        int? deliveredMinute = null)
    {
        var order = new Order
        {
            Id = id,
            BranchId = 7,
            TakenById = 11,
            AddressId = addressId,
            DeliveryRouteId = routeId,
            DeliveryManId = 11,
            Type = OrderType.Delivery,
            Status = status,
        };
        if (deliveredMinute.HasValue)
            order.AddStatusTime(OrderStatus.Delivered, From.AddMinutes(deliveredMinute.Value));
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
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        currentTenant: TestTenantContext.Default,
        tenantExecutionContext: TestTenantContext.Default);
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Deliverymen.Commands;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class DeliveryWriteReliabilityTests
{
    [Theory]
    [InlineData("40P01", true)]
    [InlineData("40001", true)]
    [InlineData("23505", false)]
    [InlineData("57014", false)]
    public void RetryClassification_UnwrapsProviderSqlState(string sqlState, bool expected)
    {
        var exception = new InvalidOperationException("EF transient failure",
            new DbUpdateException("Write failed", new FakePostgresException(sqlState)));
        Assert.Equal(expected, DeliveryWriteReliability.IsPostgresConcurrencyFailure(exception));
    }

    [Fact]
    public async Task SaveWithTransientRetry_ReplaysOnlyAbortedWrites_AtMostThreeTimes()
    {
        var db = new Mock<IApplicationDbContext>();
        var attempts = 0;
        db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            attempts++;
            return attempts < 3
                ? Task.FromException<int>(Failure("40P01"))
                : Task.FromResult(1);
        });

        await DeliveryWriteReliability.SaveWithTransientRetryAsync(
            db.Object, null, 2, 7, "location", CancellationToken.None);

        Assert.Equal(3, attempts);
        db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SaveWithTransientRetry_PropagatesNonRetryableErrors()
    {
        var db = new Mock<IApplicationDbContext>();
        db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(Failure("23505"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DeliveryWriteReliability.SaveWithTransientRetryAsync(
                db.Object, null, 2, 7, "location", CancellationToken.None));
        db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveWithTransientRetry_StopsAfterThreeConflicts()
    {
        var db = new Mock<IApplicationDbContext>();
        db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(Failure("40001"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DeliveryWriteReliability.SaveWithTransientRetryAsync(
                db.Object, null, 2, 7, "device_event", CancellationToken.None));
        db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task LocationWrite_CommitsPointBeforeUpdatingSessionHeartbeat()
    {
        var monitor = new WriteOrderInterceptor();
        await using var db = CreateDb(monitor);
        var now = new DateTime(2026, 11, 2, 18, 30, 0, DateTimeKind.Utc);
        db.DeliveryWorkSessions.Add(Session(now));
        await db.SaveChangesAsync();
        monitor.Writes.Clear();
        var clientPointId = Guid.NewGuid();

        var handler = new RecordLocationHandler(
            db, CurrentUser(), Mock.Of<IOrderNotificationService>(), new FakeClock(now));

        await handler.Handle(new RecordLocationCommand
        {
            WorkSessionId = 7, ClientPointId = clientPointId,
            Latitude = 6.25m, Longitude = -75.57m, RecordedAt = now
        }, default);

        Assert.Equal(new[] { "point", "heartbeat" }, monitor.Writes);
        Assert.Equal(now, db.DeliveryWorkSessions.Single().LastCommunicationAt);
        Assert.Equal(clientPointId, Assert.Single(db.DeliverymanLocations).ClientPointId);

        await handler.Handle(new RecordLocationCommand
        {
            WorkSessionId = 7, ClientPointId = clientPointId,
            Latitude = 6.25m, Longitude = -75.57m, RecordedAt = now
        }, default);
        Assert.Single(db.DeliverymanLocations);
        Assert.Equal(2, monitor.Writes.Count);
    }

    [Fact]
    public async Task DeviceEventWrite_CommitsEventBeforeUpdatingSessionHeartbeat()
    {
        var monitor = new WriteOrderInterceptor();
        await using var db = CreateDb(monitor);
        var now = new DateTime(2026, 11, 2, 18, 30, 0, DateTimeKind.Utc);
        db.DeliveryWorkSessions.Add(Session(now));
        await db.SaveChangesAsync();
        monitor.Writes.Clear();

        var handler = new RecordDeliveryDeviceEventHandler(db, CurrentUser(), new FakeClock(now));
        await handler.Handle(new RecordDeliveryDeviceEventCommand
        {
            WorkSessionId = 7,
            ClientEventId = Guid.NewGuid(),
            EventType = DeliveryDeviceEventType.InternetRecovered,
            RecordedAt = now
        }, default);

        Assert.Equal(new[] { "event", "heartbeat" }, monitor.Writes);
        Assert.Equal(now, db.DeliveryWorkSessions.Single().LastCommunicationAt);
        Assert.Single(db.DeliveryDeviceEvents);
    }

    private static Exception Failure(string sqlState) => new InvalidOperationException(
        "EF transient failure", new DbUpdateException("Database write", new FakePostgresException(sqlState)));

    private sealed class FakePostgresException(string sqlState) : Exception("PostgreSQL conflict")
    {
        public string SqlState { get; } = sqlState;
    }

    private static DeliveryWorkSession Session(DateTime now) => new()
    {
        Id = 7, DeliverymanId = 2, BranchId = 1,
        DeviceInstallationId = "test-device", DevicePlatform = "android",
        StartedAt = now.AddHours(-1), AutoCloseAt = now.AddHours(6),
        LastCommunicationAt = now.AddMinutes(-5),
        Status = DeliveryWorkSessionStatus.Active
    };

    private static ApplicationDbContext CreateDb(WriteOrderInterceptor interceptor) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options,
        currentTenant: TestTenantContext.Default,
        tenantExecutionContext: TestTenantContext.Default);

    private static ICurrentUser CurrentUser()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.Id).Returns(2);
        user.SetupGet(x => x.BranchId).Returns(1);
        user.SetupGet(x => x.Role).Returns("deliveryman");
        user.SetupGet(x => x.DeviceInstallationId).Returns("test-device");
        return user.Object;
    }

    private sealed class WriteOrderInterceptor : SaveChangesInterceptor
    {
        public List<string> Writes { get; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } db)
            {
                var point = db.ChangeTracker.Entries<DeliverymanLocation>()
                    .Any(x => x.State == EntityState.Added);
                var deviceEvent = db.ChangeTracker.Entries<DeliveryDeviceEvent>()
                    .Any(x => x.State == EntityState.Added);
                var sessionUpdated = db.ChangeTracker.Entries<DeliveryWorkSession>()
                    .Any(x => x.State == EntityState.Modified);
                if (point || deviceEvent)
                {
                    Assert.False(sessionUpdated,
                        "A point/event INSERT must not update the work-session heartbeat in the same transaction.");
                    Writes.Add(point ? "point" : "event");
                }
                else if (sessionUpdated)
                {
                    Writes.Add("heartbeat");
                }
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

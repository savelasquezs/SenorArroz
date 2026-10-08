using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class DeliveryAlertEvidenceRegressionTests
{
    private static readonly DateTime Start = new(2026, 10, 8, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LegacyFalseGpsAndPermissionReportsDoNotCreateAdministrativeAccusations()
    {
        await using var db = await Db();
        db.DeliveryDeviceEvents.AddRange(
            Event(1, DeliveryDeviceEventType.GpsDisabled, "readFailed=true"),
            Event(2, DeliveryDeviceEventType.LocationPermissionRevoked, "serviceEnabled=false;permission=always"));
        await db.SaveChangesAsync();
        await Service(db, Start.AddMinutes(2)).ProcessAsync();
        Assert.All(db.DeliveryTrackingAlerts, a =>
        {
            Assert.Equal(DeliveryTrackingAlertSeverity.Informational, a.Severity);
            Assert.Equal(DeliveryTrackingAlertStatus.Resolved, a.Status);
        });
        Assert.Empty(db.DeliveryTrackingIncidents);
    }

    [Fact]
    public async Task ConfirmedUserStopStaysPendingEvenWhenDeviceCommunicatesAgain()
    {
        await using var db = await Db();
        var e = Event(1, DeliveryDeviceEventType.AppStopped,
            "evidence_version=2;source=android_exit_info;sdk=34;exit_reason=10;cause=user_requested");
        db.DeliveryDeviceEvents.Add(e);
        db.DeliveryWorkSessions.Single().LastCommunicationAt = Start.AddMinutes(2);
        await db.SaveChangesAsync();
        var notifications = new Mock<IOrderNotificationService>();
        await Service(db, Start.AddMinutes(2), notifications.Object).ProcessAsync();
        var alert = Assert.Single(db.DeliveryTrackingAlerts);
        Assert.Equal(DeliveryTrackingAlertStatus.Active, alert.Status);
        Assert.NotNull(alert.RecoveredAt);
        Assert.Equal(DeliveryIncidentReviewStatus.Pending, db.DeliveryTrackingIncidents.Single().ReviewStatus);
        Assert.Equal(DeliveryInterruptionCertainty.ConfirmedByDevice, db.DeliveryTrackingIncidents.Single().InterruptionCertainty);
        notifications.Verify(x => x.NotifyDeliveryTrackingAlert(7, alert.Id, 1,
            It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ProlongedGenericSilenceClosesOnRecoveryButKeepsHistoryAndLastLocation()
    {
        await using var db = await Db();
        db.DeliverymanLocations.Add(new DeliverymanLocation { Id = 100, DeliverymanId = 1,
            WorkSessionId = 10, Latitude = 6.25m, Longitude = -75.57m,
            TrackingMode = DeliveryTrackingMode.ActiveDelivery, RecordedAt = Start, SyncedAt = Start });
        await db.SaveChangesAsync();
        await Service(db, Start.AddMinutes(10)).ProcessAsync();
        var alert = Assert.Single(db.DeliveryTrackingAlerts);
        Assert.Equal(6.25m, alert.StartLatitude);
        Assert.Equal(Start, alert.StartLocationRecordedAt);
        db.DeliveryWorkSessions.Single().LastCommunicationAt = Start.AddMinutes(15);
        await db.SaveChangesAsync();
        await Service(db, Start.AddMinutes(15)).ProcessAsync();
        Assert.Equal(DeliveryTrackingAlertStatus.Resolved, alert.Status);
        Assert.Equal(900, alert.DurationSeconds);
        Assert.Single(db.DeliveryTrackingIncidents);
    }

    [Fact]
    public async Task LightCadenceDoesNotProduceFalseThreeMinuteSilence()
    {
        await using var db = await Db();
        await Service(db, Start.AddMinutes(3)).ProcessAsync();
        Assert.Empty(db.DeliveryTrackingAlerts);
        await Service(db, Start.AddMinutes(6)).ProcessAsync();
        Assert.Equal(DeliveryTrackingAlertSeverity.Warning, db.DeliveryTrackingAlerts.Single().Severity);
    }

    [Fact]
    public async Task RebootIsInformationalAndNotAConfirmedManualStop()
    {
        await using var db = await Db();
        db.DeliveryDeviceEvents.Add(Event(1, DeliveryDeviceEventType.DeviceRestarted, "confirmed_by_android"));
        await db.SaveChangesAsync();
        await Service(db, Start.AddMinutes(2)).ProcessAsync();
        var alert = Assert.Single(db.DeliveryTrackingAlerts);
        Assert.Equal(DeliveryTrackingAlertSeverity.Informational, alert.Severity);
        Assert.Equal(DeliveryTrackingAlertStatus.Resolved, alert.Status);
        Assert.Empty(db.DeliveryTrackingIncidents);
    }

    [Fact]
    public async Task LateRecoveredPointsRetractSameStayWithoutDeletingReview()
    {
        await using var db = await Db();
        db.DeliverymanLocations.AddRange(Enumerable.Range(0, 21).Select(i => new DeliverymanLocation {
            Id = i + 1, ClientPointId = Guid.NewGuid(), DeliverymanId = 1, WorkSessionId = 10,
            Latitude = 6.25m, Longitude = -75.57m, AccuracyMeters = 8, GpsEnabled = true,
            TrackingMode = DeliveryTrackingMode.ActiveDelivery, RecordedAt = Start.AddSeconds(i * 30), SyncedAt = Start.AddMinutes(10) }));
        await db.SaveChangesAsync();
        var clock = new FakeClock(Start.AddMinutes(10));
        var detection = new DeliveryStayDetectionService(db, clock);
        await detection.ProcessSessionAsync(10);
        var stay = Assert.Single(db.DeliveryStays);
        var incident = new DeliveryTrackingIncident { Id = 500, BranchId = 7, DeliverymanId = 1, WorkSessionId = 10,
            IncidentType = DeliveryTrackingIncidentType.Stay, DeliveryStayId = stay.Id, StartedAt = stay.StartedAt,
            EndedAt = stay.EndedAt, SourceUpdatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow,
            ReviewStatus = DeliveryIncidentReviewStatus.Justified, AdminNotes = "Permiso comprobado", EvidenceComplete = true };
        db.DeliveryTrackingIncidents.Add(incident);
        // These are extra real captures, not fabricated replacement coordinates.
        db.DeliverymanLocations.AddRange(new[] { 130, 250, 370, 490 }.Select((seconds, i) => new DeliverymanLocation {
            Id = 100 + i, DeliverymanId = 1, WorkSessionId = 10, Latitude = 6.26m, Longitude = -75.57m,
            AccuracyMeters = 8, GpsEnabled = true, TrackingMode = DeliveryTrackingMode.ActiveDelivery,
            RecordedAt = Start.AddSeconds(seconds), SyncedAt = Start.AddMinutes(12) }));
        await db.SaveChangesAsync();
        clock.UtcNow = Start.AddMinutes(12);
        await detection.ProcessSessionAsync(10);
        await new DeliveryIncidentEvidenceService(db, clock).ProcessPendingStaysAsync();
        Assert.Single(db.DeliveryStays);
        Assert.Equal(DeliveryTrackingEvidencePolicy.RetractedStay, stay.ClassificationReason);
        Assert.Equal(DeliveryTrackingEvidencePolicy.RetractedStay, incident.ClassificationReason);
        Assert.Equal(DeliveryIncidentReviewStatus.Justified, incident.ReviewStatus);
        Assert.Equal("Permiso comprobado", incident.AdminNotes);
    }

    private static DeliveryDeviceEvent Event(long id, DeliveryDeviceEventType type, string details) => new()
    { Id = id, ClientEventId = Guid.NewGuid(), DeliverymanId = 1, WorkSessionId = 10,
      EventType = type, GpsEnabled = type == DeliveryDeviceEventType.GpsDisabled ? false : null,
      LocationPermissionGranted = type == DeliveryDeviceEventType.LocationPermissionRevoked ? false : null,
      Details = details, RecordedAt = Start.AddMinutes(1), SyncedAt = Start.AddMinutes(2) };

    private static DeliveryTrackingAlertService Service(ApplicationDbContext db, DateTime now, IOrderNotificationService? notifications = null) =>
        new(db, new FakeClock(now), Mock.Of<IFcmPushService>(), NullLogger<DeliveryTrackingAlertService>.Instance, notifications);

    private static async Task<ApplicationDbContext> Db()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        db.Branches.Add(new Branch { Id = 7, Name = "Centro", Address = "A",
            DeliveryTrackingActiveIntervalSeconds = 30, DeliveryTrackingLightIntervalSeconds = 300 });
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession { Id = 10, BranchId = 7, DeliverymanId = 1,
            DeviceInstallationId = "device", DevicePlatform = "android", StartedAt = Start,
            AutoCloseAt = Start.AddHours(8), LastCommunicationAt = Start, Status = DeliveryWorkSessionStatus.Active });
        await db.SaveChangesAsync();
        return db;
    }
}

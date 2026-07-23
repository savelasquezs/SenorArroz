using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class DeliveryTrackingAlertTests
{
    private static readonly DateTime BaseTime =
        new(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Process_PreservesGpsAlertAndAddsRecoveryDurationAndLocations()
    {
        await using var db = CreateDb();
        db.Branches.Add(new Branch
        {
            Id = 7,
            Name = "Centro",
            Address = "A",
            Phone1 = "1",
            DeliveryTrackingLightIntervalSeconds = 300,
            DeliveryTrackingActiveIntervalSeconds = 30,
        });
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 10,
            DeliverymanId = 1,
            BranchId = 7,
            DeviceInstallationId = "device",
            DevicePlatform = "android",
            StartedAt = BaseTime,
            AutoCloseAt = BaseTime.AddHours(8),
            LastCommunicationAt = BaseTime,
            Status = DeliveryWorkSessionStatus.Active,
        });
        db.DeliverymanLocations.Add(new DeliverymanLocation
        {
            Id = 100,
            DeliverymanId = 1,
            WorkSessionId = 10,
            Latitude = 4.6m,
            Longitude = -74.08m,
            TrackingMode = DeliveryTrackingMode.Light,
            RecordedAt = BaseTime,
        });
        db.DeliverymanLocations.Add(new DeliverymanLocation
        {
            Id = 101,
            DeliverymanId = 1,
            WorkSessionId = 10,
            Latitude = 4.61m,
            Longitude = -74.09m,
            TrackingMode = DeliveryTrackingMode.Light,
            RecordedAt = BaseTime.AddMinutes(2).AddSeconds(5),
        });
        db.DeliveryDeviceEvents.AddRange(
            DeviceEvent(201, DeliveryDeviceEventType.GpsDisabled, 1),
            DeviceEvent(202, DeliveryDeviceEventType.GpsEnabled, 2),
            DeviceEvent(203, DeliveryDeviceEventType.LocationPermissionRevoked, 3),
            DeviceEvent(204, DeliveryDeviceEventType.InternetRecovered, 4, "queued_location_count=4"));
        db.DeliveryTrackingIncidents.Add(new DeliveryTrackingIncident
        {
            Id = 300,
            BranchId = 7,
            DeliverymanId = 1,
            WorkSessionId = 10,
            IncidentType = DeliveryTrackingIncidentType.Stay,
            StayClassification = DeliveryStayClassification.UnexpectedPlace,
            StartedAt = BaseTime.AddMinutes(4),
            EndedAt = BaseTime.AddMinutes(15),
            DurationSeconds = 660,
            CenterLatitude = 4.6m,
            CenterLongitude = -74.08m,
            RadiusMeters = 10,
            AverageAccuracyMeters = 8,
            SourceUpdatedAt = BaseTime.AddMinutes(15),
            EvidenceCapturedAt = BaseTime.AddMinutes(16),
            EvidenceComplete = true,
            UpdatedAt = BaseTime.AddMinutes(16),
        });
        await db.SaveChangesAsync();
        var clock = new FakeClock(BaseTime.AddMinutes(10));
        var service = new DeliveryTrackingAlertService(db, clock);

        await service.ProcessAsync();

        Assert.Equal(5, db.DeliveryTrackingAlerts.Count());
        var gpsAlert = db.DeliveryTrackingAlerts.Single(x => x.AlertType == DeliveryTrackingAlertType.GpsDisabled);
        Assert.Equal(DeliveryTrackingAlertStatus.Active, gpsAlert.Status);
        Assert.Equal(BaseTime.AddMinutes(2), gpsAlert.RecoveredAt);
        Assert.Equal(60, gpsAlert.DurationSeconds);
        Assert.Equal(4.6m, gpsAlert.StartLatitude);
        Assert.Equal(-74.08m, gpsAlert.StartLongitude);
        Assert.Equal(4.61m, gpsAlert.EndLatitude);
        Assert.Equal(-74.09m, gpsAlert.EndLongitude);
        Assert.Contains("1 min 0 s", gpsAlert.Message);
        var gpsReview = db.DeliveryTrackingIncidents.Single(
            x => x.IncidentType == DeliveryTrackingIncidentType.LocationDisabled);
        Assert.Equal(DeliveryIncidentReviewStatus.Pending, gpsReview.ReviewStatus);
        Assert.Equal(201, gpsReview.SourceDeviceEventId);
        Assert.Equal(BaseTime.AddMinutes(1), gpsReview.StartedAt);
        Assert.Equal(BaseTime.AddMinutes(2), gpsReview.EndedAt);
        Assert.Equal(60, gpsReview.DurationSeconds);
        Assert.True(gpsReview.EvidenceComplete);
        Assert.Equal(2, db.DeliveryIncidentLocationEvidence.Count(x => x.IncidentId == gpsReview.Id));
        Assert.Equal(2, db.DeliveryIncidentDeviceEventEvidence.Count(x => x.IncidentId == gpsReview.Id));
        Assert.Equal(DeliveryTrackingAlertStatus.Active,
            db.DeliveryTrackingAlerts.Single(x => x.AlertType == DeliveryTrackingAlertType.LocationPermissionRevoked).Status);
        var queued = db.DeliveryTrackingAlerts.Single(x => x.AlertType == DeliveryTrackingAlertType.OfflineLocationsQueued);
        Assert.Equal(DeliveryTrackingAlertSeverity.Informational, queued.Severity);
        Assert.Contains("4 ubicaciones", queued.Message);
        Assert.Equal(DeliveryTrackingAlertSeverity.RequiresReview,
            db.DeliveryTrackingAlerts.Single(x => x.AlertType == DeliveryTrackingAlertType.UnexpectedStay).Severity);
        var stay = db.DeliveryTrackingAlerts.Single(x => x.AlertType == DeliveryTrackingAlertType.UnexpectedStay);
        Assert.Equal(660, stay.DurationSeconds);
        Assert.Equal(4.6m, stay.StartLatitude);
        Assert.Equal(-74.08m, stay.StartLongitude);
        Assert.Contains("300 segundos", db.DeliveryTrackingAlerts
            .Single(x => x.AlertType == DeliveryTrackingAlertType.NoCommunication).Message);
        Assert.Equal(0, await service.ProcessAsync());

        var incident = db.DeliveryTrackingIncidents.Single(
            x => x.IncidentType == DeliveryTrackingIncidentType.Stay);
        incident.ReviewStatus = DeliveryIncidentReviewStatus.Justified;
        db.DeliveryWorkSessions.Single().LastCommunicationAt = BaseTime.AddMinutes(11);
        await db.SaveChangesAsync();
        clock.UtcNow = BaseTime.AddMinutes(11);

        Assert.Equal(2, await service.ProcessAsync());
        Assert.Equal(DeliveryTrackingAlertStatus.Resolved,
            db.DeliveryTrackingAlerts.Single(x => x.AlertType == DeliveryTrackingAlertType.UnexpectedStay).Status);
        Assert.Equal(DeliveryTrackingAlertStatus.Resolved,
            db.DeliveryTrackingAlerts.Single(x => x.AlertType == DeliveryTrackingAlertType.NoCommunication).Status);
    }

    [Fact]
    public async Task Process_UsesActiveDeliveryIntervalAndFlagsSessionPastCutoff()
    {
        await using var db = CreateDb();
        db.Branches.Add(new Branch
        {
            Id = 7,
            Name = "Centro",
            Address = "A",
            Phone1 = "1",
            DeliveryTrackingLightIntervalSeconds = 300,
            DeliveryTrackingActiveIntervalSeconds = 30,
        });
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 10,
            DeliverymanId = 1,
            BranchId = 7,
            DeviceInstallationId = "device",
            DevicePlatform = "android",
            StartedAt = BaseTime,
            AutoCloseAt = BaseTime.AddSeconds(30),
            LastCommunicationAt = BaseTime,
            Status = DeliveryWorkSessionStatus.Active,
        });
        db.DeliverymanLocations.Add(new DeliverymanLocation
        {
            Id = 100,
            DeliverymanId = 1,
            WorkSessionId = 10,
            Latitude = 4.6m,
            Longitude = -74.08m,
            TrackingMode = DeliveryTrackingMode.ActiveDelivery,
            RecordedAt = BaseTime,
        });
        await db.SaveChangesAsync();
        var service = new DeliveryTrackingAlertService(
            db,
            new FakeClock(BaseTime.AddSeconds(61)));

        await service.ProcessAsync();

        var noCommunication = db.DeliveryTrackingAlerts.Single(
            x => x.AlertType == DeliveryTrackingAlertType.NoCommunication);
        Assert.Equal(BaseTime.AddSeconds(60), noCommunication.OccurredAt);
        Assert.Contains("30 segundos", noCommunication.Message);
        var pastCutoff = db.DeliveryTrackingAlerts.Single(
            x => x.AlertType == DeliveryTrackingAlertType.SessionPastAutoClose);
        Assert.Equal(DeliveryTrackingAlertSeverity.Critical, pastCutoff.Severity);
        Assert.Equal(BaseTime.AddSeconds(30), pastCutoff.OccurredAt);
    }

    private static DeliveryDeviceEvent DeviceEvent(
        long id,
        DeliveryDeviceEventType type,
        int minute,
        string? details = null) => new()
    {
        Id = id,
        DeliverymanId = 1,
        WorkSessionId = 10,
        EventType = type,
        Details = details,
        RecordedAt = BaseTime.AddMinutes(minute),
        SyncedAt = BaseTime.AddMinutes(minute).AddSeconds(2),
    };

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}

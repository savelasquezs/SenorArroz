using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
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
            DeviceEvent(204, DeliveryDeviceEventType.InternetRecovered, 4, "queued_location_count=4"),
            DeviceEvent(205, DeliveryDeviceEventType.AppStopped, 10, "detected_on_next_launch"));
        db.UserDeviceTokens.AddRange(
            new UserDeviceToken { Id = 1, UserId = 1, Token = "deliveryman-token" },
            new UserDeviceToken { Id = 2, UserId = 2, Token = "other-token" });
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
        var fcm = new FakeFcmPushService();
        var service = CreateService(db, clock, fcm);

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
        var interruption = db.DeliveryTrackingIncidents.Single(
            x => x.IncidentType == DeliveryTrackingIncidentType.TrackingInterruption);
        Assert.Equal("app_or_tracking_service_stopped", interruption.ClassificationReason);
        Assert.Equal(205, interruption.SourceDeviceEventId);
        Assert.Equal(4, fcm.Sends.Count);
        Assert.All(fcm.Sends, send => Assert.Equal(["deliveryman-token"], send.Tokens));
        Assert.All(fcm.Sends, send =>
        {
            Assert.Equal(DeliveryTrackingReviewPolicy.NotificationTitle, send.Title);
            Assert.Equal(DeliveryTrackingReviewPolicy.NotificationType, send.Data!["type"]);
            Assert.Equal("1", send.Data["deliverymanId"]);
            Assert.Equal(DeliveryTrackingReviewPolicy.NotificationChannelId, send.AndroidChannelId);
        });
        Assert.Contains(fcm.Sends, send => send.Data!["alertType"] == "gps_disabled");
        Assert.Contains(fcm.Sends, send => send.Data!["alertType"] == "location_permission_revoked");
        Assert.Contains(fcm.Sends, send => send.Data!["alertType"] == "unexpected_stay");
        Assert.Contains(fcm.Sends, send => send.Data!["alertType"] == "no_communication");
        Assert.Contains(fcm.Sends, send => send.Body.Contains("lugar no autorizado"));
        Assert.Contains(fcm.Sends, send => send.Body.Contains("apagaste la ubicación"));
        Assert.Equal(0, await service.ProcessAsync());
        Assert.Equal(4, fcm.Sends.Count);

        var incident = db.DeliveryTrackingIncidents.Single(
            x => x.IncidentType == DeliveryTrackingIncidentType.Stay);
        incident.ReviewStatus = DeliveryIncidentReviewStatus.Justified;
        db.DeliveryWorkSessions.Single().LastCommunicationAt = BaseTime.AddMinutes(11);
        await db.SaveChangesAsync();
        clock.UtcNow = BaseTime.AddMinutes(11);

        Assert.True(await service.ProcessAsync() >= 2);
        Assert.Equal(DeliveryTrackingAlertStatus.Resolved,
            db.DeliveryTrackingAlerts.Single(x => x.AlertType == DeliveryTrackingAlertType.UnexpectedStay).Status);
        var recoveredInterruption = db.DeliveryTrackingAlerts.Single(
            x => x.AlertType == DeliveryTrackingAlertType.NoCommunication);
        Assert.Equal(DeliveryTrackingAlertStatus.Active, recoveredInterruption.Status);
        Assert.Equal(BaseTime.AddMinutes(11), recoveredInterruption.RecoveredAt);
        Assert.NotNull(recoveredInterruption.IncidentId);
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
        var fcm = new FakeFcmPushService();
        var service = CreateService(db, new FakeClock(BaseTime.AddSeconds(121)), fcm);

        await service.ProcessAsync();

        var noCommunication = db.DeliveryTrackingAlerts.Single(
            x => x.AlertType == DeliveryTrackingAlertType.NoCommunication);
        Assert.Equal(BaseTime.AddSeconds(120), noCommunication.OccurredAt);
        Assert.Equal(DeliveryTrackingAlertSeverity.RequiresReview, noCommunication.Severity);
        Assert.Contains("30 segundos", noCommunication.Message);
        var pastCutoff = db.DeliveryTrackingAlerts.Single(
            x => x.AlertType == DeliveryTrackingAlertType.SessionPastAutoClose);
        Assert.Equal(DeliveryTrackingAlertSeverity.Critical, pastCutoff.Severity);
        Assert.Equal(BaseTime.AddSeconds(30), pastCutoff.OccurredAt);
        Assert.Empty(fcm.Sends);
    }

    [Fact]
    public async Task Process_AddsPendingReviewStayToAlertsConsumedByDailyAudit()
    {
        await using var db = CreateDb();
        var pending = PendingReviewStay(300);
        var reviewed = PendingReviewStay(301);
        reviewed.ReviewStatus = DeliveryIncidentReviewStatus.Justified;
        reviewed.ReviewedByUserId = 21;
        reviewed.ReviewedAt = BaseTime.AddMinutes(14);
        db.DeliveryTrackingIncidents.AddRange(pending, reviewed);
        db.UserDeviceTokens.Add(new UserDeviceToken
        {
            Id = 1,
            UserId = 1,
            Token = "deliveryman-token",
        });
        await db.SaveChangesAsync();
        var fcm = new FakeFcmPushService();
        var service = CreateService(db, new FakeClock(BaseTime.AddMinutes(15)), fcm);

        Assert.Equal(2, await service.ProcessAsync());

        var alert = db.DeliveryTrackingAlerts.Single(x => x.IncidentId == pending.Id);
        Assert.Equal(DeliveryTrackingAlertType.UnexpectedStay, alert.AlertType);
        Assert.Equal(DeliveryTrackingAlertSeverity.RequiresReview, alert.Severity);
        Assert.Equal(DeliveryTrackingAlertStatus.Active, alert.Status);
        Assert.Equal("Permanencia pendiente de revisión", alert.Title);
        Assert.Contains("requiere revisión administrativa", alert.Message);
        Assert.Equal(720, alert.DurationSeconds);
        Assert.Equal(4.6m, alert.StartLatitude);
        Assert.Equal(-74.08m, alert.StartLongitude);

        var reviewedAlert = db.DeliveryTrackingAlerts.Single(x => x.IncidentId == reviewed.Id);
        Assert.Equal(DeliveryTrackingAlertStatus.Resolved, reviewedAlert.Status);
        Assert.Equal(reviewed.ReviewedAt, reviewedAlert.ResolvedAt);
        Assert.Equal(reviewed.ReviewedByUserId, reviewedAlert.ResolvedByUserId);
        Assert.Single(fcm.Sends);
        Assert.Equal("unexpected_stay", fcm.Sends[0].Data!["alertType"]);
        Assert.Equal(0, await service.ProcessAsync());
        Assert.Single(fcm.Sends);
    }

    [Fact]
    public async Task Process_PersistsReviewAlertWhenFcmFails()
    {
        await using var db = CreateDb();
        db.DeliveryTrackingIncidents.Add(PendingReviewStay(300));
        db.UserDeviceTokens.Add(new UserDeviceToken
        {
            Id = 1,
            UserId = 1,
            Token = "deliveryman-token",
        });
        await db.SaveChangesAsync();
        var fcm = new FakeFcmPushService { Exception = new InvalidOperationException("FCM unavailable") };
        var service = CreateService(db, new FakeClock(BaseTime.AddMinutes(15)), fcm);

        await service.ProcessAsync();

        Assert.Equal(DeliveryTrackingAlertStatus.Active, db.DeliveryTrackingAlerts.Single().Status);
        Assert.Single(fcm.Sends);
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

    private static DeliveryTrackingIncident PendingReviewStay(long id) => new()
    {
        Id = id,
        BranchId = 7,
        DeliverymanId = 1,
        WorkSessionId = 10,
        IncidentType = DeliveryTrackingIncidentType.Stay,
        StayClassification = DeliveryStayClassification.PendingReview,
        StartedAt = BaseTime,
        EndedAt = BaseTime.AddMinutes(12),
        DurationSeconds = 720,
        CenterLatitude = 4.6m,
        CenterLongitude = -74.08m,
        RadiusMeters = 10,
        AverageAccuracyMeters = 8,
        SourceUpdatedAt = BaseTime.AddMinutes(12),
        EvidenceCapturedAt = BaseTime.AddMinutes(13),
        EvidenceComplete = true,
        UpdatedAt = BaseTime.AddMinutes(13),
    };

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DeliveryTrackingAlertService CreateService(
        ApplicationDbContext db,
        FakeClock clock,
        IFcmPushService? fcm = null) => new(
            db,
            clock,
            fcm ?? new FakeFcmPushService(),
            NullLogger<DeliveryTrackingAlertService>.Instance);

    private sealed class FakeFcmPushService : IFcmPushService
    {
        public List<PushSend> Sends { get; } = [];
        public Exception? Exception { get; init; }

        public Task SendToTokensAsync(
            IReadOnlyList<string> tokens,
            string title,
            string body,
            Dictionary<string, string>? data = null,
            CancellationToken cancellationToken = default,
            string? correlationId = null,
            string androidChannelId = "delivery_orders")
        {
            Sends.Add(new PushSend(tokens, title, body, data, androidChannelId));
            return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
        }
    }

    private sealed record PushSend(
        IReadOnlyList<string> Tokens,
        string Title,
        string Body,
        Dictionary<string, string>? Data,
        string AndroidChannelId);
}

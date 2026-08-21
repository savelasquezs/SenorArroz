using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public class DeliveryTrackingAlertService : IDeliveryTrackingAlertService
{
    private const int BatchSize = 200;
    private const int ReviewSilenceSeconds = 600;
    private const int ReviewOfflineDurationSeconds = 420;
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IFcmPushService _fcm;
    private readonly ILogger<DeliveryTrackingAlertService> _logger;
    private readonly List<DeliveryTrackingAlert> _newReviewAlerts = [];

    public DeliveryTrackingAlertService(
        IApplicationDbContext db,
        IClock clock,
        IFcmPushService fcm,
        ILogger<DeliveryTrackingAlertService> logger)
    {
        _db = db;
        _clock = clock;
        _fcm = fcm;
        _logger = logger;
    }

    public async Task<int> ProcessAsync(CancellationToken cancellationToken = default)
    {
        _newReviewAlerts.Clear();
        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        var changes = 0;
        changes += await ProcessDeviceEventsAsync(nowUtc, cancellationToken);
        changes += await ProcessReviewableStaysAsync(nowUtc, cancellationToken);
        changes += await ProcessActiveSessionsAsync(nowUtc, cancellationToken);
        if (changes > 0)
            await _db.SaveChangesAsync(cancellationToken);
        var incidentChanges = await ProcessGpsDisabledIncidentsAsync(nowUtc, cancellationToken);
        incidentChanges += await ProcessTrackingInterruptionIncidentsAsync(nowUtc, cancellationToken);
        if (incidentChanges > 0)
            await _db.SaveChangesAsync(cancellationToken);
        await NotifyDeliverymenAsync(_newReviewAlerts, cancellationToken);
        return changes + incidentChanges;
    }

    private void AddAlert(DeliveryTrackingAlert alert)
    {
        _db.DeliveryTrackingAlerts.Add(alert);
        QueueReviewNotification(alert);
    }

    private void QueueReviewNotification(DeliveryTrackingAlert alert)
    {
        if (alert.Status != DeliveryTrackingAlertStatus.Active
            || !DeliveryTrackingReviewPolicy.Includes(alert.AlertType)
            || (alert.AlertType == DeliveryTrackingAlertType.NoCommunication
                && alert.Severity != DeliveryTrackingAlertSeverity.RequiresReview)
            || _newReviewAlerts.Contains(alert))
            return;
        _newReviewAlerts.Add(alert);
    }

    private async Task NotifyDeliverymenAsync(
        IReadOnlyCollection<DeliveryTrackingAlert> alerts,
        CancellationToken cancellationToken)
    {
        if (alerts.Count == 0)
            return;

        var deliverymanIds = alerts.Select(alert => alert.DeliverymanId).Distinct().ToList();
        var tokensByDeliveryman = (await _db.UserDeviceTokens.AsNoTracking()
                .Where(token => deliverymanIds.Contains(token.UserId))
                .Select(token => new { token.UserId, token.Token })
                .ToListAsync(cancellationToken))
            .GroupBy(token => token.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(token => token.Token).Distinct().ToList());

        foreach (var alert in alerts)
        {
            if (!tokensByDeliveryman.TryGetValue(alert.DeliverymanId, out var tokens)
                || tokens.Count == 0)
            {
                continue;
            }

            try
            {
                await _fcm.SendToTokensAsync(
                    tokens,
                    DeliveryTrackingReviewPolicy.NotificationTitle,
                    DeliveryTrackingReviewPolicy.NotificationBody(alert.AlertType),
                    new Dictionary<string, string>
                    {
                        ["type"] = DeliveryTrackingReviewPolicy.NotificationType,
                        ["alertId"] = alert.Id.ToString(CultureInfo.InvariantCulture),
                        ["alertType"] = DeliveryTrackingReviewPolicy.AlertTypeCode(alert.AlertType),
                        ["deliverymanId"] = alert.DeliverymanId.ToString(CultureInfo.InvariantCulture),
                    },
                    cancellationToken,
                    $"tracking-review-{alert.Id}",
                    DeliveryTrackingReviewPolicy.NotificationChannelId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "No fue posible enviar el aviso de revisión de seguimiento {AlertId} al domiciliario {DeliverymanId}.",
                    alert.Id,
                    alert.DeliverymanId);
            }
        }
    }

    private async Task<int> ProcessDeviceEventsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var negativeTypes = new[]
        {
            DeliveryDeviceEventType.GpsDisabled,
            DeliveryDeviceEventType.LocationPermissionRevoked,
            DeliveryDeviceEventType.AirplaneModeEnabled,
            DeliveryDeviceEventType.WifiDisabled,
            DeliveryDeviceEventType.DeviceRestarted,
            DeliveryDeviceEventType.AppStopped,
            DeliveryDeviceEventType.LocationServiceRestarted,
        };
        var events = await _db.DeliveryDeviceEvents
            .Where(deviceEvent => (negativeTypes.Contains(deviceEvent.EventType)
                    || (deviceEvent.EventType == DeliveryDeviceEventType.InternetRecovered
                        && (deviceEvent.OfflineLocationCount > 0
                            || (deviceEvent.Details != null
                                && deviceEvent.Details.Contains("queued_location_count=")))))
                && !_db.DeliveryTrackingAlerts.Any(alert => alert.SourceDeviceEventId == deviceEvent.Id)
                && !_db.DeliveryIncidentDeviceEventEvidence.Any(evidence =>
                    evidence.SourceDeviceEventId == deviceEvent.Id))
            .OrderBy(x => x.RecordedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (events.Count == 0)
        {
            return await EnrichRecoveredDeviceAlertsAsync(nowUtc, cancellationToken);
        }

        var sessionIds = events.Select(x => x.WorkSessionId).Distinct().ToList();
        var branches = await _db.DeliveryWorkSessions.AsNoTracking()
            .Where(x => sessionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.BranchId, cancellationToken);
        var recoveryEvents = await _db.DeliveryDeviceEvents.AsNoTracking()
            .Where(x => sessionIds.Contains(x.WorkSessionId)
                && (x.EventType == DeliveryDeviceEventType.GpsEnabled
                    || x.EventType == DeliveryDeviceEventType.LocationPermissionRecovered))
            .ToListAsync(cancellationToken);
        var locationRows = await _db.DeliverymanLocations.AsNoTracking()
            .Where(x => x.WorkSessionId.HasValue && sessionIds.Contains(x.WorkSessionId.Value))
            .Select(x => new DeviceAlertLocation(
                x.WorkSessionId!.Value,
                x.Latitude,
                x.Longitude,
                x.RecordedAt,
                x.Id))
            .ToListAsync(cancellationToken);
        var changes = 0;
        foreach (var deviceEvent in events)
        {
            if (!branches.TryGetValue(deviceEvent.WorkSessionId, out var branchId))
                continue;
            if (deviceEvent.EventType == DeliveryDeviceEventType.InternetRecovered
                && deviceEvent.OfflineLocationCount.HasValue)
            {
                await ApplyVerifiedOfflineEvidenceAsync(deviceEvent, cancellationToken);
            }
            if (IsReviewInterruptionEvent(deviceEvent))
            {
                var existingInterruption = _db.DeliveryTrackingAlerts.Local
                    .Where(x => x.WorkSessionId == deviceEvent.WorkSessionId
                        && x.AlertType == DeliveryTrackingAlertType.NoCommunication
                        && x.Status == DeliveryTrackingAlertStatus.Active
                        && MatchesInterruptionEvent(x, deviceEvent))
                    .OrderByDescending(x => x.OccurredAt)
                    .FirstOrDefault();
                existingInterruption ??= await _db.DeliveryTrackingAlerts
                    .Where(x => x.WorkSessionId == deviceEvent.WorkSessionId
                        && x.AlertType == DeliveryTrackingAlertType.NoCommunication
                        && x.Status == DeliveryTrackingAlertStatus.Active
                        && x.OccurredAt <= deviceEvent.RecordedAt
                        && (x.DeduplicationKey.StartsWith("session:")
                            ? !x.RecoveredAt.HasValue
                                || deviceEvent.RecordedAt <= x.RecoveredAt.Value.AddMinutes(1)
                            : x.LastOccurredAt >= deviceEvent.RecordedAt.AddMinutes(-1)
                                && x.LastOccurredAt <= deviceEvent.RecordedAt.AddMinutes(1)))
                    .OrderByDescending(x => x.OccurredAt)
                    .FirstOrDefaultAsync(cancellationToken);
                if (existingInterruption is not null)
                {
                    var currentSource = events.FirstOrDefault(x => x.Id == existingInterruption.SourceDeviceEventId);
                    if (currentSource is null
                        || DirectInterruptionPriority(deviceEvent.EventType) > DirectInterruptionPriority(currentSource.EventType))
                    {
                        existingInterruption.SourceDeviceEventId = deviceEvent.Id;
                        existingInterruption.Message = BuildDirectInterruptionMessage(deviceEvent);
                    }
                    existingInterruption.LastOccurredAt = existingInterruption.LastOccurredAt > deviceEvent.RecordedAt
                        ? existingInterruption.LastOccurredAt
                        : deviceEvent.RecordedAt;
                    existingInterruption.UpdatedAt = nowUtc;
                    changes++;
                    continue;
                }
            }
            DeliveryTrackingAlert? alert = deviceEvent.EventType switch
            {
                DeliveryDeviceEventType.GpsDisabled => CreateFromEvent(
                    deviceEvent,
                    branchId,
                    DeliveryTrackingAlertType.GpsDisabled,
                    DeliveryTrackingAlertSeverity.Warning,
                    "GPS apagado durante la jornada",
                    "El dispositivo reportó que el servicio de ubicación fue desactivado."),
                DeliveryDeviceEventType.LocationPermissionRevoked => CreateFromEvent(
                    deviceEvent,
                    branchId,
                    DeliveryTrackingAlertType.LocationPermissionRevoked,
                    DeliveryTrackingAlertSeverity.Critical,
                    "Permiso de ubicación retirado",
                    "La aplicación perdió el permiso necesario para registrar ubicaciones."),
                DeliveryDeviceEventType.InternetRecovered => CreateOfflineQueueAlert(deviceEvent, branchId),
                DeliveryDeviceEventType.AirplaneModeEnabled
                    or DeliveryDeviceEventType.WifiDisabled
                    or DeliveryDeviceEventType.DeviceRestarted
                    or DeliveryDeviceEventType.AppStopped
                    or DeliveryDeviceEventType.LocationServiceRestarted =>
                    CreateDirectInterruptionAlert(deviceEvent, branchId),
                _ => null,
            };
            if (alert is null)
                continue;
            if (alert.AlertType == DeliveryTrackingAlertType.GpsDisabled
                || alert.AlertType == DeliveryTrackingAlertType.LocationPermissionRevoked)
            {
                ApplyDeviceEvidence(alert, recoveryEvents, locationRows);
            }
            alert.CreatedAt = nowUtc;
            alert.UpdatedAt = nowUtc;
            AddAlert(alert);
            changes++;
        }

        return changes + await EnrichRecoveredDeviceAlertsAsync(nowUtc, cancellationToken);
    }

    private async Task<int> EnrichRecoveredDeviceAlertsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var alerts = await _db.DeliveryTrackingAlerts
            .Where(x => (x.AlertType == DeliveryTrackingAlertType.GpsDisabled
                    || x.AlertType == DeliveryTrackingAlertType.LocationPermissionRevoked)
                && x.WorkSessionId.HasValue
                && _db.DeliveryWorkSessions.Any(session =>
                    session.Id == x.WorkSessionId.Value
                    && session.Status == DeliveryWorkSessionStatus.Active)
                && (x.RecoveredAt == null
                    || x.StartLatitude == null
                    || x.EndLatitude == null))
            .OrderBy(x => x.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (alerts.Count == 0)
            return 0;

        var sessionIds = alerts.Where(x => x.WorkSessionId.HasValue)
            .Select(x => x.WorkSessionId!.Value).Distinct().ToList();
        var recoveryEvents = await _db.DeliveryDeviceEvents.AsNoTracking()
            .Where(x => sessionIds.Contains(x.WorkSessionId)
                && (x.EventType == DeliveryDeviceEventType.GpsEnabled
                    || x.EventType == DeliveryDeviceEventType.LocationPermissionRecovered))
            .ToListAsync(cancellationToken);
        var locationRows = await _db.DeliverymanLocations.AsNoTracking()
            .Where(x => x.WorkSessionId.HasValue && sessionIds.Contains(x.WorkSessionId.Value))
            .Select(x => new DeviceAlertLocation(
                x.WorkSessionId!.Value,
                x.Latitude,
                x.Longitude,
                x.RecordedAt,
                x.Id))
            .ToListAsync(cancellationToken);
        var changes = 0;
        foreach (var alert in alerts)
        {
            if (ApplyDeviceEvidence(alert, recoveryEvents, locationRows))
            {
                alert.UpdatedAt = nowUtc;
                changes++;
            }
        }
        return changes;
    }

    private static bool ApplyDeviceEvidence(
        DeliveryTrackingAlert alert,
        IReadOnlyCollection<DeliveryDeviceEvent> recoveryEvents,
        IReadOnlyCollection<DeviceAlertLocation> locations)
    {
        if (!alert.WorkSessionId.HasValue)
            return false;

        var changed = false;
        var sessionId = alert.WorkSessionId.Value;
        var occurrenceLocation = locations
            .Where(x => x.WorkSessionId == sessionId && x.RecordedAt <= alert.OccurredAt)
            .OrderByDescending(x => x.RecordedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        if (occurrenceLocation is not null && alert.StartLatitude is null)
        {
            alert.StartLatitude = occurrenceLocation.Latitude;
            alert.StartLongitude = occurrenceLocation.Longitude;
            alert.StartLocationRecordedAt = occurrenceLocation.RecordedAt;
            changed = true;
        }

        var recoveryType = alert.AlertType == DeliveryTrackingAlertType.GpsDisabled
            ? DeliveryDeviceEventType.GpsEnabled
            : DeliveryDeviceEventType.LocationPermissionRecovered;
        var recovery = recoveryEvents
            .Where(x => x.WorkSessionId == sessionId
                && x.EventType == recoveryType
                && x.RecordedAt >= alert.OccurredAt)
            .OrderBy(x => x.RecordedAt)
            .ThenBy(x => x.Id)
            .FirstOrDefault();
        if (recovery is not null && alert.RecoveredAt is null)
        {
            alert.RecoveryDeviceEventId = recovery.Id;
            alert.RecoveredAt = recovery.RecordedAt;
            alert.DurationSeconds = Math.Max(
                0,
                (int)Math.Round((recovery.RecordedAt - alert.OccurredAt).TotalSeconds));
            alert.LastOccurredAt = recovery.RecordedAt;
            changed = true;
        }

        if (alert.RecoveredAt.HasValue && alert.EndLatitude is null)
        {
            var recoveryLocation = locations
                .Where(x => x.WorkSessionId == sessionId && x.RecordedAt >= alert.RecoveredAt.Value)
                .OrderBy(x => x.RecordedAt)
                .ThenBy(x => x.Id)
                .FirstOrDefault();
            if (recoveryLocation is not null)
            {
                alert.EndLatitude = recoveryLocation.Latitude;
                alert.EndLongitude = recoveryLocation.Longitude;
                alert.EndLocationRecordedAt = recoveryLocation.RecordedAt;
                changed = true;
            }
        }

        var message = BuildDeviceEvidenceMessage(alert);
        if (!string.Equals(alert.Message, message, StringComparison.Ordinal))
        {
            alert.Message = message;
            changed = true;
        }
        return changed;
    }

    private static string BuildDeviceEvidenceMessage(DeliveryTrackingAlert alert)
    {
        var eventName = alert.AlertType == DeliveryTrackingAlertType.GpsDisabled
            ? "El GPS fue apagado durante la jornada."
            : "El permiso de ubicación fue retirado durante la jornada.";
        var occurrence = FormatLocationEvidence(
            "Última ubicación antes del evento",
            alert.StartLatitude,
            alert.StartLongitude);
        if (!alert.RecoveredAt.HasValue)
            return $"{eventName} {occurrence} Aún no se ha registrado su recuperación.";

        var recovery = FormatLocationEvidence(
            "Primera ubicación después de recuperarlo",
            alert.EndLatitude,
            alert.EndLongitude);
        return $"{eventName} Duración: {FormatDuration(alert.DurationSeconds)}. {occurrence} {recovery}";
    }

    private static string FormatLocationEvidence(string label, decimal? latitude, decimal? longitude)
    {
        if (!latitude.HasValue || !longitude.HasValue)
            return $"{label}: no disponible.";
        return $"{label}: {latitude.Value.ToString("0.000000", CultureInfo.InvariantCulture)}, " +
            $"{longitude.Value.ToString("0.000000", CultureInfo.InvariantCulture)}.";
    }

    internal static string FormatDuration(int? totalSeconds)
    {
        if (!totalSeconds.HasValue)
            return "pendiente";
        var duration = TimeSpan.FromSeconds(Math.Max(0, totalSeconds.Value));
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours} h {duration.Minutes} min {duration.Seconds} s";
        if (duration.TotalMinutes >= 1)
            return $"{duration.Minutes} min {duration.Seconds} s";
        return $"{duration.Seconds} s";
    }

    private sealed record DeviceAlertLocation(
        int WorkSessionId,
        decimal Latitude,
        decimal Longitude,
        DateTime RecordedAt,
        long Id);

    private async Task<int> ProcessGpsDisabledIncidentsAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var alerts = await _db.DeliveryTrackingAlerts.AsNoTracking()
            .Where(alert => (alert.AlertType == DeliveryTrackingAlertType.GpsDisabled
                    || alert.AlertType == DeliveryTrackingAlertType.LocationPermissionRevoked)
                && alert.SourceDeviceEventId.HasValue
                && !_db.DeliveryTrackingIncidents.Any(incident =>
                    incident.SourceDeviceEventId == alert.SourceDeviceEventId
                    && incident.SourceUpdatedAt >= alert.UpdatedAt))
            .OrderBy(alert => alert.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (alerts.Count == 0)
            return 0;

        var sourceEventIds = alerts.Select(x => x.SourceDeviceEventId!.Value).ToList();
        var recoveryEventIds = alerts.Where(x => x.RecoveryDeviceEventId.HasValue)
            .Select(x => x.RecoveryDeviceEventId!.Value)
            .ToList();
        var eventIds = sourceEventIds.Concat(recoveryEventIds).Distinct().ToList();
        var events = await _db.DeliveryDeviceEvents.AsNoTracking()
            .Where(x => eventIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var sessionIds = alerts.Where(x => x.WorkSessionId.HasValue)
            .Select(x => x.WorkSessionId!.Value)
            .Distinct()
            .ToList();
        var locationsBySession = (await _db.DeliverymanLocations.AsNoTracking()
                .Where(x => x.WorkSessionId.HasValue && sessionIds.Contains(x.WorkSessionId.Value))
                .OrderBy(x => x.RecordedAt)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.WorkSessionId!.Value)
            .ToDictionary(x => x.Key, x => x.ToList());
        var existingIncidents = await _db.DeliveryTrackingIncidents
            .Where(x => x.SourceDeviceEventId.HasValue
                && sourceEventIds.Contains(x.SourceDeviceEventId.Value))
            .ToDictionaryAsync(x => x.SourceDeviceEventId!.Value, cancellationToken);
        var existingIncidentIds = existingIncidents.Values.Select(x => x.Id).ToList();
        var existingLocationEvidence = existingIncidentIds.Count == 0
            ? []
            : await _db.DeliveryIncidentLocationEvidence.AsNoTracking()
                .Where(x => existingIncidentIds.Contains(x.IncidentId))
                .Select(x => new { x.IncidentId, x.SourceLocationId })
                .ToListAsync(cancellationToken);
        var existingEventEvidence = existingIncidentIds.Count == 0
            ? []
            : await _db.DeliveryIncidentDeviceEventEvidence.AsNoTracking()
                .Where(x => existingIncidentIds.Contains(x.IncidentId))
                .Select(x => new { x.IncidentId, x.SourceDeviceEventId })
                .ToListAsync(cancellationToken);

        var changes = 0;
        foreach (var alert in alerts)
        {
            if (!alert.WorkSessionId.HasValue
                || !events.TryGetValue(alert.SourceDeviceEventId!.Value, out var sourceEvent))
            {
                continue;
            }

            var isNew = !existingIncidents.TryGetValue(sourceEvent.Id, out var incident);
            if (isNew)
            {
                incident = new DeliveryTrackingIncident
                {
                    IncidentType = DeliveryTrackingIncidentType.LocationDisabled,
                    SourceDeviceEventId = sourceEvent.Id,
                    ReviewStatus = DeliveryIncidentReviewStatus.Pending,
                    CreatedAt = nowUtc,
                };
                _db.DeliveryTrackingIncidents.Add(incident);
                existingIncidents[sourceEvent.Id] = incident;
            }

            var sessionLocations = locationsBySession.GetValueOrDefault(alert.WorkSessionId.Value, []);
            var startLocation = sessionLocations
                .Where(x => x.RecordedAt <= alert.OccurredAt)
                .OrderByDescending(x => x.RecordedAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();
            var endLocation = alert.RecoveredAt.HasValue
                ? sessionLocations
                    .Where(x => x.RecordedAt >= alert.RecoveredAt.Value)
                    .OrderBy(x => x.RecordedAt)
                    .ThenBy(x => x.Id)
                    .FirstOrDefault()
                : null;
            var evidenceLocations = new[] { startLocation, endLocation }
                .Where(x => x is not null)
                .Cast<DeliverymanLocation>()
                .GroupBy(x => x.Id)
                .Select(x => x.First())
                .ToList();

            incident!.BranchId = alert.BranchId;
            incident.DeliverymanId = alert.DeliverymanId;
            incident.WorkSessionId = alert.WorkSessionId.Value;
            incident.StartedAt = alert.OccurredAt;
            incident.EndedAt = alert.RecoveredAt ?? alert.OccurredAt;
            incident.DurationSeconds = alert.DurationSeconds ?? 0;
            incident.CenterLatitude = alert.StartLatitude ?? alert.EndLatitude;
            incident.CenterLongitude = alert.StartLongitude ?? alert.EndLongitude;
            incident.RadiusMeters = 0;
            incident.AverageAccuracyMeters = evidenceLocations
                .Where(x => x.AccuracyMeters.HasValue)
                .Select(x => x.AccuracyMeters!.Value)
                .DefaultIfEmpty(0)
                .Average();
            incident.StayClassification = null;
            incident.ClassificationReason = alert.AlertType == DeliveryTrackingAlertType.GpsDisabled
                ? "gps_disabled_during_work_session"
                : "location_permission_revoked_during_work_session";
            incident.InterruptionCause = alert.AlertType == DeliveryTrackingAlertType.GpsDisabled
                ? DeliveryInterruptionCause.GpsDisabled
                : DeliveryInterruptionCause.LocationPermissionRevoked;
            incident.InterruptionCertainty = DeliveryInterruptionCertainty.ConfirmedByDevice;
            incident.SourceUpdatedAt = alert.UpdatedAt;
            incident.EvidenceCapturedAt = nowUtc;
            incident.EvidenceComplete = alert.RecoveredAt.HasValue;
            incident.UpdatedAt = nowUtc;

            var knownLocationIds = isNew
                ? new HashSet<long>()
                : existingLocationEvidence
                    .Where(x => x.IncidentId == incident.Id)
                    .Select(x => x.SourceLocationId)
                    .ToHashSet();
            foreach (var location in evidenceLocations.Where(x => knownLocationIds.Add(x.Id)))
                incident.LocationEvidence.Add(CopyIncidentLocation(location));

            var knownEventIds = isNew
                ? new HashSet<long>()
                : existingEventEvidence
                    .Where(x => x.IncidentId == incident.Id)
                    .Select(x => x.SourceDeviceEventId)
                    .ToHashSet();
            var incidentEvents = new List<DeliveryDeviceEvent> { sourceEvent };
            if (alert.RecoveryDeviceEventId.HasValue
                && events.TryGetValue(alert.RecoveryDeviceEventId.Value, out var recoveryEvent))
            {
                incidentEvents.Add(recoveryEvent);
            }
            foreach (var deviceEvent in incidentEvents.Where(x => knownEventIds.Add(x.Id)))
                incident.DeviceEventEvidence.Add(CopyIncidentDeviceEvent(deviceEvent));

            changes++;
        }
        return changes;
    }

    private static DeliveryIncidentLocationEvidence CopyIncidentLocation(DeliverymanLocation source) => new()
    {
        SourceLocationId = source.Id,
        ClientPointId = source.ClientPointId,
        IsCorePoint = true,
        Latitude = source.Latitude,
        Longitude = source.Longitude,
        AccuracyMeters = source.AccuracyMeters,
        HeadingDegrees = source.HeadingDegrees,
        BatteryLevelPercent = source.BatteryLevelPercent,
        InternetAvailable = source.InternetAvailable,
        GpsEnabled = source.GpsEnabled,
        TrackingMode = source.TrackingMode,
        RecordedAt = source.RecordedAt,
        SyncedAt = source.SyncedAt,
    };

    private async Task<int> ProcessTrackingInterruptionIncidentsAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var alerts = await _db.DeliveryTrackingAlerts
            .Where(alert => alert.AlertType == DeliveryTrackingAlertType.NoCommunication
                && alert.Severity == DeliveryTrackingAlertSeverity.RequiresReview
                && alert.Status == DeliveryTrackingAlertStatus.Active
                && alert.WorkSessionId.HasValue
                && !_db.DeliveryTrackingIncidents.Any(incident =>
                    incident.AlertId == alert.Id && incident.SourceUpdatedAt >= alert.UpdatedAt))
            .OrderBy(alert => alert.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (alerts.Count == 0)
            return 0;

        var alertIds = alerts.Select(x => x.Id).ToList();
        var existing = await _db.DeliveryTrackingIncidents
            .Where(x => x.AlertId.HasValue && alertIds.Contains(x.AlertId.Value))
            .ToDictionaryAsync(x => x.AlertId!.Value, cancellationToken);
        var changes = 0;
        foreach (var alert in alerts)
        {
            var sessionId = alert.WorkSessionId!.Value;
            var evidenceEnd = alert.RecoveredAt ?? nowUtc;
            var locations = await _db.DeliverymanLocations.AsNoTracking()
                .Where(x => x.WorkSessionId == sessionId
                    && x.RecordedAt >= alert.OccurredAt.AddMinutes(-5)
                    && x.RecordedAt <= evidenceEnd.AddMinutes(5))
                .OrderBy(x => x.RecordedAt).ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);
            var events = await _db.DeliveryDeviceEvents.AsNoTracking()
                .Where(x => x.WorkSessionId == sessionId
                    && x.RecordedAt >= alert.OccurredAt.AddMinutes(-5)
                    && x.RecordedAt <= evidenceEnd.AddMinutes(5))
                .OrderBy(x => x.RecordedAt).ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);
            var before = locations.LastOrDefault(x => x.RecordedAt <= alert.OccurredAt);
            var after = alert.RecoveredAt.HasValue
                ? locations.FirstOrDefault(x => x.RecordedAt >= alert.RecoveredAt.Value)
                : null;
            var sourceEvent = alert.SourceDeviceEventId.HasValue
                ? events.FirstOrDefault(x => x.Id == alert.SourceDeviceEventId.Value)
                : null;
            var directEvent = sourceEvent is not null && IsDirectInterruptionEvent(sourceEvent.EventType)
                ? sourceEvent
                : events
                .Where(x => x.EventType is
                    DeliveryDeviceEventType.AirplaneModeEnabled
                    or DeliveryDeviceEventType.AppStopped
                    or DeliveryDeviceEventType.LocationServiceRestarted
                    or DeliveryDeviceEventType.WifiDisabled
                    or DeliveryDeviceEventType.DeviceRestarted)
                .OrderByDescending(x => x.EventType switch
                {
                    DeliveryDeviceEventType.AirplaneModeEnabled => 4,
                    DeliveryDeviceEventType.AppStopped
                        or DeliveryDeviceEventType.LocationServiceRestarted => 3,
                    DeliveryDeviceEventType.WifiDisabled => 2,
                    _ => 1,
                })
                .ThenByDescending(x => x.RecordedAt)
                .FirstOrDefault();
            var connectivityEvidence = events.Any(x =>
                    x.EventType == DeliveryDeviceEventType.InternetLost
                    || x.EventType == DeliveryDeviceEventType.InternetRecovered)
                || locations.Any(x => x.InternetAvailable == false || x.TrackingMode == DeliveryTrackingMode.Offline);
            var (cause, certainty, reason) = directEvent?.EventType switch
            {
                DeliveryDeviceEventType.AirplaneModeEnabled =>
                    (DeliveryInterruptionCause.AirplaneModeEnabled, DeliveryInterruptionCertainty.ConfirmedByDevice, "airplane_mode_enabled"),
                DeliveryDeviceEventType.AppStopped =>
                    (DeliveryInterruptionCause.AppOrTrackingServiceStopped, DeliveryInterruptionCertainty.ConfirmedByDevice, "app_or_tracking_service_stopped"),
                DeliveryDeviceEventType.LocationServiceRestarted =>
                    (DeliveryInterruptionCause.AppOrTrackingServiceStopped, DeliveryInterruptionCertainty.ConfirmedByDevice, "app_or_tracking_service_stopped"),
                DeliveryDeviceEventType.WifiDisabled =>
                    (DeliveryInterruptionCause.WifiDisabled, DeliveryInterruptionCertainty.ConfirmedByDevice, "wifi_disabled"),
                DeliveryDeviceEventType.DeviceRestarted =>
                    (DeliveryInterruptionCause.DeviceRestarted, DeliveryInterruptionCertainty.ConfirmedByDevice, "device_restarted"),
                _ when connectivityEvidence =>
                    (DeliveryInterruptionCause.ConnectivityInterruption, DeliveryInterruptionCertainty.TechnicalEvidence, "connectivity_interruption"),
                _ =>
                    (DeliveryInterruptionCause.NotDetermined, DeliveryInterruptionCertainty.NotDetermined, "cause_not_determinable"),
            };

            if (!existing.TryGetValue(alert.Id, out var incident))
            {
                incident = new DeliveryTrackingIncident
                {
                    IncidentType = DeliveryTrackingIncidentType.TrackingInterruption,
                    AlertId = alert.Id,
                    ReviewStatus = DeliveryIncidentReviewStatus.Pending,
                    CreatedAt = nowUtc,
                };
                _db.DeliveryTrackingIncidents.Add(incident);
                existing[alert.Id] = incident;
            }

            incident.BranchId = alert.BranchId;
            incident.DeliverymanId = alert.DeliverymanId;
            incident.WorkSessionId = sessionId;
            incident.SourceDeviceEventId = alert.SourceDeviceEventId;
            incident.DeliveryRouteId = before?.DeliveryRouteId ?? after?.DeliveryRouteId;
            incident.StartedAt = alert.OccurredAt;
            incident.EndedAt = alert.RecoveredAt ?? alert.OccurredAt;
            incident.DurationSeconds = alert.DurationSeconds ?? 0;
            incident.CenterLatitude = before?.Latitude ?? after?.Latitude;
            incident.CenterLongitude = before?.Longitude ?? after?.Longitude;
            incident.RadiusMeters = 0;
            incident.AverageAccuracyMeters = new[] { before, after }
                .Where(x => x?.AccuracyMeters is not null)
                .Select(x => x!.AccuracyMeters!.Value)
                .DefaultIfEmpty(0).Average();
            incident.ClassificationReason = reason;
            incident.InterruptionCause = cause;
            incident.InterruptionCertainty = certainty;
            incident.SourceUpdatedAt = alert.UpdatedAt;
            incident.EvidenceCapturedAt = nowUtc;
            incident.EvidenceComplete = alert.RecoveredAt.HasValue;
            incident.UpdatedAt = nowUtc;

            var knownLocations = incident.Id == 0
                ? new HashSet<long>()
                : (await _db.DeliveryIncidentLocationEvidence.AsNoTracking()
                    .Where(x => x.IncidentId == incident.Id)
                    .Select(x => x.SourceLocationId)
                    .ToListAsync(cancellationToken)).ToHashSet();
            foreach (var location in new[] { before, after }.Where(x => x is not null).Cast<DeliverymanLocation>()
                         .Where(x => knownLocations.Add(x.Id)))
                incident.LocationEvidence.Add(CopyIncidentLocation(location));

            var knownEvents = incident.Id == 0
                ? new HashSet<long>()
                : (await _db.DeliveryIncidentDeviceEventEvidence.AsNoTracking()
                    .Where(x => x.IncidentId == incident.Id)
                    .Select(x => x.SourceDeviceEventId)
                    .ToListAsync(cancellationToken)).ToHashSet();
            foreach (var deviceEvent in events.Where(x => knownEvents.Add(x.Id)))
                incident.DeviceEventEvidence.Add(CopyIncidentDeviceEvent(deviceEvent));

            if (incident.Id == 0)
                await _db.SaveChangesAsync(cancellationToken);
            alert.IncidentId = incident.Id;
            changes++;
        }
        return changes;
    }

    private static DeliveryIncidentDeviceEventEvidence CopyIncidentDeviceEvent(DeliveryDeviceEvent source) => new()
    {
        SourceDeviceEventId = source.Id,
        ClientEventId = source.ClientEventId,
        EventType = source.EventType,
        BatteryLevelPercent = source.BatteryLevelPercent,
        InternetAvailable = source.InternetAvailable,
        GpsEnabled = source.GpsEnabled,
        LocationPermissionGranted = source.LocationPermissionGranted,
        Details = source.Details,
        OfflineLocationCount = source.OfflineLocationCount,
        OfflineStartedAt = source.OfflineStartedAt,
        OfflineEndedAt = source.OfflineEndedAt,
        RecordedAt = source.RecordedAt,
        SyncedAt = source.SyncedAt,
    };

    private async Task<int> ProcessReviewableStaysAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var incidents = await _db.DeliveryTrackingIncidents.AsNoTracking()
            .Where(incident => incident.IncidentType == DeliveryTrackingIncidentType.Stay
                && (incident.StayClassification == DeliveryStayClassification.PendingReview
                    || incident.StayClassification == DeliveryStayClassification.UnexpectedPlace)
                && !_db.DeliveryTrackingAlerts.Any(alert => alert.IncidentId == incident.Id))
            .OrderBy(x => x.StartedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        var changes = 0;
        foreach (var incident in incidents)
        {
            var alreadyReviewed = incident.ReviewStatus != DeliveryIncidentReviewStatus.Pending;
            AddAlert(new DeliveryTrackingAlert
            {
                BranchId = incident.BranchId,
                DeliverymanId = incident.DeliverymanId,
                WorkSessionId = incident.WorkSessionId,
                IncidentId = incident.Id,
                DeduplicationKey = $"incident:{incident.Id}:unexpected_stay",
                AlertType = DeliveryTrackingAlertType.UnexpectedStay,
                Severity = DeliveryTrackingAlertSeverity.RequiresReview,
                Status = alreadyReviewed
                    ? DeliveryTrackingAlertStatus.Resolved
                    : DeliveryTrackingAlertStatus.Active,
                Title = incident.StayClassification == DeliveryStayClassification.PendingReview
                    ? "Permanencia pendiente de revisión"
                    : "Permanencia en lugar no esperado",
                Message = incident.StayClassification == DeliveryStayClassification.PendingReview
                    ? $"Permaneció {FormatDuration(incident.DurationSeconds)} en un lugar que requiere " +
                        $"revisión administrativa. {FormatLocationEvidence("Lugar", incident.CenterLatitude, incident.CenterLongitude)}"
                    : $"Permaneció {FormatDuration(incident.DurationSeconds)} fuera de lugares " +
                        $"operativos conocidos. {FormatLocationEvidence("Lugar", incident.CenterLatitude, incident.CenterLongitude)}",
                OccurredAt = incident.StartedAt,
                LastOccurredAt = incident.EndedAt,
                DurationSeconds = incident.DurationSeconds,
                StartLatitude = incident.CenterLatitude,
                StartLongitude = incident.CenterLongitude,
                StartLocationRecordedAt = incident.StartedAt,
                ResolvedAt = alreadyReviewed ? incident.ReviewedAt ?? nowUtc : null,
                ResolvedByUserId = alreadyReviewed ? incident.ReviewedByUserId : null,
                ResolutionReason = alreadyReviewed ? "El incidente relacionado fue revisado." : null,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
            });
            changes++;
        }

        var reviewedAlerts = await (
            from alert in _db.DeliveryTrackingAlerts
            join incident in _db.DeliveryTrackingIncidents on alert.IncidentId equals incident.Id
            where alert.Status == DeliveryTrackingAlertStatus.Active
                && alert.AlertType == DeliveryTrackingAlertType.UnexpectedStay
                && incident.ReviewStatus != DeliveryIncidentReviewStatus.Pending
            select alert)
            .ToListAsync(cancellationToken);
        foreach (var alert in reviewedAlerts)
        {
            Resolve(alert, nowUtc, "El incidente relacionado fue revisado.");
            changes++;
        }
        return changes;
    }

    private async Task<int> ProcessActiveSessionsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var sessions = await _db.DeliveryWorkSessions.AsNoTracking()
            .Where(x => x.Status == DeliveryWorkSessionStatus.Active)
            .ToListAsync(cancellationToken);
        var sessionIds = sessions.Select(x => x.Id).ToList();
        var branchIds = sessions.Select(x => x.BranchId).Distinct().ToList();
        var branches = await _db.Branches.AsNoTracking()
            .Where(x => branchIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var locationRows = await _db.DeliverymanLocations.AsNoTracking()
            .Where(x => x.WorkSessionId.HasValue && sessionIds.Contains(x.WorkSessionId.Value))
            .Select(x => new { SessionId = x.WorkSessionId!.Value, x.RecordedAt, x.Id, x.TrackingMode })
            .ToListAsync(cancellationToken);
        var lastModes = locationRows.GroupBy(x => x.SessionId).ToDictionary(
            x => x.Key,
            x => x.OrderByDescending(p => p.RecordedAt).ThenByDescending(p => p.Id).First().TrackingMode);
        var relevantAlerts = await _db.DeliveryTrackingAlerts
            .Where(x => x.WorkSessionId.HasValue
                && (sessionIds.Contains(x.WorkSessionId.Value) || x.Status == DeliveryTrackingAlertStatus.Active)
                && (x.AlertType == DeliveryTrackingAlertType.NoCommunication
                    || x.AlertType == DeliveryTrackingAlertType.GpsDisabled
                    || x.AlertType == DeliveryTrackingAlertType.LocationPermissionRevoked
                    || x.AlertType == DeliveryTrackingAlertType.SessionPastAutoClose))
            .ToListAsync(cancellationToken);
        relevantAlerts.AddRange(_db.DeliveryTrackingAlerts.Local.Where(x =>
            x.WorkSessionId.HasValue
            && (x.AlertType == DeliveryTrackingAlertType.NoCommunication
                || x.AlertType == DeliveryTrackingAlertType.GpsDisabled
                || x.AlertType == DeliveryTrackingAlertType.LocationPermissionRevoked
                || x.AlertType == DeliveryTrackingAlertType.SessionPastAutoClose)
            && !relevantAlerts.Contains(x)));
        var relevantSessionIds = relevantAlerts
            .Where(x => x.WorkSessionId.HasValue)
            .Select(x => x.WorkSessionId!.Value)
            .Distinct()
            .ToList();
        var relevantSessionMap = await _db.DeliveryWorkSessions.AsNoTracking()
            .Where(x => relevantSessionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var keys = relevantAlerts.Select(x => x.DeduplicationKey).ToHashSet();
        var changes = 0;

        foreach (var session in sessions)
        {
            if (!branches.TryGetValue(session.BranchId, out var branch))
                continue;
            var intervalSeconds = lastModes.GetValueOrDefault(session.Id) == DeliveryTrackingMode.ActiveDelivery
                ? branch.DeliveryTrackingActiveIntervalSeconds
                : branch.DeliveryTrackingLightIntervalSeconds;
            var thresholdSeconds = lastModes.GetValueOrDefault(session.Id) == DeliveryTrackingMode.ActiveDelivery
                ? 120
                : Math.Max(1, intervalSeconds) * 2;
            var silenceSeconds = (nowUtc - session.LastCommunicationAt).TotalSeconds;
            if (silenceSeconds >= thresholdSeconds)
            {
                var key = $"session:{session.Id}:no_communication:{session.LastCommunicationAt.Ticks}";
                var hasActiveInterruption = relevantAlerts.Any(x =>
                    x.WorkSessionId == session.Id
                    && (x.AlertType == DeliveryTrackingAlertType.NoCommunication
                        || x.AlertType == DeliveryTrackingAlertType.GpsDisabled
                        || x.AlertType == DeliveryTrackingAlertType.LocationPermissionRevoked)
                    && x.Status == DeliveryTrackingAlertStatus.Active
                    && !x.RecoveredAt.HasValue);
                if (!keys.Contains(key) && !hasActiveInterruption)
                {
                    var requiresReview = silenceSeconds >= ReviewSilenceSeconds;
                    AddAlert(new DeliveryTrackingAlert
                    {
                        BranchId = session.BranchId,
                        DeliverymanId = session.DeliverymanId,
                        WorkSessionId = session.Id,
                        DeduplicationKey = key,
                        AlertType = DeliveryTrackingAlertType.NoCommunication,
                        Severity = requiresReview
                            ? DeliveryTrackingAlertSeverity.RequiresReview
                            : DeliveryTrackingAlertSeverity.Warning,
                        Status = DeliveryTrackingAlertStatus.Active,
                        Title = "Domiciliario sin comunicación",
                        Message = $"No se reciben datos desde hace {Math.Max(1, (int)silenceSeconds / 60)} minutos; se esperaban reportes cada {intervalSeconds} segundos.",
                        OccurredAt = session.LastCommunicationAt,
                        LastOccurredAt = nowUtc,
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc,
                    });
                    keys.Add(key);
                    changes++;
                }
            }

            var activeInterruption = relevantAlerts
                .Where(x => x.WorkSessionId == session.Id
                    && x.AlertType == DeliveryTrackingAlertType.NoCommunication
                    && x.Status == DeliveryTrackingAlertStatus.Active
                    && !x.RecoveredAt.HasValue)
                .OrderByDescending(x => x.OccurredAt)
                .FirstOrDefault();
            if (activeInterruption is not null
                && silenceSeconds >= ReviewSilenceSeconds
                && activeInterruption.Severity != DeliveryTrackingAlertSeverity.RequiresReview)
            {
                activeInterruption.Severity = DeliveryTrackingAlertSeverity.RequiresReview;
                activeInterruption.Title = "Interrupción prolongada pendiente de revisión";
                activeInterruption.Message = activeInterruption.SourceDeviceEventId.HasValue
                    ? $"{activeInterruption.Message} La interrupción superó 10 minutos y requiere revisión administrativa."
                    : "La interrupción de seguimiento superó 10 minutos y requiere revisión administrativa.";
                activeInterruption.UpdatedAt = nowUtc;
                QueueReviewNotification(activeInterruption);
                changes++;
            }

            if (nowUtc >= session.AutoCloseAt)
            {
                var key = $"session:{session.Id}:past_auto_close";
                if (!keys.Contains(key))
                {
                    AddAlert(new DeliveryTrackingAlert
                    {
                        BranchId = session.BranchId,
                        DeliverymanId = session.DeliverymanId,
                        WorkSessionId = session.Id,
                        DeduplicationKey = key,
                        AlertType = DeliveryTrackingAlertType.SessionPastAutoClose,
                        Severity = DeliveryTrackingAlertSeverity.Critical,
                        Status = DeliveryTrackingAlertStatus.Active,
                        Title = "Jornada activa después del cierre automático",
                        Message = "La jornada continúa activa después de la hora límite configurada.",
                        OccurredAt = session.AutoCloseAt,
                        LastOccurredAt = nowUtc,
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc,
                    });
                    keys.Add(key);
                    changes++;
                }
            }
        }

        var activeSessionMap = sessions.ToDictionary(x => x.Id);
        foreach (var alert in relevantAlerts.Where(x => x.Status == DeliveryTrackingAlertStatus.Active))
        {
            if (!alert.WorkSessionId.HasValue || !activeSessionMap.TryGetValue(alert.WorkSessionId.Value, out var session))
            {
                if (RequiresAdministrativeResolution(alert))
                {
                    if (alert.AlertType == DeliveryTrackingAlertType.NoCommunication
                        && alert.WorkSessionId.HasValue
                        && relevantSessionMap.TryGetValue(alert.WorkSessionId.Value, out var endedSession)
                        && endedSession.LastCommunicationAt > alert.OccurredAt
                        && !alert.RecoveredAt.HasValue)
                    {
                        alert.RecoveredAt = endedSession.LastCommunicationAt;
                        alert.LastOccurredAt = endedSession.LastCommunicationAt;
                        alert.DurationSeconds = Math.Max(0,
                            (int)Math.Round((endedSession.LastCommunicationAt - alert.OccurredAt).TotalSeconds));
                        alert.Message = $"El seguimiento se interrumpió durante {FormatDuration(alert.DurationSeconds)}. " +
                            "La finalización de la jornada no cierra la revisión administrativa.";
                        alert.UpdatedAt = nowUtc;
                        changes++;
                    }
                    continue;
                }
                Resolve(alert, nowUtc, "La jornada laboral finalizó.");
                changes++;
                continue;
            }
            if (alert.AlertType == DeliveryTrackingAlertType.NoCommunication
                && session.LastCommunicationAt > alert.OccurredAt
                && !alert.RecoveredAt.HasValue)
            {
                alert.RecoveredAt = session.LastCommunicationAt;
                alert.LastOccurredAt = session.LastCommunicationAt;
                alert.DurationSeconds = Math.Max(0,
                    (int)Math.Round((session.LastCommunicationAt - alert.OccurredAt).TotalSeconds));
                var sessionLocations = await _db.DeliverymanLocations.AsNoTracking()
                    .Where(x => x.WorkSessionId == session.Id)
                    .OrderBy(x => x.RecordedAt).ThenBy(x => x.Id)
                    .ToListAsync(cancellationToken);
                var before = sessionLocations.LastOrDefault(x => x.RecordedAt <= alert.OccurredAt);
                var after = sessionLocations.FirstOrDefault(x => x.RecordedAt >= session.LastCommunicationAt);
                alert.StartLatitude ??= before?.Latitude;
                alert.StartLongitude ??= before?.Longitude;
                alert.StartLocationRecordedAt ??= before?.RecordedAt;
                alert.EndLatitude ??= after?.Latitude;
                alert.EndLongitude ??= after?.Longitude;
                alert.EndLocationRecordedAt ??= after?.RecordedAt;
                var relatedEvents = await _db.DeliveryDeviceEvents.AsNoTracking()
                    .Where(x => x.WorkSessionId == session.Id
                        && x.RecordedAt >= alert.OccurredAt
                        && x.RecordedAt <= session.LastCommunicationAt.AddMinutes(1))
                    .ToListAsync(cancellationToken);
                var requiresReview = alert.Severity == DeliveryTrackingAlertSeverity.RequiresReview
                    || alert.DurationSeconds >= ReviewSilenceSeconds
                    || relatedEvents.Any(IsReviewableOfflineEvidence);
                if (requiresReview)
                {
                    var wasAlreadyReview = alert.Severity == DeliveryTrackingAlertSeverity.RequiresReview;
                    alert.Severity = DeliveryTrackingAlertSeverity.RequiresReview;
                    alert.Title = "Interrupción de seguimiento pendiente de revisión";
                    alert.Message = $"El seguimiento se interrumpió durante {FormatDuration(alert.DurationSeconds)}. " +
                        "La recuperación no cierra la revisión administrativa.";
                    alert.UpdatedAt = nowUtc;
                    if (!wasAlreadyReview)
                        QueueReviewNotification(alert);
                }
                else
                {
                    alert.Message = $"El seguimiento se recuperó después de {FormatDuration(alert.DurationSeconds)}.";
                    Resolve(alert, session.LastCommunicationAt, "Interrupción breve recuperada automáticamente.");
                }
                changes++;
            }
        }
        return changes;
    }

    private static bool RequiresAdministrativeResolution(DeliveryTrackingAlert alert) =>
        alert.AlertType == DeliveryTrackingAlertType.GpsDisabled
        || alert.AlertType == DeliveryTrackingAlertType.LocationPermissionRevoked
        || (alert.AlertType == DeliveryTrackingAlertType.NoCommunication
            && alert.Severity == DeliveryTrackingAlertSeverity.RequiresReview);

    private static DeliveryTrackingAlert CreateFromEvent(
        DeliveryDeviceEvent source,
        int branchId,
        DeliveryTrackingAlertType type,
        DeliveryTrackingAlertSeverity severity,
        string title,
        string message) => new()
    {
        BranchId = branchId,
        DeliverymanId = source.DeliverymanId,
        WorkSessionId = source.WorkSessionId,
        SourceDeviceEventId = source.Id,
        DeduplicationKey = $"device_event:{source.Id}:{type}",
        AlertType = type,
        Severity = severity,
        Status = DeliveryTrackingAlertStatus.Active,
        Title = title,
        Message = message,
        OccurredAt = source.RecordedAt,
        LastOccurredAt = source.RecordedAt,
    };

    private static DeliveryTrackingAlert? CreateOfflineQueueAlert(DeliveryDeviceEvent source, int branchId)
    {
        var count = source.OfflineLocationCount ?? ParseQueuedLocationCount(source.Details);
        if (count <= 0)
            return null;
        var durationSeconds = OfflineDurationSeconds(source);
        if (durationSeconds >= ReviewOfflineDurationSeconds)
        {
            return new DeliveryTrackingAlert
            {
                BranchId = branchId,
                DeliverymanId = source.DeliverymanId,
                WorkSessionId = source.WorkSessionId,
                SourceDeviceEventId = source.Id,
                DeduplicationKey = $"device_event:{source.Id}:offline_review",
                AlertType = DeliveryTrackingAlertType.NoCommunication,
                Severity = DeliveryTrackingAlertSeverity.RequiresReview,
                Status = DeliveryTrackingAlertStatus.Active,
                Title = "Interrupción offline pendiente de revisión",
                Message = $"Se sincronizaron {count} ubicaciones acumuladas sin conexión durante " +
                    $"{FormatDuration(durationSeconds)}.",
                OccurredAt = source.OfflineStartedAt ?? source.RecordedAt.AddSeconds(-durationSeconds),
                LastOccurredAt = source.OfflineEndedAt ?? source.RecordedAt,
                RecoveredAt = source.RecordedAt,
                DurationSeconds = durationSeconds,
            };
        }
        return new DeliveryTrackingAlert
        {
            BranchId = branchId,
            DeliverymanId = source.DeliverymanId,
            WorkSessionId = source.WorkSessionId,
            SourceDeviceEventId = source.Id,
            DeduplicationKey = $"device_event:{source.Id}:offline_queue",
            AlertType = DeliveryTrackingAlertType.OfflineLocationsQueued,
            Severity = DeliveryTrackingAlertSeverity.Informational,
            Status = DeliveryTrackingAlertStatus.Resolved,
            Title = "Ubicaciones offline sincronizadas",
            Message = $"El dispositivo acumuló {count} ubicaciones sin conexión y las envió al recuperar internet.",
            OccurredAt = source.RecordedAt,
            LastOccurredAt = source.RecordedAt,
            ResolvedAt = source.SyncedAt,
            ResolutionReason = "Sincronización recuperada.",
        };
    }

    private static DeliveryTrackingAlert CreateDirectInterruptionAlert(DeliveryDeviceEvent source, int branchId) =>
        CreateFromEvent(
            source,
            branchId,
            DeliveryTrackingAlertType.NoCommunication,
            DeliveryTrackingAlertSeverity.Warning,
            "Interrupción de seguimiento detectada",
            BuildDirectInterruptionMessage(source));

    private static bool IsReviewableOfflineEvidence(DeliveryDeviceEvent source) =>
        source.EventType == DeliveryDeviceEventType.InternetRecovered
        && OfflineDurationSeconds(source) >= ReviewOfflineDurationSeconds;

    private static bool IsReviewInterruptionEvent(DeliveryDeviceEvent source) => source.EventType switch
    {
        DeliveryDeviceEventType.AirplaneModeEnabled
            or DeliveryDeviceEventType.WifiDisabled
            or DeliveryDeviceEventType.DeviceRestarted
            or DeliveryDeviceEventType.AppStopped
            or DeliveryDeviceEventType.LocationServiceRestarted => true,
        DeliveryDeviceEventType.InternetRecovered => IsReviewableOfflineEvidence(source),
        _ => false,
    };

    private static bool IsDirectInterruptionEvent(DeliveryDeviceEventType eventType) => eventType is
        DeliveryDeviceEventType.AirplaneModeEnabled
        or DeliveryDeviceEventType.WifiDisabled
        or DeliveryDeviceEventType.DeviceRestarted
        or DeliveryDeviceEventType.AppStopped
        or DeliveryDeviceEventType.LocationServiceRestarted;

    private static bool MatchesInterruptionEvent(
        DeliveryTrackingAlert alert,
        DeliveryDeviceEvent deviceEvent) =>
        alert.OccurredAt <= deviceEvent.RecordedAt
        && (alert.DeduplicationKey.StartsWith("session:", StringComparison.Ordinal)
            ? !alert.RecoveredAt.HasValue
                || deviceEvent.RecordedAt <= alert.RecoveredAt.Value.AddMinutes(1)
            : alert.LastOccurredAt >= deviceEvent.RecordedAt.AddMinutes(-1)
                && alert.LastOccurredAt <= deviceEvent.RecordedAt.AddMinutes(1));

    private static int DirectInterruptionPriority(DeliveryDeviceEventType eventType) => eventType switch
    {
        DeliveryDeviceEventType.AirplaneModeEnabled => 5,
        DeliveryDeviceEventType.AppStopped => 4,
        DeliveryDeviceEventType.LocationServiceRestarted => 3,
        DeliveryDeviceEventType.WifiDisabled => 2,
        DeliveryDeviceEventType.DeviceRestarted => 1,
        _ => 0,
    };

    private static string BuildDirectInterruptionMessage(DeliveryDeviceEvent source) => source.EventType switch
    {
        DeliveryDeviceEventType.AirplaneModeEnabled => "El dispositivo confirmó la activación del modo avión.",
        DeliveryDeviceEventType.WifiDisabled => "El dispositivo confirmó que el Wi-Fi fue desactivado durante una interrupción de conectividad.",
        DeliveryDeviceEventType.DeviceRestarted => "El dispositivo confirmó un reinicio durante la jornada.",
        DeliveryDeviceEventType.AppStopped => "El dispositivo confirmó que la app o el servicio de seguimiento fue detenido.",
        DeliveryDeviceEventType.LocationServiceRestarted => "El dispositivo confirmó que el servicio de seguimiento tuvo que reiniciarse.",
        DeliveryDeviceEventType.InternetRecovered =>
            $"Se sincronizaron {source.OfflineLocationCount ?? ParseQueuedLocationCount(source.Details)} ubicaciones offline.",
        _ => "La interrupción requiere revisión.",
    };

    private static int OfflineDurationSeconds(DeliveryDeviceEvent source) =>
        source.OfflineStartedAt.HasValue && source.OfflineEndedAt.HasValue
            ? Math.Max(0, (int)Math.Round((source.OfflineEndedAt.Value - source.OfflineStartedAt.Value).TotalSeconds))
            : 0;

    private async Task ApplyVerifiedOfflineEvidenceAsync(
        DeliveryDeviceEvent source,
        CancellationToken cancellationToken)
    {
        var receivedFrom = source.SyncedAt.AddMinutes(-30);
        var receivedTo = source.SyncedAt.AddMinutes(1);
        var verified = await _db.DeliverymanLocations.AsNoTracking()
            .Where(x => x.WorkSessionId == source.WorkSessionId
                && x.InternetAvailable == false
                && x.SyncedAt >= receivedFrom
                && x.SyncedAt <= receivedTo
                && x.RecordedAt <= source.RecordedAt)
            .OrderBy(x => x.RecordedAt)
            .ThenBy(x => x.Id)
            .Select(x => x.RecordedAt)
            .ToListAsync(cancellationToken);
        source.OfflineLocationCount = verified.Count;
        source.OfflineStartedAt = verified.FirstOrDefault();
        source.OfflineEndedAt = verified.LastOrDefault();
        if (verified.Count == 0)
        {
            source.OfflineStartedAt = null;
            source.OfflineEndedAt = null;
        }
    }

    internal static int ParseQueuedLocationCount(string? details)
    {
        const string prefix = "queued_location_count=";
        var part = details?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return part is not null && int.TryParse(part[prefix.Length..], out var count) && count > 0 ? count : 0;
    }

    private static void Resolve(DeliveryTrackingAlert alert, DateTime resolvedAt, string reason)
    {
        alert.Status = DeliveryTrackingAlertStatus.Resolved;
        alert.ResolvedAt = resolvedAt;
        alert.ResolutionReason = reason;
        alert.UpdatedAt = resolvedAt;
    }
}

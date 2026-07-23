using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public class DeliveryTrackingAlertService : IDeliveryTrackingAlertService
{
    private const int BatchSize = 200;
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public DeliveryTrackingAlertService(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> ProcessAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        var changes = 0;
        changes += await ProcessDeviceEventsAsync(nowUtc, cancellationToken);
        changes += await ProcessUnexpectedStaysAsync(nowUtc, cancellationToken);
        changes += await ProcessActiveSessionsAsync(nowUtc, cancellationToken);
        if (changes > 0)
            await _db.SaveChangesAsync(cancellationToken);
        return changes;
    }

    private async Task<int> ProcessDeviceEventsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var negativeTypes = new[]
        {
            DeliveryDeviceEventType.GpsDisabled,
            DeliveryDeviceEventType.LocationPermissionRevoked,
        };
        var events = await _db.DeliveryDeviceEvents.AsNoTracking()
            .Where(deviceEvent => (negativeTypes.Contains(deviceEvent.EventType)
                    || (deviceEvent.EventType == DeliveryDeviceEventType.InternetRecovered
                        && deviceEvent.Details != null
                        && deviceEvent.Details.Contains("queued_location_count=")))
                && !_db.DeliveryTrackingAlerts.Any(alert => alert.SourceDeviceEventId == deviceEvent.Id))
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
            _db.DeliveryTrackingAlerts.Add(alert);
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

    private async Task<int> ProcessUnexpectedStaysAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var incidents = await _db.DeliveryTrackingIncidents.AsNoTracking()
            .Where(incident => incident.StayClassification == DeliveryStayClassification.UnexpectedPlace
                && !_db.DeliveryTrackingAlerts.Any(alert => alert.IncidentId == incident.Id))
            .OrderBy(x => x.StartedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        var changes = 0;
        foreach (var incident in incidents)
        {
            _db.DeliveryTrackingAlerts.Add(new DeliveryTrackingAlert
            {
                BranchId = incident.BranchId,
                DeliverymanId = incident.DeliverymanId,
                WorkSessionId = incident.WorkSessionId,
                IncidentId = incident.Id,
                DeduplicationKey = $"incident:{incident.Id}:unexpected_stay",
                AlertType = DeliveryTrackingAlertType.UnexpectedStay,
                Severity = DeliveryTrackingAlertSeverity.RequiresReview,
                Status = DeliveryTrackingAlertStatus.Active,
                Title = "Permanencia en lugar no esperado",
                Message = $"Permaneció {FormatDuration(incident.DurationSeconds)} en " +
                    $"{incident.CenterLatitude.ToString("0.000000", CultureInfo.InvariantCulture)}, " +
                    $"{incident.CenterLongitude.ToString("0.000000", CultureInfo.InvariantCulture)} " +
                    "fuera de lugares operativos conocidos.",
                OccurredAt = incident.StartedAt,
                LastOccurredAt = incident.EndedAt,
                DurationSeconds = incident.DurationSeconds,
                StartLatitude = incident.CenterLatitude,
                StartLongitude = incident.CenterLongitude,
                StartLocationRecordedAt = incident.StartedAt,
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
                    || x.AlertType == DeliveryTrackingAlertType.SessionPastAutoClose))
            .ToListAsync(cancellationToken);
        var keys = relevantAlerts.Select(x => x.DeduplicationKey).ToHashSet();
        var changes = 0;

        foreach (var session in sessions)
        {
            if (!branches.TryGetValue(session.BranchId, out var branch))
                continue;
            var intervalSeconds = lastModes.GetValueOrDefault(session.Id) == DeliveryTrackingMode.ActiveDelivery
                ? branch.DeliveryTrackingActiveIntervalSeconds
                : branch.DeliveryTrackingLightIntervalSeconds;
            var thresholdSeconds = Math.Max(1, intervalSeconds) * 2;
            var silenceSeconds = (nowUtc - session.LastCommunicationAt).TotalSeconds;
            if (silenceSeconds >= thresholdSeconds)
            {
                var key = $"session:{session.Id}:no_communication:{session.LastCommunicationAt.Ticks}";
                if (!keys.Contains(key))
                {
                    _db.DeliveryTrackingAlerts.Add(new DeliveryTrackingAlert
                    {
                        BranchId = session.BranchId,
                        DeliverymanId = session.DeliverymanId,
                        WorkSessionId = session.Id,
                        DeduplicationKey = key,
                        AlertType = DeliveryTrackingAlertType.NoCommunication,
                        Severity = DeliveryTrackingAlertSeverity.Warning,
                        Status = DeliveryTrackingAlertStatus.Active,
                        Title = "Domiciliario sin comunicación",
                        Message = $"No se reciben datos desde hace {Math.Max(1, (int)silenceSeconds / 60)} minutos; se esperaban reportes cada {intervalSeconds} segundos.",
                        OccurredAt = session.LastCommunicationAt.AddSeconds(thresholdSeconds),
                        LastOccurredAt = nowUtc,
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc,
                    });
                    keys.Add(key);
                    changes++;
                }
            }

            if (nowUtc >= session.AutoCloseAt)
            {
                var key = $"session:{session.Id}:past_auto_close";
                if (!keys.Contains(key))
                {
                    _db.DeliveryTrackingAlerts.Add(new DeliveryTrackingAlert
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
                Resolve(alert, nowUtc, "La jornada laboral finalizó.");
                changes++;
                continue;
            }
            if (alert.AlertType == DeliveryTrackingAlertType.NoCommunication
                && session.LastCommunicationAt > alert.OccurredAt)
            {
                Resolve(alert, session.LastCommunicationAt, "La comunicación se recuperó.");
                changes++;
            }
        }
        return changes;
    }

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
        var count = ParseQueuedLocationCount(source.Details);
        if (count <= 0)
            return null;
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

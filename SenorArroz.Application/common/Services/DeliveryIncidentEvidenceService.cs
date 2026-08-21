using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public class DeliveryIncidentEvidenceService : IDeliveryIncidentEvidenceService
{
    private const int MarginPointCount = 2;
    private const int BatchSize = 100;
    private static readonly DeliveryStayClassification[] RelevantClassifications =
    [
        DeliveryStayClassification.PendingReview,
        DeliveryStayClassification.UnexpectedPlace,
        DeliveryStayClassification.GpsUnreliable,
    ];

    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public DeliveryIncidentEvidenceService(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> ProcessPendingStaysAsync(CancellationToken cancellationToken = default)
    {
        var stays = await _db.DeliveryStays.AsNoTracking()
            .Where(stay => stay.ClassifiedAt.HasValue
                && RelevantClassifications.Contains(stay.Classification)
                && !_db.DeliveryTrackingIncidents.Any(incident =>
                    incident.DeliveryStayId == stay.Id
                    && incident.SourceUpdatedAt >= stay.UpdatedAt
                    && incident.EvidenceComplete))
            .OrderBy(stay => stay.StartedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (stays.Count == 0)
            return 0;

        var sessionIds = stays.Select(x => x.WorkSessionId).Distinct().ToList();
        var sessions = await _db.DeliveryWorkSessions.AsNoTracking()
            .Where(x => sessionIds.Contains(x.Id))
            .Select(x => new SessionSnapshot(x.Id, x.BranchId, x.Status))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var locationsBySession = (await _db.DeliverymanLocations.AsNoTracking()
                .Where(x => x.WorkSessionId.HasValue && sessionIds.Contains(x.WorkSessionId.Value))
                .OrderBy(x => x.RecordedAt)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.WorkSessionId!.Value)
            .ToDictionary(x => x.Key, x => x.ToList());
        var eventsBySession = (await _db.DeliveryDeviceEvents.AsNoTracking()
                .Where(x => sessionIds.Contains(x.WorkSessionId))
                .OrderBy(x => x.RecordedAt)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.WorkSessionId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var stayIds = stays.Select(x => x.Id).ToList();
        var incidents = await _db.DeliveryTrackingIncidents
            .Where(x => x.DeliveryStayId.HasValue && stayIds.Contains(x.DeliveryStayId.Value))
            .ToDictionaryAsync(x => x.DeliveryStayId!.Value, cancellationToken);
        var existingIncidentIds = incidents.Values.Select(x => x.Id).ToList();
        var oldLocationEvidence = existingIncidentIds.Count == 0
            ? []
            : await _db.DeliveryIncidentLocationEvidence
                .Where(x => existingIncidentIds.Contains(x.IncidentId))
                .ToListAsync(cancellationToken);
        var oldEventEvidence = existingIncidentIds.Count == 0
            ? []
            : await _db.DeliveryIncidentDeviceEventEvidence
                .Where(x => existingIncidentIds.Contains(x.IncidentId))
                .ToListAsync(cancellationToken);

        var orderIds = stays.Where(x => x.NearestOrderId.HasValue)
            .Select(x => x.NearestOrderId!.Value).Distinct().ToList();
        var orderSnapshots = await _db.Orders.AsNoTracking()
            .Where(x => orderIds.Contains(x.Id))
            .Select(x => new OrderSnapshot(
                x.Id,
                x.Status,
                x.Address == null ? null : x.Address.AddressText,
                x.Address == null ? null : x.Address.Latitude,
                x.Address == null ? null : x.Address.Longitude))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        var processed = 0;
        foreach (var stay in stays)
        {
            if (!sessions.TryGetValue(stay.WorkSessionId, out var session)
                || !locationsBySession.TryGetValue(stay.WorkSessionId, out var sessionLocations))
                continue;

            var firstIndex = sessionLocations.FindIndex(x => x.Id == stay.FirstLocationId);
            var lastIndex = sessionLocations.FindIndex(x => x.Id == stay.LastLocationId);
            if (firstIndex < 0 || lastIndex < firstIndex)
                continue;

            var evidenceStartIndex = Math.Max(0, firstIndex - MarginPointCount);
            var evidenceEndIndex = Math.Min(sessionLocations.Count - 1, lastIndex + MarginPointCount);
            var evidenceLocations = sessionLocations.GetRange(
                evidenceStartIndex,
                evidenceEndIndex - evidenceStartIndex + 1);
            var followingPointCount = evidenceEndIndex - lastIndex;

            if (!incidents.TryGetValue(stay.Id, out var incident))
            {
                incident = new DeliveryTrackingIncident
                {
                    IncidentType = DeliveryTrackingIncidentType.Stay,
                    DeliveryStayId = stay.Id,
                    CreatedAt = nowUtc,
                };
                _db.DeliveryTrackingIncidents.Add(incident);
                incidents[stay.Id] = incident;
            }
            else
            {
                _db.DeliveryIncidentLocationEvidence.RemoveRange(
                    oldLocationEvidence.Where(x => x.IncidentId == incident.Id));
                _db.DeliveryIncidentDeviceEventEvidence.RemoveRange(
                    oldEventEvidence.Where(x => x.IncidentId == incident.Id));
            }

            orderSnapshots.TryGetValue(stay.NearestOrderId ?? 0, out var order);
            CopyStaySnapshot(incident, stay, session.BranchId, order, nowUtc);
            incident.EvidenceComplete = followingPointCount >= MarginPointCount
                || session.Status == DeliveryWorkSessionStatus.Closed;

            for (var index = evidenceStartIndex; index <= evidenceEndIndex; index++)
            {
                var location = sessionLocations[index];
                incident.LocationEvidence.Add(CopyLocation(
                    location,
                    index >= firstIndex && index <= lastIndex));
            }

            var periodStart = evidenceLocations[0].RecordedAt;
            var periodEnd = evidenceLocations[^1].RecordedAt;
            foreach (var deviceEvent in eventsBySession.GetValueOrDefault(stay.WorkSessionId, [])
                         .Where(x => x.RecordedAt >= periodStart && x.RecordedAt <= periodEnd))
            {
                incident.DeviceEventEvidence.Add(CopyDeviceEvent(deviceEvent));
            }

            processed++;
        }

        if (processed > 0)
            await _db.SaveChangesAsync(cancellationToken);
        return processed;
    }

    private static void CopyStaySnapshot(
        DeliveryTrackingIncident incident,
        DeliveryStay stay,
        int branchId,
        OrderSnapshot? order,
        DateTime nowUtc)
    {
        incident.BranchId = branchId;
        incident.DeliverymanId = stay.DeliverymanId;
        incident.WorkSessionId = stay.WorkSessionId;
        incident.DeliveryRouteId = stay.DeliveryRouteId;
        incident.OrderId = stay.NearestOrderId;
        incident.StayClassification = stay.Classification;
        incident.ClassificationReason = stay.ClassificationReason;
        incident.StartedAt = stay.StartedAt;
        incident.EndedAt = stay.EndedAt;
        incident.DurationSeconds = stay.DurationSeconds;
        incident.CenterLatitude = stay.CenterLatitude;
        incident.CenterLongitude = stay.CenterLongitude;
        incident.RadiusMeters = stay.RadiusMeters;
        incident.AverageAccuracyMeters = stay.AverageAccuracyMeters;
        incident.DistanceToBranchMeters = stay.DistanceToBranchMeters;
        incident.DistanceToOrderMeters = stay.DistanceToNearestOrderMeters;
        incident.OrderAddressSnapshot = order?.Address;
        incident.OrderLatitudeSnapshot = order?.Latitude;
        incident.OrderLongitudeSnapshot = order?.Longitude;
        incident.OrderStatusSnapshot = order?.Status.ToString();
        incident.SourceUpdatedAt = stay.UpdatedAt;
        incident.EvidenceCapturedAt = nowUtc;
        incident.UpdatedAt = nowUtc;
    }

    private static DeliveryIncidentLocationEvidence CopyLocation(
        DeliverymanLocation source,
        bool isCorePoint) => new()
    {
        SourceLocationId = source.Id,
        ClientPointId = source.ClientPointId,
        IsCorePoint = isCorePoint,
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

    private static DeliveryIncidentDeviceEventEvidence CopyDeviceEvent(DeliveryDeviceEvent source) => new()
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

    private sealed record SessionSnapshot(int Id, int BranchId, DeliveryWorkSessionStatus Status);
    private sealed record OrderSnapshot(
        int Id,
        OrderStatus Status,
        string? Address,
        decimal? Latitude,
        decimal? Longitude);
}

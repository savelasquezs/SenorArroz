using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Services;

public sealed record DeliveryStayPoint(
    long Id,
    decimal Latitude,
    decimal Longitude,
    double? AccuracyMeters,
    bool? GpsEnabled,
    int? DeliveryRouteId,
    DateTime RecordedAt);

public sealed record DetectedDeliveryStay(
    long FirstLocationId,
    long LastLocationId,
    int? DeliveryRouteId,
    DateTime StartedAt,
    DateTime EndedAt,
    int DurationSeconds,
    decimal CenterLatitude,
    decimal CenterLongitude,
    double RadiusMeters,
    double AverageAccuracyMeters,
    int PointCount);

public class DeliveryStayDetectionService : IDeliveryStayDetectionService
{
    public const int MinimumPointCount = 3;
    public const double MaximumAcceptedAccuracyMeters = 50;
    private const int PendingSessionBatchSize = 50;

    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public DeliveryStayDetectionService(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> ProcessPendingSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessionIds = await _db.DeliveryWorkSessions.AsNoTracking()
            .Where(session => _db.DeliverymanLocations.Any(location =>
                location.WorkSessionId == session.Id
                && (!session.StayAnalysisLastLocationId.HasValue
                    || location.Id > session.StayAnalysisLastLocationId.Value)))
            .OrderBy(session => session.StayAnalysisLastLocationId ?? 0)
            .ThenBy(session => session.Id)
            .Select(session => session.Id)
            .Take(PendingSessionBatchSize)
            .ToListAsync(cancellationToken);

        foreach (var sessionId in sessionIds)
            await ProcessSessionAsync(sessionId, cancellationToken);

        return sessionIds.Count;
    }

    public async Task<int> ProcessSessionAsync(
        int workSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _db.DeliveryWorkSessions
            .FirstOrDefaultAsync(x => x.Id == workSessionId, cancellationToken);
        if (session is null)
            return 0;

        var branch = await _db.Branches.AsNoTracking()
            .Where(x => x.Id == session.BranchId)
            .Select(x => new
            {
                x.Latitude,
                x.Longitude,
                x.DeliveryTrackingStayThresholdMinutes,
                x.DeliveryTrackingStayRadiusMeters,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (branch is null)
            return 0;

        var points = await _db.DeliverymanLocations.AsNoTracking()
            .Where(x => x.WorkSessionId == workSessionId)
            .OrderBy(x => x.RecordedAt)
            .ThenBy(x => x.Id)
            .Select(x => new DeliveryStayPoint(
                x.Id,
                x.Latitude,
                x.Longitude,
                x.AccuracyMeters,
                x.GpsEnabled,
                x.DeliveryRouteId,
                x.RecordedAt))
            .ToListAsync(cancellationToken);
        if (points.Count == 0)
            return 0;

        var detected = Detect(
            points,
            branch.DeliveryTrackingStayThresholdMinutes,
            branch.DeliveryTrackingStayRadiusMeters);
        var routeIds = detected
            .Where(x => x.DeliveryRouteId.HasValue)
            .Select(x => x.DeliveryRouteId!.Value)
            .Distinct()
            .ToList();
        var destinations = routeIds.Count == 0
            ? []
            : await (
                from stop in _db.DeliveryRouteStops.AsNoTracking()
                join order in _db.Orders.AsNoTracking() on stop.OrderId equals order.Id
                join address in _db.Addresses.AsNoTracking() on order.AddressId equals address.Id
                where routeIds.Contains(stop.DeliveryRouteId)
                      && address.Latitude.HasValue
                      && address.Longitude.HasValue
                select new RouteDestination(
                    stop.DeliveryRouteId,
                    order.Id,
                    address.Latitude!.Value,
                    address.Longitude!.Value))
                .ToListAsync(cancellationToken);
        var destinationsByRoute = destinations
            .GroupBy(x => x.RouteId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var existing = await _db.DeliveryStays
            .Where(x => x.WorkSessionId == workSessionId)
            .OrderBy(x => x.StartedAt)
            .ToListAsync(cancellationToken);
        var matchedIds = new HashSet<long>();
        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);

        foreach (var candidate in detected)
        {
            var stay = existing.FirstOrDefault(x =>
                           x.FirstLocationId == candidate.FirstLocationId)
                       ?? existing
                           .Where(x => !matchedIds.Contains(x.Id)
                                       && x.DeliveryRouteId == candidate.DeliveryRouteId
                                       && x.StartedAt <= candidate.EndedAt
                                       && x.EndedAt >= candidate.StartedAt)
                           .OrderBy(x => Math.Abs((x.StartedAt - candidate.StartedAt).TotalSeconds))
                           .FirstOrDefault();
            if (stay is null)
            {
                stay = new DeliveryStay
                {
                    DeliverymanId = session.DeliverymanId,
                    WorkSessionId = session.Id,
                    CreatedAt = nowUtc,
                };
                _db.DeliveryStays.Add(stay);
            }

            ApplyCandidate(stay, candidate, branch.Latitude, branch.Longitude, destinationsByRoute, nowUtc);
            if (stay.Id != 0)
                matchedIds.Add(stay.Id);
        }

        session.StayAnalysisLastLocationId = points.Max(x => x.Id);
        await _db.SaveChangesAsync(cancellationToken);
        return detected.Count;
    }

    public static IReadOnlyList<DetectedDeliveryStay> Detect(
        IReadOnlyCollection<DeliveryStayPoint> source,
        int thresholdMinutes,
        int radiusMeters)
    {
        if (thresholdMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(thresholdMinutes));
        if (radiusMeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(radiusMeters));

        var result = new List<DetectedDeliveryStay>();
        var candidate = new List<DeliveryStayPoint>();
        foreach (var point in source.OrderBy(x => x.RecordedAt).ThenBy(x => x.Id))
        {
            if (!IsReliable(point))
            {
                TryAddCandidate(result, candidate, thresholdMinutes, radiusMeters);
                candidate.Clear();
                continue;
            }

            if (candidate.Count == 0)
            {
                candidate.Add(point);
                continue;
            }

            if (candidate[0].DeliveryRouteId == point.DeliveryRouteId
                && FitsRadius(candidate, point, radiusMeters))
            {
                candidate.Add(point);
                continue;
            }

            TryAddCandidate(result, candidate, thresholdMinutes, radiusMeters);
            candidate.Clear();
            candidate.Add(point);
        }

        TryAddCandidate(result, candidate, thresholdMinutes, radiusMeters);
        return result;
    }

    private static bool IsReliable(DeliveryStayPoint point) =>
        point.GpsEnabled != false
        && point.AccuracyMeters.HasValue
        && point.AccuracyMeters.Value >= 0
        && point.AccuracyMeters.Value <= MaximumAcceptedAccuracyMeters;

    private static bool FitsRadius(
        IReadOnlyCollection<DeliveryStayPoint> current,
        DeliveryStayPoint next,
        int radiusMeters)
    {
        var latitude = (current.Sum(x => (double)x.Latitude) + (double)next.Latitude) / (current.Count + 1);
        var longitude = (current.Sum(x => (double)x.Longitude) + (double)next.Longitude) / (current.Count + 1);
        return current.Append(next).All(point =>
            GeoHelper.HaversineDistanceMeters(
                latitude,
                longitude,
                (double)point.Latitude,
                (double)point.Longitude) <= radiusMeters);
    }

    private static void TryAddCandidate(
        ICollection<DetectedDeliveryStay> result,
        IReadOnlyCollection<DeliveryStayPoint> candidate,
        int thresholdMinutes,
        int radiusMeters)
    {
        if (candidate.Count < MinimumPointCount)
            return;
        var ordered = candidate.OrderBy(x => x.RecordedAt).ThenBy(x => x.Id).ToList();
        var duration = ordered[^1].RecordedAt - ordered[0].RecordedAt;
        if (duration < TimeSpan.FromMinutes(thresholdMinutes))
            return;

        var centerLatitude = ordered.Average(x => (double)x.Latitude);
        var centerLongitude = ordered.Average(x => (double)x.Longitude);
        var coveredRadius = ordered.Max(point => GeoHelper.HaversineDistanceMeters(
            centerLatitude,
            centerLongitude,
            (double)point.Latitude,
            (double)point.Longitude));
        if (coveredRadius > radiusMeters)
            return;

        result.Add(new DetectedDeliveryStay(
            ordered[0].Id,
            ordered[^1].Id,
            ordered[0].DeliveryRouteId,
            ordered[0].RecordedAt,
            ordered[^1].RecordedAt,
            checked((int)Math.Round(duration.TotalSeconds)),
            (decimal)centerLatitude,
            (decimal)centerLongitude,
            coveredRadius,
            ordered.Average(x => x.AccuracyMeters!.Value),
            ordered.Count));
    }

    private static void ApplyCandidate(
        DeliveryStay stay,
        DetectedDeliveryStay candidate,
        decimal? branchLatitude,
        decimal? branchLongitude,
        IReadOnlyDictionary<int, List<RouteDestination>> destinationsByRoute,
        DateTime nowUtc)
    {
        stay.DeliveryRouteId = candidate.DeliveryRouteId;
        stay.FirstLocationId = candidate.FirstLocationId;
        stay.LastLocationId = candidate.LastLocationId;
        stay.StartedAt = candidate.StartedAt;
        stay.EndedAt = candidate.EndedAt;
        stay.DurationSeconds = candidate.DurationSeconds;
        stay.CenterLatitude = candidate.CenterLatitude;
        stay.CenterLongitude = candidate.CenterLongitude;
        stay.RadiusMeters = candidate.RadiusMeters;
        stay.AverageAccuracyMeters = candidate.AverageAccuracyMeters;
        stay.PointCount = candidate.PointCount;
        stay.UpdatedAt = nowUtc;
        stay.InvalidateClassification();
        stay.DistanceToBranchMeters = branchLatitude.HasValue && branchLongitude.HasValue
            ? GeoHelper.HaversineDistanceMeters(
                (double)candidate.CenterLatitude,
                (double)candidate.CenterLongitude,
                (double)branchLatitude.Value,
                (double)branchLongitude.Value)
            : null;

        var nearest = candidate.DeliveryRouteId.HasValue
                      && destinationsByRoute.TryGetValue(candidate.DeliveryRouteId.Value, out var routeDestinations)
            ? routeDestinations
                .Select(destination => new
                {
                    Destination = destination,
                    Distance = GeoHelper.HaversineDistanceMeters(
                        (double)candidate.CenterLatitude,
                        (double)candidate.CenterLongitude,
                        (double)destination.Latitude,
                        (double)destination.Longitude),
                })
                .OrderBy(x => x.Distance)
                .FirstOrDefault()
            : null;
        stay.NearestOrderId = nearest?.Destination.OrderId;
        stay.DistanceToNearestOrderMeters = nearest?.Distance;
    }

    private sealed record RouteDestination(int RouteId, int OrderId, decimal Latitude, decimal Longitude);
}

using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public sealed record DeliveryStayPoint(
    long Id,
    decimal Latitude,
    decimal Longitude,
    double? AccuracyMeters,
    bool? GpsEnabled,
    int? DeliveryRouteId,
    DateTime RecordedAt,
    int ExpectedIntervalSeconds = 300);

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
                x.DeliveryTrackingActiveIntervalSeconds,
                x.DeliveryTrackingLightIntervalSeconds,
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
                x.RecordedAt,
                x.TrackingMode == DeliveryTrackingMode.ActiveDelivery
                    ? branch.DeliveryTrackingActiveIntervalSeconds
                    : branch.DeliveryTrackingLightIntervalSeconds))
            .ToListAsync(cancellationToken);
        if (points.Count == 0)
            return 0;

        var detected = Detect(
            points,
            DeliveryTrackingEvidencePolicy.StayMinutes,
            DeliveryTrackingEvidencePolicy.StayRadiusMeters);
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
                           !matchedIds.Contains(x.Id) && x.FirstLocationId == candidate.FirstLocationId)
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

        // Late coordinates can disprove a previously inferred stay. Preserve its
        // identity and all administrative reviews, but retract the inference.
        foreach (var obsolete in existing.Where(x => !matchedIds.Contains(x.Id)))
        {
            if (obsolete.ClassificationReason == DeliveryTrackingEvidencePolicy.RetractedStay) continue;
            obsolete.Classification = DeliveryStayClassification.GpsUnreliable;
            obsolete.ClassificationReason = DeliveryTrackingEvidencePolicy.RetractedStay;
            obsolete.ClassifiedAt = nowUtc;
            obsolete.UpdatedAt = nowUtc;
        }

        session.StayAnalysisLastLocationId = points.Max(x => x.Id);
        await _db.SaveChangesAsync(cancellationToken);
        return detected.Count;
    }

    /// Both tracking cadences remain eligible. Cadence bounds missing evidence;
    /// it is NOT an active-route or active-delivery eligibility gate.
    public static IReadOnlyList<DetectedDeliveryStay> Detect(
        IReadOnlyCollection<DeliveryStayPoint> source, int thresholdMinutes, int radiusMeters)
    {
        if (thresholdMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(thresholdMinutes));
        if (radiusMeters <= 0) throw new ArgumentOutOfRangeException(nameof(radiusMeters));
        var points = source.OrderBy(x => x.RecordedAt).ThenBy(x => x.Id)
            .GroupBy(x => x.RecordedAt).Select(x => x.OrderBy(p => p.AccuracyMeters ?? double.MaxValue).First())
            .ToList();
        var result = new List<DetectedDeliveryStay>();
        var segmentStart = 0;
        var windowStart = 0;
        var threshold = TimeSpan.FromMinutes(thresholdMinutes);
        for (var end = 0; end < points.Count; end++)
        {
            var point = points[end];
            if (point.GpsEnabled == false)
            {
                segmentStart = windowStart = end + 1;
                continue;
            }
            if (end > segmentStart)
            {
                var previous = points[end - 1];
                // The cadence of the preceding capture defines when the next was
                // expected; a mode change cannot retroactively excuse a data gap.
                var maxGap = TimeSpan.FromSeconds(Math.Max(1, previous.ExpectedIntervalSeconds) * 1.5);
                if (point.DeliveryRouteId != previous.DeliveryRouteId
                    || point.RecordedAt - previous.RecordedAt > maxGap)
                    segmentStart = windowStart = end;
            }
            if (windowStart > end) continue;
            while (windowStart < end && point.RecordedAt - points[windowStart + 1].RecordedAt >= threshold)
                windowStart++;
            if (point.RecordedAt - points[windowStart].RecordedAt < threshold) continue;
            var sample = points.GetRange(windowStart, end - windowStart + 1);
            var stay = Evaluate(sample, threshold, radiusMeters);
            if (stay is null) continue;
            if (result.Count > 0 && result[^1].DeliveryRouteId == stay.DeliveryRouteId
                && result[^1].EndedAt >= stay.StartedAt)
            {
                var previous = result[^1];
                var combined = points.Skip(segmentStart).Take(end - segmentStart + 1)
                    .Where(x => x.RecordedAt >= previous.StartedAt).ToList();
                var extended = Evaluate(combined, threshold, radiusMeters);
                if (extended != null)
                    result[^1] = extended;
                // Do not create overlapping accusations if a different cluster
                // begins inside a previous episode. Require a new supported window.
                continue;
            }
            result.Add(stay);
        }
        return result;
    }

    private static DetectedDeliveryStay? Evaluate(
        IReadOnlyList<DeliveryStayPoint> sample, TimeSpan threshold, double radius)
    {
        var valid = sample.Where(p => p.GpsEnabled != false
            && p.Latitude is >= -90 and <= 90 && p.Longitude is >= -180 and <= 180
            && p.AccuracyMeters.HasValue && double.IsFinite(p.AccuracyMeters.Value)
            && p.AccuracyMeters.Value >= 0
            && p.AccuracyMeters.Value <= Math.Min(radius, MaximumAcceptedAccuracyMeters)).ToList();
        // Neutral readings do not vote against the place, but cannot manufacture
        // 90% confidence from only a handful of good readings in a noisy interval.
        if (valid.Count < MinimumPointCount
            || valid.Count + 1e-9 < sample.Count * DeliveryTrackingEvidencePolicy.MinimumAgreement)
            return null;
        static double Median(IEnumerable<double> values)
        {
            var a = values.OrderBy(x => x).ToArray();
            return (a[(a.Length - 1) / 2] + a[a.Length / 2]) / 2;
        }
        var lat = Median(valid.Select(x => (double)x.Latitude));
        var lon = Median(valid.Select(x => (double)x.Longitude));
        double Distance(DeliveryStayPoint p) => GeoHelper.HaversineDistanceMeters(
            lat, lon, (double)p.Latitude, (double)p.Longitude);
        var inliers = valid.Where(p => Distance(p) <= radius).ToList();
        if (inliers.Count < MinimumPointCount
            || inliers.Count + 1e-9 < valid.Count * DeliveryTrackingEvidencePolicy.MinimumAgreement)
            return null;
        var first = inliers[0];
        var last = inliers[^1];
        if (last.RecordedAt - first.RecordedAt < threshold) return null;
        // Agreement is an observational proportion, not a probability of misconduct.
        // Preserve original points and timestamps in the evidence snapshot.
        return new DetectedDeliveryStay(first.Id, last.Id, first.DeliveryRouteId,
            first.RecordedAt, last.RecordedAt,
            checked((int)(last.RecordedAt - first.RecordedAt).TotalSeconds),
            (decimal)lat, (decimal)lon, inliers.Max(Distance),
            inliers.Average(x => x.AccuracyMeters!.Value), valid.Count);
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

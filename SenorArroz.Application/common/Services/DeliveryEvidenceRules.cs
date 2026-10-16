using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Services;

/// <summary>Shared GPS evidence rules for live delivery and chronological recovery.</summary>
internal static class DeliveryEvidenceRules
{
    internal const double MaximumAccuracyMeters = 50;
    internal static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan MaximumSampleGap = TimeSpan.FromSeconds(90);

    internal static bool ValidCoordinates(decimal latitude, decimal longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180
        && !(latitude == 0 && longitude == 0);

    internal static bool Reliable(DeliverymanLocation point) =>
        point.GpsEnabled != false && point.AccuracyMeters is >= 0 and <= MaximumAccuracyMeters
        && double.IsFinite(point.AccuracyMeters.Value)
        && ValidCoordinates(point.Latitude, point.Longitude);

    internal static bool Fresh(DeliverymanLocation point, DateTime now) =>
        Reliable(point) && point.RecordedAt >= now - MaximumAge
        && point.RecordedAt <= now.AddSeconds(15);

    internal static double Distance(DeliverymanLocation point, decimal latitude, decimal longitude) =>
        GeoHelper.HaversineDistanceMeters((double)point.Latitude, (double)point.Longitude,
            (double)latitude, (double)longitude);

    internal sealed record ReturnEvidence(long LocationId, DateTime EnteredAt, DateTime LastOutsideAt);

    internal static ReturnEvidence? FindReturn(
        IReadOnlyList<DeliverymanLocation> orderedPoints,
        decimal latitude, decimal longitude, int radius, DateTime now)
    {
        if (orderedPoints.Count == 0 || radius <= 0 || !ValidCoordinates(latitude, longitude)) return null;
        var latest = orderedPoints[^1];
        if (!Fresh(latest, now) || Distance(latest, latitude, longitude) > radius) return null;
        DateTime? outside = null, entered = null, previousInside = null;
        var insideCount = 0;
        foreach (var point in orderedPoints)
        {
            if (!Reliable(point))
            {
                entered = previousInside = null;
                insideCount = 0;
                continue;
            }
            var distance = Distance(point, latitude, longitude);
            if (distance > radius)
            {
                if (distance >= Math.Max(120, radius * 2)) outside = point.RecordedAt;
                entered = previousInside = null;
                insideCount = 0;
                continue;
            }
            if (!outside.HasValue) continue;
            if (previousInside.HasValue && point.RecordedAt <= previousInside.Value) continue;
            if (!previousInside.HasValue || point.RecordedAt - previousInside.Value > MaximumSampleGap)
            {
                entered = point.RecordedAt;
                insideCount = 0;
            }
            previousInside = point.RecordedAt;
            insideCount++;
        }
        return insideCount >= 2 && entered.HasValue && outside.HasValue
               && latest.RecordedAt - entered.Value >= TimeSpan.FromSeconds(30)
            ? new ReturnEvidence(latest.Id, entered.Value, outside.Value) : null;
    }

    internal sealed class Visit(DeliveryRouteStop stop, int orderId,
        decimal latitude, decimal longitude, DateTime eligibleAt)
    {
        internal DeliveryRouteStop Stop { get; } = stop;
        internal int OrderId { get; } = orderId;
        internal decimal Latitude { get; } = latitude;
        internal decimal Longitude { get; } = longitude;
        internal DateTime EligibleAt { get; } = eligibleAt;
        internal bool Finished { get; set; }
    }

    internal sealed record Departure(Visit Visit, DeliverymanLocation Point, double DistanceMeters);

    internal static Departure? Advance(IReadOnlyList<Visit> visits, DeliverymanLocation point,
        int arrivalRadius, int departureRadius, int minPresenceSeconds)
    {
        if (!Reliable(point)) return null;
        var eligible = visits.Where(v => !v.Finished && point.RecordedAt >= v.EligibleAt
            && ValidCoordinates(v.Latitude, v.Longitude)).ToList();
        var active = eligible.FirstOrDefault(v => v.Stop.ArrivalConfirmedAtUtc.HasValue)
            ?? eligible.FirstOrDefault(v => v.Stop.ArrivalCandidateAtUtc.HasValue);
        Departure? departure = null;
        if (active is not null)
        {
            var stop = active.Stop;
            if (stop.ArrivalLastSeenAtUtc is DateTime last && point.RecordedAt <= last) return null;
            var distance = Distance(point, active.Latitude, active.Longitude);
            if (distance <= arrivalRadius)
            {
                if (!stop.ArrivalConfirmedAtUtc.HasValue && stop.ArrivalLastSeenAtUtc.HasValue
                    && point.RecordedAt - stop.ArrivalLastSeenAtUtc.Value > MaximumSampleGap)
                    Reset(stop);
                ObserveArrival(stop, point.RecordedAt, distance, minPresenceSeconds);
                return null;
            }
            if (distance < departureRadius) return null;
            if (stop.ArrivalConfirmedAtUtc.HasValue)
            {
                active.Finished = true;
                departure = new Departure(active, point, distance);
            }
            else Reset(stop);
        }
        // A departure sample may simultaneously be the first arrival sample for the next stop.
        var next = eligible.FirstOrDefault(v => !v.Finished && v != active
            && Distance(point, v.Latitude, v.Longitude) <= arrivalRadius);
        if (next is not null)
            ObserveArrival(next.Stop, point.RecordedAt,
                Distance(point, next.Latitude, next.Longitude), minPresenceSeconds);
        return departure;
    }

    private static void ObserveArrival(DeliveryRouteStop stop, DateTime at, double distance, int minSeconds)
    {
        stop.ArrivalCandidateAtUtc ??= at;
        stop.ArrivalEvidenceCount++;
        stop.ArrivalLastSeenAtUtc = at;
        stop.ClosestDistanceMeters = Math.Min(stop.ClosestDistanceMeters ?? distance, distance);
        if (stop.ArrivalEvidenceCount >= 2 && at - stop.ArrivalCandidateAtUtc.Value >= TimeSpan.FromSeconds(minSeconds))
            stop.ArrivalConfirmedAtUtc ??= at;
    }

    private static void Reset(DeliveryRouteStop stop)
    {
        stop.ArrivalCandidateAtUtc = stop.ArrivalConfirmedAtUtc = stop.ArrivalLastSeenAtUtc = null;
        stop.ArrivalEvidenceCount = 0;
        stop.ClosestDistanceMeters = null;
    }
}

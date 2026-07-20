using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public class DeliveryStayClassificationService : IDeliveryStayClassificationService
{
    private const int PendingBatchSize = 100;
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public DeliveryStayClassificationService(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> ProcessPendingStaysAsync(CancellationToken cancellationToken = default)
    {
        var stays = await _db.DeliveryStays
            .Where(x => !x.ClassifiedAt.HasValue || x.ClassifiedAt < x.UpdatedAt)
            .OrderBy(x => x.StartedAt)
            .Take(PendingBatchSize)
            .ToListAsync(cancellationToken);
        if (stays.Count == 0)
            return 0;

        var sessionIds = stays.Select(x => x.WorkSessionId).Distinct().ToList();
        var sessionBranches = await _db.DeliveryWorkSessions.AsNoTracking()
            .Where(x => sessionIds.Contains(x.Id))
            .Select(x => new { x.Id, x.BranchId })
            .ToDictionaryAsync(x => x.Id, x => x.BranchId, cancellationToken);
        var branchIds = sessionBranches.Values.Distinct().ToList();
        var branches = await _db.Branches.AsNoTracking()
            .Where(x => branchIds.Contains(x.Id))
            .Select(x => new BranchRules(
                x.Id,
                x.Latitude,
                x.Longitude,
                x.DeliveryTrackingAllowedDistanceMeters))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var places = await _db.DeliveryAuthorizedPlaces.AsNoTracking()
            .Where(x => branchIds.Contains(x.BranchId) && x.Active)
            .Select(x => new AuthorizedPlace(
                x.Id,
                x.BranchId,
                x.Latitude,
                x.Longitude,
                x.RadiusMeters))
            .ToListAsync(cancellationToken);
        var placesByBranch = places
            .GroupBy(x => x.BranchId)
            .ToDictionary(x => x.Key, x => x.ToList());
        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);

        foreach (var stay in stays)
        {
            stay.AuthorizedPlaceId = null;
            stay.DistanceToAuthorizedPlaceMeters = null;
            if (!sessionBranches.TryGetValue(stay.WorkSessionId, out var branchId)
                || !branches.TryGetValue(branchId, out var branch))
            {
                SetClassification(stay, DeliveryStayClassification.PendingReview, "branch_context_missing", nowUtc);
                continue;
            }

            if (stay.AverageAccuracyMeters > DeliveryStayDetectionService.MaximumAcceptedAccuracyMeters)
            {
                SetClassification(stay, DeliveryStayClassification.GpsUnreliable, "average_accuracy_above_50m", nowUtc);
                continue;
            }

            if (branch.Latitude.HasValue && branch.Longitude.HasValue)
            {
                stay.DistanceToBranchMeters = GeoHelper.HaversineDistanceMeters(
                    (double)stay.CenterLatitude,
                    (double)stay.CenterLongitude,
                    (double)branch.Latitude.Value,
                    (double)branch.Longitude.Value);
            }
            if (stay.DistanceToBranchMeters <= branch.AllowedDistanceMeters)
            {
                SetClassification(stay, DeliveryStayClassification.Branch, "within_branch_tolerance", nowUtc);
                continue;
            }

            if (stay.NearestOrderId.HasValue
                && stay.DistanceToNearestOrderMeters <= branch.AllowedDistanceMeters)
            {
                SetClassification(
                    stay,
                    DeliveryStayClassification.OrderDestination,
                    "within_order_destination_tolerance",
                    nowUtc);
                continue;
            }

            var nearestAuthorizedPlace = placesByBranch.GetValueOrDefault(branchId)?
                .Select(place => new
                {
                    Place = place,
                    Distance = GeoHelper.HaversineDistanceMeters(
                        (double)stay.CenterLatitude,
                        (double)stay.CenterLongitude,
                        (double)place.Latitude,
                        (double)place.Longitude),
                })
                .OrderBy(x => x.Distance)
                .FirstOrDefault();
            if (nearestAuthorizedPlace is not null)
            {
                stay.DistanceToAuthorizedPlaceMeters = nearestAuthorizedPlace.Distance;
                if (nearestAuthorizedPlace.Distance <= nearestAuthorizedPlace.Place.RadiusMeters)
                {
                    stay.AuthorizedPlaceId = nearestAuthorizedPlace.Place.Id;
                    SetClassification(
                        stay,
                        DeliveryStayClassification.AuthorizedPlace,
                        "within_authorized_place_radius",
                        nowUtc);
                    continue;
                }
            }

            SetClassification(
                stay,
                stay.DeliveryRouteId.HasValue
                    ? DeliveryStayClassification.PendingReview
                    : DeliveryStayClassification.UnexpectedPlace,
                stay.DeliveryRouteId.HasValue
                    ? "route_context_requires_review"
                    : "outside_known_operational_places",
                nowUtc);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return stays.Count;
    }

    private static void SetClassification(
        Domain.Entities.DeliveryStay stay,
        DeliveryStayClassification classification,
        string reason,
        DateTime nowUtc)
    {
        stay.Classification = classification;
        stay.ClassificationReason = reason;
        stay.ClassifiedAt = nowUtc;
    }

    private sealed record BranchRules(
        int Id,
        decimal? Latitude,
        decimal? Longitude,
        int AllowedDistanceMeters);

    private sealed record AuthorizedPlace(
        int Id,
        int BranchId,
        decimal Latitude,
        decimal Longitude,
        int RadiusMeters);
}

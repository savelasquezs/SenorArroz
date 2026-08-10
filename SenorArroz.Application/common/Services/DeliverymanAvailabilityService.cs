using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public sealed class DeliverymanAvailabilityService : IDeliverymanAvailabilityService
{
    private readonly IApplicationDbContext _db;
    private readonly IGoogleRoutesDrivingMetricsService _googleRoutes;
    private readonly DeliveryRoutingOptions _options;

    public DeliverymanAvailabilityService(
        IApplicationDbContext db,
        IGoogleRoutesDrivingMetricsService googleRoutes,
        IOptions<DeliveryRoutingOptions> options)
    {
        _db = db;
        _googleRoutes = googleRoutes;
        _options = options.Value;
    }

    public async Task<DeliveryRoutingCapacity> GetCapacityAsync(
        int branchId,
        double branchLatitude,
        double branchLongitude,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var today = ColombiaTimeHelper.GetTodayDateOnlyColombiaFromUtc(nowUtc);
        var blockedIds = await _db.DeliverymanDayStates
            .AsNoTracking()
            .Where(x => x.BranchId == branchId && x.Date == today && x.Blocked)
            .Select(x => x.DeliverymanId)
            .ToListAsync(cancellationToken);
        var sessions = await _db.DeliveryWorkSessions
            .AsNoTracking()
            .Where(x => x.BranchId == branchId
                        && x.Status == DeliveryWorkSessionStatus.Active
                        && x.AutoCloseAt > nowUtc
                        && !blockedIds.Contains(x.DeliverymanId))
            .Select(x => new { x.Id, x.DeliverymanId })
            .ToListAsync(cancellationToken);
        if (sessions.Count == 0)
            return new DeliveryRoutingCapacity([], 0, 0, []);

        var deliverymanIds = sessions.Select(x => x.DeliverymanId).Distinct().ToList();
        var pendingIds = await _db.Orders
            .AsNoTracking()
            .Where(x => x.BranchId == branchId
                        && x.DeliveryManId.HasValue
                        && deliverymanIds.Contains(x.DeliveryManId.Value)
                        && x.Status != OrderStatus.Delivered
                        && x.Status != OrderStatus.Cancelled)
            .Select(x => x.DeliveryManId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var freshnessCutoff = nowUtc.AddSeconds(-Math.Max(30, _options.ActiveGpsFreshnessSeconds));
        var sessionIds = sessions.Select(x => x.Id).ToList();
        var locationRows = await _db.DeliverymanLocations
            .AsNoTracking()
            .Where(x => x.WorkSessionId.HasValue
                        && sessionIds.Contains(x.WorkSessionId.Value)
                        && x.RecordedAt >= freshnessCutoff
                        && x.GpsEnabled != false)
            .OrderByDescending(x => x.RecordedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.DeliverymanId, x.Latitude, x.Longitude })
            .ToListAsync(cancellationToken);
        var latest = locationRows.GroupBy(x => x.DeliverymanId).ToDictionary(x => x.Key, x => x.First());
        var allowedMeters = await _db.Branches
            .AsNoTracking()
            .Where(x => x.Id == branchId)
            .Select(x => x.DeliveryTrackingAllowedDistanceMeters)
            .SingleAsync(cancellationToken);

        var slots = new List<RoutingVehicleSlot>();
        var warnings = new List<string>();
        var availableNow = 0;
        var availableSoon = 0;
        foreach (var deliverymanId in deliverymanIds)
        {
            if (!latest.TryGetValue(deliverymanId, out var point) || pendingIds.Contains(deliverymanId))
                continue;

            var distance = GeoHelper.HaversineDistanceMeters(
                (double)point.Latitude,
                (double)point.Longitude,
                branchLatitude,
                branchLongitude);
            if (distance <= Math.Max(1, allowedMeters))
            {
                slots.Add(new RoutingVehicleSlot(0));
                availableNow++;
                continue;
            }

            var metrics = await _googleRoutes.ComputeRouteAsync(
                [((double)point.Latitude, (double)point.Longitude), (branchLatitude, branchLongitude)],
                cancellationToken);
            if (metrics.DurationSeconds is > 0 && metrics.DurationSeconds <= _options.SoonAvailableThresholdSeconds)
            {
                slots.Add(new RoutingVehicleSlot(metrics.DurationSeconds));
                availableSoon++;
            }
            else if (metrics.DurationSeconds == 0)
            {
                warnings.Add($"No fue posible estimar el regreso del domiciliario {deliverymanId}.");
            }
        }

        return new DeliveryRoutingCapacity(slots, availableNow, availableSoon, warnings);
    }
}

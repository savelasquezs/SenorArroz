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

    public DeliverymanAvailabilityService(IApplicationDbContext db,
        IGoogleRoutesDrivingMetricsService googleRoutes, IOptions<DeliveryRoutingOptions> options)
    {
        _db = db;
        _googleRoutes = googleRoutes;
        _options = options.Value;
    }

    public async Task<DeliveryRoutingCapacity> GetCapacityAsync(int branchId, double branchLatitude,
        double branchLongitude, DateTime nowUtc, CancellationToken ct = default)
    {
        var branch = await _db.Branches.AsNoTracking().SingleAsync(b => b.Id == branchId, ct);
        var today = ColombiaTimeHelper.GetTodayDateOnlyColombiaFromUtc(nowUtc);
        var blocked = await _db.DeliverymanDayStates.AsNoTracking()
            .Where(s => s.BranchId == branchId && s.Date == today && s.Blocked)
            .Select(s => s.DeliverymanId).ToListAsync(ct);
        var sessions = await DeliveryRouteScope.ActiveSessions(_db, branchId, nowUtc).AsNoTracking()
            .Where(s => _db.Users.Any(u => u.Id == s.DeliverymanId && u.BranchId == branchId && u.Active
                && (u.Role == UserRole.Deliveryman || (u.DeliveryAppEnabled
                    && (u.Role == UserRole.Admin || u.Role == UserRole.Cashier)))))
            .ToListAsync(ct);
        var couriers = await _db.Users.AsNoTracking().Where(u => u.BranchId == branchId && u.Active
            && (u.Role == UserRole.Deliveryman || (u.DeliveryAppEnabled
                && (u.Role == UserRole.Admin || u.Role == UserRole.Cashier))))
            .Select(u => new { u.Id, u.Name }).ToListAsync(ct);
        var offDuty = couriers.Where(u => sessions.All(s => s.DeliverymanId != u.Id))
            .Select(u => new DeliveryCourierAvailability(u.Id, u.Name, 0, "offDuty", null, [], null, null,
                "confirmed", "workSession")).ToList();
        if (sessions.Count == 0) return new DeliveryRoutingCapacity([], 0, 0, []) { Couriers = offDuty };
        var courierIds = sessions.Select(s => s.DeliverymanId).ToArray();
        var names = couriers.ToDictionary(u => u.Id, u => u.Name);
        var sessionIds = sessions.Select(s => s.Id).ToArray();
        var earliestSession = sessions.Min(s => s.StartedAt);
        var routes = await _db.DeliveryRoutes.AsNoTracking().Include(r => r.Stops)
            .Where(r => r.BranchId == branchId && courierIds.Contains(r.DeliverymanId)
                && (r.Status == DeliveryRouteStatus.Open || r.Status == DeliveryRouteStatus.InProgress)
                && (r.WorkSessionId.HasValue && sessionIds.Contains(r.WorkSessionId.Value)
                    || r.WorkSessionId == null && r.LastAssignmentAtUtc >= earliestSession))
            .OrderByDescending(r => r.LastAssignmentAtUtc).ThenByDescending(r => r.Id).ToListAsync(ct);
        var pending = await _db.Orders.AsNoTracking()
            .Where(o => o.BranchId == branchId && o.DeliveryManId.HasValue && courierIds.Contains(o.DeliveryManId.Value)
                && o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled)
            .Select(o => new { o.Id, Courier = o.DeliveryManId!.Value, o.DeliveryRouteId }).ToListAsync(ct);
        var maximumAge = Math.Max(Math.Clamp(_options.ActiveGpsFreshnessSeconds, 30, 600),
            Math.Clamp(branch.DeliveryTrackingLightIntervalSeconds, 30, 900) * 2 + 30);
        var cutoff = nowUtc.AddSeconds(-maximumAge);
        var points = await _db.DeliverymanLocations.AsNoTracking()
            .Where(p => p.WorkSessionId.HasValue && sessionIds.Contains(p.WorkSessionId.Value)
                && p.RecordedAt >= cutoff && p.RecordedAt <= nowUtc.AddSeconds(15))
            .OrderByDescending(p => p.RecordedAt).ThenByDescending(p => p.Id).ToListAsync(ct);
        var latest = points.GroupBy(p => p.WorkSessionId!.Value).ToDictionary(g => g.Key, g => g.First());
        var slots = new List<RoutingVehicleSlot>();
        var fleet = new List<DeliveryCourierAvailability>(offDuty);
        var warnings = new List<string>();
        foreach (var session in sessions.OrderBy(s => s.DeliverymanId))
        {
            var id = session.DeliverymanId;
            var assigned = pending.Where(o => o.Courier == id).ToList();
            var ownedRoutes = routes.Where(r => r.Stops.Count > 0 && DeliveryRouteScope.BelongsToSession(r, session, nowUtc)).ToList();
            var route = ownedRoutes.FirstOrDefault();
            latest.TryGetValue(session.Id, out var point);
            var state = "unknown";
            var confidence = "unknown";
            var source = "none";
            DateTime? availableAt = null;
            if (blocked.Contains(id)) state = "unavailable";
            else if (ownedRoutes.Count > 1 || assigned.Any(o => o.DeliveryRouteId != route?.Id))
            {
                state = "inconsistent";
                warnings.Add($"El domiciliario {id} requiere revisión de su ruta/jornada.");
            }
            else if (point is not null && point.DeliverymanId == id && DeliveryRouteScope.Reliable(point))
            {
                var age = Math.Max(0, (nowUtc - point.RecordedAt).TotalSeconds);
                var cadence = point.TrackingMode == DeliveryTrackingMode.Light
                    ? branch.DeliveryTrackingLightIntervalSeconds : branch.DeliveryTrackingActiveIntervalSeconds;
                var validAge = Math.Clamp(cadence, 15, 900) * 2 + 30;
                var fresh = age <= Math.Min(validAge, Math.Clamp(_options.ActiveGpsFreshnessSeconds, 30, 600));
                if (age <= validAge)
                {
                    var distance = GeoHelper.HaversineDistanceMeters((double)point.Latitude,
                        (double)point.Longitude, branchLatitude, branchLongitude);
                    var inside = distance <= Math.Max(1, branch.DeliveryTrackingAllowedDistanceMeters);
                    confidence = fresh ? "confirmed" : "probable";
                    source = "gps";
                    if (assigned.Count > 0)
                        state = fresh && inside && route?.DepartedAtUtc is null ? "loading" : "delivering";
                    else if (inside)
                    {
                        state = fresh ? "available" : "probableAtBranch";
                        if (fresh)
                        {
                            availableAt = nowUtc;
                            slots.Add(new RoutingVehicleSlot(0) { DeliverymanId = id, WorkSessionId = session.Id,
                                CurrentRouteId = route?.Id, AvailableAtUtc = nowUtc, Confidence = confidence, Source = source });
                        }
                    }
                    else if (route is not null)
                    {
                        state = "returning";
                        source = "approximate";
                        confidence = "probable";
                        var duration = (int)Math.Ceiling(distance * Math.Clamp(_options.ApproximateRoadFactor, 1, 3)
                            / (Math.Clamp(_options.ApproximateUrbanSpeedKph, 5, 60) / 3.6));
                        if (fresh)
                        {
                            try
                            {
                                var metrics = await _googleRoutes.ComputeRouteAsync(
                                    [((double)point.Latitude, (double)point.Longitude), (branchLatitude, branchLongitude)], ct);
                                if (metrics.DurationSeconds > 0) { duration = metrics.DurationSeconds; source = "google"; }
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            { warnings.Add($"Regreso aproximado del domiciliario {id}; Google no respondió."); }
                        }
                        var conservative = Math.Max(30, duration + (int)Math.Ceiling(age) + Math.Max(30, duration / 4));
                        availableAt = nowUtc.AddSeconds(conservative);
                        if (conservative <= Math.Max(0, _options.SoonAvailableThresholdSeconds))
                            slots.Add(new RoutingVehicleSlot(conservative) { DeliverymanId = id, WorkSessionId = session.Id,
                                CurrentRouteId = route.Id, AvailableAtUtc = availableAt, Confidence = confidence, Source = source });
                    }
                    else state = "away";
                }
            }
            fleet.Add(new DeliveryCourierAvailability(id, names.GetValueOrDefault(id, string.Empty), session.Id,
                state, route?.Id, assigned.Select(o => o.Id).OrderBy(x => x).ToArray(),
                point?.RecordedAt, availableAt, confidence, source));
        }
        return new DeliveryRoutingCapacity(slots, slots.Count(s => s.AvailableAtSeconds == 0),
            slots.Count(s => s.AvailableAtSeconds > 0), warnings) { Couriers = fleet };
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Helpers;

/// <summary>One definition of a current trip for tracking, assignment and fleet planning.</summary>
public static class DeliveryRouteScope
{
    public static IQueryable<DeliveryWorkSession> ActiveSessions(IApplicationDbContext db, int branchId, DateTime now) =>
        db.DeliveryWorkSessions.Where(s => s.BranchId == branchId
            && s.Status == DeliveryWorkSessionStatus.Active && s.StartedAt <= now && s.AutoCloseAt > now
            && !db.DeliveryWorkSessions.Any(newer => newer.BranchId == branchId
                && newer.DeliverymanId == s.DeliverymanId
                && newer.Status == DeliveryWorkSessionStatus.Active && newer.StartedAt <= now && newer.AutoCloseAt > now
                && (newer.StartedAt > s.StartedAt || (newer.StartedAt == s.StartedAt && newer.Id > s.Id))));

    public static IQueryable<DeliveryRoute> Current(IApplicationDbContext db, int branchId, int deliverymanId, DateTime now)
    {
        var sessions = ActiveSessions(db, branchId, now).Where(s => s.DeliverymanId == deliverymanId);
        return db.DeliveryRoutes.Where(r => r.BranchId == branchId && r.DeliverymanId == deliverymanId
            && (r.Status == DeliveryRouteStatus.Open || r.Status == DeliveryRouteStatus.InProgress)
            && r.Stops.Any()
            && sessions.Any(s => r.WorkSessionId == s.Id || (r.WorkSessionId == null
                && r.LastAssignmentAtUtc >= s.StartedAt && r.LastAssignmentAtUtc < s.AutoCloseAt
                && r.LastAssignmentAtUtc <= now && (!r.RouteStartedAtUtc.HasValue || r.RouteStartedAtUtc >= s.StartedAt))))
            .OrderByDescending(r => r.LastAssignmentAtUtc).ThenByDescending(r => r.Id);
    }

    public static bool BelongsToSession(DeliveryRoute route, DeliveryWorkSession session, DateTime now) =>
        route.BranchId == session.BranchId && route.DeliverymanId == session.DeliverymanId
        && route.TenantId == session.TenantId
        && session.Status == DeliveryWorkSessionStatus.Active && session.StartedAt <= now && session.AutoCloseAt > now
        && (route.Status == DeliveryRouteStatus.Open || route.Status == DeliveryRouteStatus.InProgress)
        && (route.WorkSessionId == session.Id || (route.WorkSessionId is null
            && route.LastAssignmentAtUtc >= session.StartedAt && route.LastAssignmentAtUtc < session.AutoCloseAt
            && route.LastAssignmentAtUtc <= now && (!route.RouteStartedAtUtc.HasValue || route.RouteStartedAtUtc >= session.StartedAt)));

    public static bool Reliable(DeliverymanLocation point) => point.GpsEnabled != false
        && point.AccuracyMeters is >= 0 and <= 50 && double.IsFinite(point.AccuracyMeters.Value)
        && point.Latitude is >= -90 and <= 90 && point.Longitude is >= -180 and <= 180
        && !(point.Latitude == 0 && point.Longitude == 0);

    public static double Distance(DeliverymanLocation point, Branch branch) =>
        GeoHelper.HaversineDistanceMeters((double)point.Latitude, (double)point.Longitude,
            (double)branch.Latitude!.Value, (double)branch.Longitude!.Value);

    public static async Task ObserveDepartureAsync(IApplicationDbContext db, DeliverymanLocation point,
        DeliveryWorkSession session, DateTime now, CancellationToken ct)
    {
        if (!point.DeliveryRouteId.HasValue || !Reliable(point)
            || point.RecordedAt < now.AddMinutes(-2) || point.RecordedAt > now.AddSeconds(15)) return;
        var route = await db.DeliveryRoutes.Include(r => r.Branch)
            .SingleOrDefaultAsync(r => r.Id == point.DeliveryRouteId, ct);
        if (route is null || !BelongsToSession(route, session, now)) return;
        route.WorkSessionId ??= session.Id;
        var branch = route.Branch;
        if (route.DepartedAtUtc is not null || branch.Latitude is null || branch.Longitude is null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }
        var radius = Math.Max(1, branch.DeliveryTrackingAllowedDistanceMeters);
        var outsideRadius = Math.Max(80, radius * 2);
        if (Distance(point, branch) < outsideRadius) { await db.SaveChangesAsync(ct); return; }
        var points = await db.DeliverymanLocations.AsNoTracking()
            .Where(p => p.DeliverymanId == route.DeliverymanId && p.DeliveryRouteId == route.Id
                && p.WorkSessionId == session.Id && p.RecordedAt >= route.CreatedAt
                && p.RecordedAt <= point.RecordedAt)
            .OrderByDescending(p => p.RecordedAt).ThenByDescending(p => p.Id)
            .Take(32).ToListAsync(ct);
        var previous = points.FirstOrDefault(p => p.RecordedAt < point.RecordedAt);
        if (previous is null || !Reliable(previous)
            || point.RecordedAt - previous.RecordedAt < TimeSpan.FromSeconds(15)
            || point.RecordedAt - previous.RecordedAt > TimeSpan.FromSeconds(90)
            || Distance(previous, branch) < outsideRadius
            || !points.Any(p => p.RecordedAt < previous.RecordedAt && Reliable(p) && Distance(p, branch) <= radius))
        {
            await db.SaveChangesAsync(ct);
            return;
        }
        route.DepartedAtUtc = previous.RecordedAt;
        route.DepartureSource = "gps";
        db.EntityAuditLogs.Add(new EntityAuditLog
        {
            TenantId = route.TenantId, BranchId = route.BranchId, EntityType = "delivery_route",
            EntityId = route.Id, OperationType = "departure_observed", ChangedAt = now,
            BusinessDate = now.AddHours(-5).Date, ChangedByUserId = route.DeliverymanId,
            SummaryText = $"Salida física observada de ruta #{route.Id}",
            MetadataJson = JsonSerializer.Serialize(new { workSessionId = session.Id,
                observedAtUtc = previous.RecordedAt, receivedAtUtc = now, locationId = previous.Id, source = "gps" })
        });
        await db.SaveChangesAsync(ct);
    }
}

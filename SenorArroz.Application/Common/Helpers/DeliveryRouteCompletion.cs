using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Helpers;

public static class DeliveryRouteCompletion
{
    public static DateTime? Start(DeliveryRoute route) => route.DepartedAtUtc
        ?? route.Stops.Select(s => s.Order.GetStatusTimes().TryGetValue("ontheway", out var time)
            ? (DateTime?)time : null).Min() ?? route.RouteStartedAtUtc;

    public static bool Complete(DeliveryRoute route, IReadOnlyCollection<Order> orders, DateTime end)
    {
        if (route.CompletedAtUtc.HasValue || route.Status == DeliveryRouteStatus.Cancelled
            || orders.Count == 0 || !orders.Any(o => o.Status == OrderStatus.Delivered)
            || orders.Any(o => o.Status is not (OrderStatus.Delivered or OrderStatus.Cancelled))) return false;
        var start = route.DepartedAtUtc ?? orders.Select(o => o.GetStatusTimes().TryGetValue("ontheway", out var time)
            ? (DateTime?)time : null).Min() ?? route.RouteStartedAtUtc;
        if (!start.HasValue || end < start || orders.Any(o => o.Status == OrderStatus.Delivered
            && o.GetStatusTimes().TryGetValue("delivered", out var delivered) && delivered > end)) return false;
        route.CompletedAtUtc = end;
        route.Status = DeliveryRouteStatus.Completed;
        route.ActualDurationSeconds = (int)(end - (route.RouteStartedAtUtc ?? start.Value)).TotalSeconds;
        route.MetSla = route.ActualDurationSeconds >= 0 && route.MetaDurationSeconds.HasValue
            ? route.ActualDurationSeconds <= route.MetaDurationSeconds : null;
        return true;
    }

    public static async Task ResolveHistoryAsync(IApplicationDbContext db, IReadOnlyCollection<DeliveryRoute> routes,
        CancellationToken ct)
    {
        var missing = routes.Where(r => r.CompletedAtUtc == null && Start(r).HasValue).ToList();
        if (missing.Count == 0) return;
        var drivers = missing.Select(r => r.DeliverymanId).Distinct().ToArray();
        var from = missing.Min(r => Start(r)!.Value).AddDays(-1);
        var to = missing.Max(r => Start(r)!.Value).AddDays(2);
        var candidates = await db.DeliveryRoutes.AsNoTracking().Where(r => drivers.Contains(r.DeliverymanId)
            && r.Status != DeliveryRouteStatus.Cancelled && r.LastAssignmentAtUtc >= from && r.LastAssignmentAtUtc <= to)
            .Include(r => r.Stops).ThenInclude(s => s.Order).AsSplitQuery().ToListAsync(ct);
        foreach (var route in missing)
        {
            var start = Start(route)!.Value;
            var next = candidates.Where(r => r.Id != route.Id && r.TenantId == route.TenantId
                && r.BranchId == route.BranchId && r.DeliverymanId == route.DeliverymanId && Start(r) > start)
                .OrderBy(r => Start(r)).ThenBy(r => r.Id).FirstOrDefault();
            if (next is null) continue;
            var end = Start(next)!.Value;
            if (route.WorkSessionId.HasValue && next.WorkSessionId.HasValue
                ? route.WorkSessionId != next.WorkSessionId
                : ColombiaTimeHelper.GetTodayStartInUtcFromUtc(start) != ColombiaTimeHelper.GetTodayStartInUtcFromUtc(end)) continue;
            Complete(route, route.Stops.Select(s => s.Order).ToList(), end);
        }
    }
}

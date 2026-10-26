using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Dashboard.Services;

public static class DeliveryPerformanceAnalytics
{
    public sealed record OrderSample(Order Order, DateTime DeliveredAt, DateTime? OnTheWayAt);
    public sealed record PendingOrderSample(Order Order, DateTime OnTheWayAt);
    public sealed record GpsTrack(List<List<DashboardRoutePointDto>> Segments, double CoveragePercent, double DistanceKm, bool Complete);
    public sealed record RouteSample(DeliveryRoute Route, GpsTrack Track, bool? InconsistentReturn = null)
    {
        public bool HasInconsistentReturn => InconsistentReturn ?? IsReturnInconsistent(Route, []);
    }

    public static bool IncludesDay(DateTime timestampUtc, bool excludeWednesdays) =>
        !excludeWednesdays || ColombiaTimeHelper.GetNowInColombiaFromUtc(timestampUtc).DayOfWeek != DayOfWeek.Wednesday;

    public static DateTime? StatusTime(string? json, string status)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (doc.RootElement.TryGetProperty(status, out var value) && value.ValueKind == JsonValueKind.String
                && DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
                return timestamp;
        }
        catch (JsonException) { }
        return null;
    }

    public static GpsTrack AnalyzeTrack(DeliveryRoute route, IEnumerable<DeliverymanLocation> locations, DateTime? observationEnd = null)
    {
        var segments = new List<List<DashboardRoutePointDto>>();
        if (route.DepartedAtUtc is not { } start || (route.CompletedAtUtc ?? observationEnd) is not { } end
            || end <= start || !route.WorkSessionId.HasValue)
            return new(segments, 0, 0, false);

        var maxGap = Math.Max(180, (route.Branch?.DeliveryTrackingActiveIntervalSeconds ?? 30) * 3);
        DeliverymanLocation? previous = null;
        List<DashboardRoutePointDto>? segment = null;
        double coveredSeconds = 0, meters = 0;
        var interrupted = false;
        foreach (var point in locations.Where(p => p.RecordedAt >= start && p.RecordedAt <= end
                     && p.DeliveryRouteId == route.Id && p.DeliverymanId == route.DeliverymanId
                     && p.WorkSessionId == route.WorkSessionId).OrderBy(p => p.RecordedAt).ThenBy(p => p.Id))
        {
            if (point.GpsEnabled == false || point.AccuracyMeters is not (>= 0 and <= 50)
                || point.Latitude is < -90 or > 90 || point.Longitude is < -180 or > 180)
            {
                previous = null;
                segment = null;
                interrupted = true;
                continue;
            }

            var seconds = previous == null ? 0 : (point.RecordedAt - previous.RecordedAt).TotalSeconds;
            if (previous != null && seconds <= 0) continue;
            var distance = previous == null ? 0 : GeoHelper.HaversineDistanceMeters(
                (double)previous.Latitude, (double)previous.Longitude, (double)point.Latitude, (double)point.Longitude);
            var connected = previous != null && seconds <= maxGap && distance / seconds <= 120d / 3.6;
            if (!connected)
            {
                if (previous != null) interrupted = true;
                segment = new();
                segments.Add(segment);
            }
            else
            {
                coveredSeconds += seconds;
                meters += distance;
            }
            segment!.Add(new() { Latitude = point.Latitude, Longitude = point.Longitude, RecordedAt = point.RecordedAt });
            previous = point;
        }
        var coverage = Math.Min(100, 100 * coveredSeconds / (end - start).TotalSeconds);
        var complete = coverage >= 80 && meters > 0 && !interrupted;
        return new(segments, Math.Round(coverage, 1), meters / 1000, complete);
    }

    public static (DateTime? Start, bool UsesOrderDeparture) RouteTiming(DeliveryRoute route)
    {
        if (route.ClosureSource is "next_route" or "legacy") return (null, false);
        if (route.DepartedAtUtc.HasValue) return (route.DepartedAtUtc, false);
        var start = route.Stops.Select(stop => new
            {
                Start = StatusTime(stop.Order.StatusTimes, "ontheway"),
                Delivered = StatusTime(stop.Order.StatusTimes, "delivered"),
            }).Where(stop => stop.Start.HasValue && (stop.Delivered >= stop.Start || (route.CompletedAtUtc == null && !stop.Delivered.HasValue))
                && (route.CompletedAtUtc == null || route.CompletedAtUtc >= stop.Start))
            .Select(stop => stop.Start).Min();
        return (start, start.HasValue);
    }

    public static string RouteState(DeliveryRoute route) => route.Status == DeliveryRouteStatus.Cancelled ? "cancelled"
        : route.Status == DeliveryRouteStatus.Completed && (route.CompletedAtUtc.HasValue || route.ClosureSource == "legacy") ? "completed"
        : route.Stops.Any(s => s.Order.Status == OrderStatus.Delivered)
            && route.Stops.All(s => s.Order.Status is OrderStatus.Delivered or OrderStatus.Cancelled) ? "returnPending" : "inProgress";

    public static DateTime RouteActivityTime(DeliveryRoute route) =>
        RouteState(route) == "completed" ? route.CompletedAtUtc ?? route.RouteStartedAtUtc ?? route.LastAssignmentAtUtc
            : RouteTiming(route).Start ?? route.RouteStartedAtUtc ?? route.LastAssignmentAtUtc;

    public static bool IsReturnInconsistent(DeliveryRoute route, IReadOnlyList<DeliveryWorkSession> sessions)
    {
        var start = RouteTiming(route).Start;
        if (start is not { } departure || route.CompletedAtUtc is not { } returned) return false;
        if (returned < departure) return true;
        var matching = sessions.Where(s => s.TenantId == route.TenantId && s.BranchId == route.BranchId
            && s.DeliverymanId == route.DeliverymanId).ToList();
        var session = route.WorkSessionId.HasValue
            ? matching.FirstOrDefault(s => s.Id == route.WorkSessionId)
            : matching.Where(s => s.StartedAt <= departure && departure <= SessionEnd(s))
                .OrderByDescending(s => s.StartedAt).FirstOrDefault();
        if (session != null)
            return departure < session.StartedAt || returned > SessionEnd(session);
        return ColombiaTimeHelper.GetNowInColombiaFromUtc(departure).Date
            != ColombiaTimeHelper.GetNowInColombiaFromUtc(returned).Date;
    }

    private static DateTime SessionEnd(DeliveryWorkSession session) =>
        session.EndedAt is { } ended && (session.AutoCloseAt <= session.StartedAt || ended < session.AutoCloseAt)
            ? ended : session.AutoCloseAt;

    public static async Task<List<DeliveryWorkSession>> LoadWorkSessionsAsync(IApplicationDbContext db,
        IReadOnlyList<DeliveryRoute> routes, CancellationToken ct)
    {
        if (routes.Count == 0) return [];
        var drivers = routes.Select(r => r.DeliverymanId).Distinct().ToArray();
        var branches = routes.Select(r => r.BranchId).Distinct().ToArray();
        var ids = routes.Where(r => r.WorkSessionId.HasValue).Select(r => r.WorkSessionId!.Value).ToArray();
        var from = routes.Min(r => RouteTiming(r).Start ?? r.LastAssignmentAtUtc);
        var to = routes.Max(r => r.CompletedAtUtc ?? r.LastAssignmentAtUtc);
        return await db.DeliveryWorkSessions.AsNoTracking().Where(s => drivers.Contains(s.DeliverymanId)
            && branches.Contains(s.BranchId) && (ids.Contains(s.Id)
                || (s.StartedAt <= to && (s.EndedAt >= from || s.AutoCloseAt >= from)))).ToListAsync(ct);
    }

    public static T Summarize<T>(IReadOnlyList<OrderSample> orders, IReadOnlyList<RouteSample> routes,
        IReadOnlyList<PendingOrderSample>? pendingOrders = null)
        where T : DashboardDeliveryPerformanceSummaryDto, new()
    {
        var activityRoutes = routes.Where(sample => RouteState(sample.Route) != "cancelled").ToList();
        routes = activityRoutes.Where(sample => RouteState(sample.Route) == "completed").ToList();
        var durations = routes.Where(sample => !sample.HasInconsistentReturn).Select(sample => (Source: sample, Timing: RouteTiming(sample.Route), End: sample.Route.CompletedAtUtc))
            .Where(sample => sample.Timing.Start.HasValue && sample.End >= sample.Timing.Start)
            .Select(sample => (Minutes: (sample.End!.Value - sample.Timing.Start!.Value).TotalMinutes, sample.Timing.UsesOrderDeparture, sample.Source)).ToList();
        var orderMinutes = orders.Where(x => x.Order.DeliveryCompletionSource is not ("next_route" or "return" or "legacy" or "offline_confirmation") && x.OnTheWayAt.HasValue && x.DeliveredAt >= x.OnTheWayAt)
            .Select(x => (x.DeliveredAt - x.OnTheWayAt!.Value).TotalMinutes).OrderBy(x => x).ToList();
        var sla = routes.Where(x => x.Route.MetSla.HasValue).ToList();
        var distances = durations.Select(sample =>
        {
            var useGps = !sample.UsesOrderDeparture && sample.Source.Track.Complete && double.IsFinite(sample.Source.Track.DistanceKm) && sample.Source.Track.DistanceKm > 0;
            var route = sample.Source.Route;
            var distanceKm = useGps ? sample.Source.Track.DistanceKm
                : route.PlannedDistanceMeters is > 0 && route.ReturnToBranchMeters is >= 0
                    ? ((double)route.PlannedDistanceMeters.Value + route.ReturnToBranchMeters.Value) / 1000 : 0;
            return (sample.Minutes, Km: distanceKm, Estimated: !useGps);
        }).Where(sample => sample.Km > 0).ToList();
        var gps = distances.Where(sample => !sample.Estimated).ToList();
        var estimated = distances.Where(sample => sample.Estimated).ToList();
        var stages = new List<(double Dispatch, double Delivery, double Return)>();
        var deliveredInRoutes = 0;
        foreach (var sample in routes)
        {
            var route = sample.Route;
            var delivered = route.Stops.Where(s => s.Order.Status == OrderStatus.Delivered)
                .Select(s => new { Delivered = StatusTime(s.Order.StatusTimes, "delivered"), Ready = StatusTime(s.Order.StatusTimes, "ready") }).ToList();
            deliveredInRoutes += delivered.Count;
            var timing = RouteTiming(route);
            if (sample.HasInconsistentReturn || route.ClosureSource is "next_route" or "legacy"
                || route.Stops.Any(s => s.Order.DeliveryCompletionSource is "next_route" or "return" or "legacy" or "offline_confirmation")) continue;
            if (timing.Start is not { } start || route.CompletedAtUtc is not { } end || delivered.Count == 0
                || delivered.Any(x => !x.Delivered.HasValue || !x.Ready.HasValue || x.Delivered < start || x.Delivered > end || x.Ready > start)) continue;
            var ready = delivered.Max(x => x.Ready!.Value);
            var last = delivered.Max(x => x.Delivered!.Value);
            stages.Add(((start - ready).TotalMinutes, (last - start).TotalMinutes, (end - last).TotalMinutes));
        }
        var km = gps.Sum(sample => sample.Km);
        return new T
        {
            AssignedCount = orders.Count(sample => sample.Order.DeliveryManId.HasValue) + (pendingOrders?.Count ?? 0),
            PendingCount = pendingOrders?.Count ?? 0,
            OnTheWayCount = pendingOrders?.Count(sample => sample.Order.Status == OrderStatus.OnTheWay) ?? 0,
            RoutesCount = activityRoutes.Count,
            InProgressRoutesCount = activityRoutes.Count(sample => RouteState(sample.Route) == "inProgress"),
            ReturnPendingRoutesCount = activityRoutes.Count(sample => RouteState(sample.Route) == "returnPending"),
            DeliveredCount = orders.Count,
            CompletedRoutesCount = routes.Count,
            InconsistentReturnCount = routes.Count(sample => sample.HasInconsistentReturn),
            SlaSampleCount = sla.Count,
            RouteSampleCount = durations.Count,
            OrderSampleCount = orderMinutes.Count,
            GpsSampleCount = gps.Count,
            DistanceSampleCount = distances.Count,
            EstimatedDistanceSampleCount = estimated.Count,
            StageSampleCount = stages.Count,
            OnTimePercent = sla.Count > 0 ? Round(100d * sla.Count(x => x.Route.MetSla == true) / sla.Count) : null,
            AvgRouteMinutes = Average(durations.Select(sample => sample.Minutes).ToList()),
            AvgMetaMinutes = Average(routes.Where(x => x.Route.MetaDurationSeconds.HasValue).Select(x => x.Route.MetaDurationSeconds!.Value / 60d).ToList()),
            AvgOrderMinutes = Average(orderMinutes),
            P90OrderMinutes = orderMinutes.Count > 0 ? Round(orderMinutes[(int)Math.Ceiling(orderMinutes.Count * .9) - 1]) : null,
            MinutesPerKm = Ratio(distances.Sum(sample => sample.Minutes), distances.Sum(sample => sample.Km)),
            GpsMinutesPerKm = Ratio(gps.Sum(sample => sample.Minutes), km),
            EstimatedMinutesPerKm = Ratio(estimated.Sum(sample => sample.Minutes), estimated.Sum(sample => sample.Km)),
            OrdersPerRoute = routes.Count > 0 ? Round((double)deliveredInRoutes / routes.Count) : null,
            AvgDispatchMinutes = Average(stages.Select(x => x.Dispatch).ToList()),
            AvgDeliveryStageMinutes = Average(stages.Select(x => x.Delivery).ToList()),
            AvgReturnMinutes = Average(stages.Select(x => x.Return).ToList()),
            TotalGpsKm = Round(km),
        };
    }

    public static async Task<DashboardDeliveryPerformanceDto> BuildAsync(IApplicationDbContext db,
        int? branchId, int? driverId, DateTime from, DateTime to, CancellationToken ct, bool ownOnly = false, bool excludeWednesdays = false)
    {
        var previousTo = from.AddTicks(-1);
        var previousFrom = from - (to.AddTicks(1) - from);
        var orderQuery = db.Orders.AsNoTracking().Where(o => (o.DeliveryManId.HasValue || (o.Type == OrderType.Delivery && o.Status == OrderStatus.Delivered))
            && o.Status != OrderStatus.Cancelled
            && o.UpdatedAt >= previousFrom && o.CreatedAt <= to);
        if (branchId.HasValue) orderQuery = orderQuery.Where(o => o.BranchId == branchId);
        if (ownOnly) orderQuery = orderQuery.Where(o => o.DeliveryManId == driverId);
        var loadedOrders = await orderQuery.Include(o => o.DeliveryMan).ToListAsync(ct);
        var orders = loadedOrders.Where(o => o.Status == OrderStatus.Delivered)
            .Select(o => (Order: o, Time: StatusTime(o.StatusTimes, "delivered")))
            .Where(x => x.Time >= previousFrom && x.Time <= to)
            .Select(x => new OrderSample(x.Order, x.Time!.Value, StatusTime(x.Order.StatusTimes, "ontheway"))).ToList();
        var pendingOrders = loadedOrders.Where(o => o.Status != OrderStatus.Delivered && o.DeliveryManId.HasValue)
            .Select(o => new PendingOrderSample(o, StatusTime(o.StatusTimes, "ontheway")
                ?? StatusTime(o.StatusTimes, "delivery_man_assigned") ?? o.CreatedAt))
            .Where(x => x.OnTheWayAt >= previousFrom && x.OnTheWayAt <= to).ToList();
        var routeQuery = db.DeliveryRoutes.AsNoTracking().Where(r => r.Status != DeliveryRouteStatus.Cancelled
            && ((r.Status == DeliveryRouteStatus.Completed && r.CompletedAtUtc >= previousFrom && r.CompletedAtUtc <= to)
                || (r.CompletedAtUtc == null && r.Stops.Any() && ((r.DepartedAtUtc >= previousFrom && r.DepartedAtUtc <= to)
                    || (r.RouteStartedAtUtc >= previousFrom && r.RouteStartedAtUtc <= to)
                    || (r.LastAssignmentAtUtc >= previousFrom && r.LastAssignmentAtUtc <= to)))));
        if (branchId.HasValue) routeQuery = routeQuery.Where(r => r.BranchId == branchId);
        if (ownOnly) routeQuery = routeQuery.Where(r => r.DeliverymanId == driverId);
        var loadedRoutes = await routeQuery.Include(r => r.Deliveryman).Include(r => r.Branch)
            .Include(r => r.Stops).ThenInclude(s => s.Order).AsSplitQuery().ToListAsync(ct);
        var resolvedReturns = await DeliveryRouteCompletion.ResolveHistoryAsync(db, loadedRoutes, ct);
        loadedRoutes = loadedRoutes.Where(r => RouteActivityTime(r) >= previousFrom && RouteActivityTime(r) <= to).ToList();
        var routeIds = loadedRoutes.Select(r => r.Id).ToArray();
        var locations = await db.DeliverymanLocations.AsNoTracking().Where(p => p.DeliveryRouteId.HasValue
            && routeIds.Contains(p.DeliveryRouteId.Value) && p.RecordedAt >= previousFrom.AddDays(-1) && p.RecordedAt <= to).ToListAsync(ct);
        var pointsByRoute = locations.ToLookup(p => p.DeliveryRouteId!.Value);
        var sessions = await LoadWorkSessionsAsync(db, loadedRoutes, ct);
        var routes = loadedRoutes.Select(r => new RouteSample(r, AnalyzeTrack(r, pointsByRoute[r.Id]),
            !resolvedReturns.Contains(r.Id) && IsReturnInconsistent(r, sessions))).ToList();
        var scopedOrders = orders.Where(x => (!driverId.HasValue || x.Order.DeliveryManId == driverId) && IncludesDay(x.DeliveredAt, excludeWednesdays)).ToList();
        var scopedPending = pendingOrders.Where(x => (!driverId.HasValue || x.Order.DeliveryManId == driverId) && IncludesDay(x.OnTheWayAt, excludeWednesdays)).ToList();
        var scopedRoutes = routes.Where(x => (!driverId.HasValue || x.Route.DeliverymanId == driverId) && IncludesDay(RouteActivityTime(x.Route), excludeWednesdays)).ToList();
        var currentOrders = scopedOrders.Where(x => x.DeliveredAt >= from).ToList();
        var currentPending = scopedPending.Where(x => x.OnTheWayAt >= from).ToList();
        var currentRoutes = scopedRoutes.Where(x => RouteActivityTime(x.Route) >= from).ToList();
        var buckets = DashboardDeliveryTimeBuckets.Create(from, to, excludeWednesdays);
        var options = orders.Select(x => x.Order).Concat(pendingOrders.Select(x => x.Order)).Where(o => o.DeliveryManId.HasValue)
            .Select(o => new DashboardDeliveryPerformanceRowDto { Id = o.DeliveryManId!.Value, Name = o.DeliveryMan?.Name ?? $"#{o.DeliveryManId}", BranchId = o.BranchId })
            .Concat(routes.Select(x => new DashboardDeliveryPerformanceRowDto { Id = x.Route.DeliverymanId, Name = x.Route.Deliveryman.Name, BranchId = x.Route.BranchId }))
            .DistinctBy(x => x.Id).OrderBy(x => x.Name).ToList();
        var result = new DashboardDeliveryPerformanceDto
        {
            Current = Summarize<DashboardDeliveryPerformanceSummaryDto>(currentOrders, currentRoutes, currentPending),
            Previous = Summarize<DashboardDeliveryPerformanceSummaryDto>(scopedOrders.Where(x => x.DeliveredAt <= previousTo).ToList(),
                scopedRoutes.Where(x => RouteActivityTime(x.Route) <= previousTo).ToList(), scopedPending.Where(x => x.OnTheWayAt <= previousTo).ToList()),
            DriverOptions = options,
            Labels = buckets.Labels.ToList(),
            Evolution = Enumerable.Range(0, buckets.Count).Select(i => Summarize<DashboardDeliveryPerformanceSummaryDto>(
                currentOrders.Where(x => buckets.GetBucketIndex(x.DeliveredAt) == i).ToList(),
                currentRoutes.Where(x => buckets.GetBucketIndex(RouteActivityTime(x.Route)) == i).ToList(),
                currentPending.Where(x => buckets.GetBucketIndex(x.OnTheWayAt) == i).ToList())).ToList(),
            RecentRoutes = currentRoutes.OrderByDescending(x => RouteActivityTime(x.Route)).Take(80).Select(x =>
            {
                var timing = RouteTiming(x.Route);
                var completed = RouteState(x.Route) == "completed";
                return new DashboardDeliveryPerformanceRouteDto
                {
                    Status = RouteState(x.Route), StartedAtUtc = timing.Start ?? x.Route.RouteStartedAtUtc ?? x.Route.LastAssignmentAtUtc,
                    PendingOrdersCount = x.Route.Stops.Count(s => s.Order.Status is not (OrderStatus.Delivered or OrderStatus.Cancelled)),
                    Id = x.Route.Id, DeliverymanId = x.Route.DeliverymanId, DeliverymanName = x.Route.Deliveryman.Name,
                    BranchName = x.Route.Branch.Name, DepartedAtUtc = x.Route.DepartedAtUtc, CompletedAtUtc = x.Route.CompletedAtUtc,
                    DeliveredCount = x.Route.Stops.Count(s => s.Order.Status == OrderStatus.Delivered),
                    ReturnIsInconsistent = x.HasInconsistentReturn,
                    DurationMinutes = completed && !x.HasInconsistentReturn && timing.Start.HasValue && x.Route.CompletedAtUtc >= timing.Start
                        ? Round((x.Route.CompletedAtUtc!.Value - timing.Start.Value).TotalMinutes) : null,
                    MetaMinutes = x.Route.MetaDurationSeconds / 60d, MetSla = completed ? x.Route.MetSla : null,
                    GpsDistanceKm = x.Track.Complete ? Round(x.Track.DistanceKm) : null,
                    GpsCoveragePercent = x.Track.CoveragePercent,
                    PlannedDistanceKm = ((x.Route.PlannedDistanceMeters ?? 0) + (x.Route.ReturnToBranchMeters ?? 0)) / 1000d,
                };
            }).ToList(),
        };
        if (driverId.HasValue && !ownOnly)
        {
            var teamOrders = orders.Where(x => x.Order.DeliveryManId.HasValue && x.Order.DeliveryManId != driverId && x.DeliveredAt >= from
                && IncludesDay(x.DeliveredAt, excludeWednesdays)).ToList();
            var teamPending = pendingOrders.Where(x => x.Order.DeliveryManId != driverId && x.OnTheWayAt >= from
                && IncludesDay(x.OnTheWayAt, excludeWednesdays)).ToList();
            var teamRoutes = routes.Where(x => x.Route.DeliverymanId != driverId && RouteActivityTime(x.Route) >= from
                && IncludesDay(RouteActivityTime(x.Route), excludeWednesdays)).ToList();
            result.Team = Summarize<DashboardDeliveryPerformanceSummaryDto>(teamOrders, teamRoutes, teamPending);
            result.TeamEvolution = Enumerable.Range(0, buckets.Count).Select(i => Summarize<DashboardDeliveryPerformanceSummaryDto>(
                teamOrders.Where(x => buckets.GetBucketIndex(x.DeliveredAt) == i).ToList(),
                teamRoutes.Where(x => buckets.GetBucketIndex(RouteActivityTime(x.Route)) == i).ToList(),
                teamPending.Where(x => buckets.GetBucketIndex(x.OnTheWayAt) == i).ToList())).ToList();
        }
        foreach (var option in options.Where(x => !driverId.HasValue || x.Id == driverId))
        {
            var row = Summarize<DashboardDeliveryPerformanceRowDto>(currentOrders.Where(x => x.Order.DeliveryManId == option.Id).ToList(),
                currentRoutes.Where(x => x.Route.DeliverymanId == option.Id).ToList(), currentPending.Where(x => x.Order.DeliveryManId == option.Id).ToList());
            row.Id = option.Id; row.Name = option.Name; row.BranchId = option.BranchId;
            if (row.AssignedCount > 0 || row.RoutesCount > 0) result.Drivers.Add(row);
        }
        result.Drivers = result.Drivers.OrderByDescending(x => x.DeliveredCount).ToList();
        return result;
    }
    private static double Round(double value) => Math.Round(value, 2);
    private static double? Ratio(double minutes, double km) => km > 0 ? Round(minutes / km) : null;
    private static double? Average(List<double> values) => values.Count > 0 ? Round(values.Average()) : null;
}

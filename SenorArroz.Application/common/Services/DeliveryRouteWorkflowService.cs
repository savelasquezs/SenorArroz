using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public class DeliveryRouteWorkflowService : IDeliveryRouteWorkflowService
{
    private readonly IApplicationDbContext _db;
    private readonly IGoogleRoutesDrivingMetricsService _routesMetrics;
    private readonly DeliveryRouteOptions _opt;
    private readonly ILogger<DeliveryRouteWorkflowService> _logger;
    private readonly IClock _clock;
    private readonly IBackgroundWorkSignal<DeliveryRouteConsolidationWork>? _consolidationSignal;

    public DeliveryRouteWorkflowService(IApplicationDbContext db,
        IGoogleRoutesDrivingMetricsService routesMetrics, IOptions<DeliveryRouteOptions> options,
        ILogger<DeliveryRouteWorkflowService> logger, IClock clock,
        IBackgroundWorkSignal<DeliveryRouteConsolidationWork>? consolidationSignal = null)
    {
        _db = db;
        _routesMetrics = routesMetrics;
        _opt = options.Value;
        _logger = logger;
        _clock = clock;
        _consolidationSignal = consolidationSignal;
    }

    public async Task OnOrderAssignedToDeliverymanAsync(Order order, CancellationToken ct = default)
    {
        if ((order.Type != OrderType.Delivery
            && !(order.Type == OrderType.Reservation && order.AddressId.HasValue)) || order.DeliveryManId is null) return;
        var tracked = await _db.Orders.Include(o => o.Address).FirstOrDefaultAsync(o => o.Id == order.Id, ct);
        if (tracked?.DeliveryManId is not int deliverymanId) return;
        var now = _clock.UtcNow;
        var session = await DeliveryRouteScope.ActiveSessions(_db, tracked.BranchId, now).AsNoTracking()
            .FirstOrDefaultAsync(s => s.DeliverymanId == deliverymanId, ct);
        var oldStops = await _db.DeliveryRouteStops.Where(s => s.OrderId == tracked.Id).ToListAsync(ct);
        var oldRouteIds = oldStops.Select(s => s.DeliveryRouteId).Distinct().ToArray();
        _db.DeliveryRouteStops.RemoveRange(oldStops);
        tracked.DeliveryRouteId = null;

        // Manual cashier assignments before a courier logs in remain supported.
        // Unbound trips can only be grouped within the same business day; GPS does
        // not bind a trip from a previous workday or rewrite its operational clock.
        var todayStart = ColombiaTimeHelper.GetTodayStartInUtcFromUtc(now);
        var candidates = session is not null
            ? DeliveryRouteScope.Current(_db, tracked.BranchId, deliverymanId, now)
            : _db.DeliveryRoutes.Where(r => r.DeliverymanId == deliverymanId && r.BranchId == tracked.BranchId
                && r.WorkSessionId == null && r.LastAssignmentAtUtc >= todayStart && r.LastAssignmentAtUtc <= now
                && ((r.Status == DeliveryRouteStatus.InProgress && r.RouteStartedAtUtc >= todayStart)
                    || r.Status == DeliveryRouteStatus.Open));
        var activeRoutes = await candidates.Include(r => r.Stops)
            .OrderByDescending(r => r.LastAssignmentAtUtc).ThenByDescending(r => r.Id).ToListAsync(ct);
        var activeIds = activeRoutes.Select(r => r.Id).ToArray();
        var routeOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.DeliveryRouteId.HasValue && activeIds.Contains(o.DeliveryRouteId.Value) && o.Id != tracked.Id)
            .ToListAsync(ct);
        foreach (var previous in activeRoutes)
        {
            var members = routeOrders.Where(o => o.DeliveryRouteId == previous.Id).ToList();
            if (members.Count > 0 && members.All(o => IsTerminal(o.Status)))
                await CloseTerminalAdministrativelyAsync(previous, members, "new_assignment", ct);
        }
        var route = activeRoutes.FirstOrDefault(r => (r.Status == DeliveryRouteStatus.Open || r.Status == DeliveryRouteStatus.InProgress)
            && routeOrders.Any(o => o.DeliveryRouteId == r.Id && o.Status == OrderStatus.OnTheWay));
        if (route is null)
        {
            route = new DeliveryRoute
            {
                DeliverymanId = deliverymanId, BranchId = tracked.BranchId, WorkSessionId = session?.Id,
                Status = DeliveryRouteStatus.Open, LastAssignmentAtUtc = now,
                PerOrderBufferSeconds = _opt.PerOrderBufferSeconds,
                ComplexAccessBufferSeconds = _opt.ComplexAccessBufferSeconds
            };
            _db.DeliveryRoutes.Add(route);
            await _db.SaveChangesAsync(ct);
        }
        route.WorkSessionId ??= session?.Id;
        var maxSequence = await _db.DeliveryRouteStops.Where(s => s.DeliveryRouteId == route.Id)
            .Select(s => (int?)s.StopSequence).MaxAsync(ct) ?? 0;
        _db.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            DeliveryRouteId = route.Id, OrderId = tracked.Id, StopSequence = maxSequence + 1,
            AddressSnapshotText = BuildAddressSnapshot(tracked)
        });
        tracked.DeliveryRouteId = route.Id;
        route.LastAssignmentAtUtc = now;
        route.PerOrderBufferSeconds = _opt.PerOrderBufferSeconds;
        route.ComplexAccessBufferSeconds = _opt.ComplexAccessBufferSeconds;
        await _db.SaveChangesAsync(ct);
        if (route.Status == DeliveryRouteStatus.Open) _consolidationSignal?.Pulse();
        if (route.Status == DeliveryRouteStatus.InProgress)
        {
            try
            {
                var stops = await _db.DeliveryRouteStops.Where(s => s.DeliveryRouteId == route.Id)
                    .OrderBy(s => s.StopSequence).ToListAsync(ct);
                var orders = await LoadOrdersAsync(stops, ct);
                await ApplyRoutePlanningCoreAsync(route, stops, orders, ct, preserveRouteStartedAt: true);
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { _logger.LogWarning(ex, "Pedido {OrderId} asignado; no se pudieron recalcular métricas de ruta {RouteId}", tracked.Id, route.Id); }
        }
        foreach (var id in oldRouteIds.Where(id => id != route.Id)) await PruneEmptyOpenRouteAsync(id, ct);
    }

    private async Task CloseTerminalAdministrativelyAsync(DeliveryRoute route, IReadOnlyList<Order> orders,
        string reason, CancellationToken ct)
    {
        if (route.CompletedAtUtc.HasValue || orders.Count == 0 || orders.Any(o => !IsTerminal(o.Status))) return;
        var now = _clock.UtcNow;
        var priorStatus = route.Status;
        var completedByNextRoute = reason == "new_assignment" && DeliveryRouteCompletion.Complete(route, orders, now);
        _db.EntityAuditLogs.Add(new EntityAuditLog
        {
            TenantId = route.TenantId, BranchId = route.BranchId, EntityType = "delivery_route",
            EntityId = route.Id, OperationType = "administrative_closure", ChangedAt = now,
            BusinessDate = now.AddHours(-5).Date,
            SummaryText = $"Ruta #{route.Id} retirada del conjunto activo: {reason}",
            MetadataJson = JsonSerializer.Serialize(new { reason, priorStatus = priorStatus.ToString(),
                route.CompletedAtUtc, route.ActualDurationSeconds, route.MetSla, physicalReturnKnown = false, completedByNextRoute })
        });
        if (!completedByNextRoute)
        {
            route.Status = orders.All(o => o.Status == OrderStatus.Cancelled) ? DeliveryRouteStatus.Cancelled : DeliveryRouteStatus.Completed;
            route.CompletedAtUtc = null;
            route.ActualDurationSeconds = null;
            route.MetSla = null;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static bool IsTerminal(OrderStatus status) => status is OrderStatus.Delivered or OrderStatus.Cancelled;

    public async Task OnOrderUnassignedAsync(int orderId, CancellationToken ct = default)
    {
        var stops = await _db.DeliveryRouteStops.Where(s => s.OrderId == orderId).ToListAsync(ct);
        var routeIds = stops.Select(s => s.DeliveryRouteId).Distinct().ToList();
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (stops.Count > 0) _db.DeliveryRouteStops.RemoveRange(stops);
        if (order is not null) order.DeliveryRouteId = null;
        if (stops.Count == 0 && order is null) return;
        await _db.SaveChangesAsync(ct);
        foreach (var id in routeIds) await PruneEmptyOpenRouteAsync(id, ct);
    }

    public async Task OnOrderCancelledWhileRouteOpenAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order?.DeliveryRouteId is not int routeId) return;
        var route = await _db.DeliveryRoutes.FirstOrDefaultAsync(r => r.Id == routeId, ct);
        if (route?.Status != DeliveryRouteStatus.Open) return;
        var stop = await _db.DeliveryRouteStops.FirstOrDefaultAsync(s => s.OrderId == orderId, ct);
        if (stop is null) return;
        _db.DeliveryRouteStops.Remove(stop);
        order.DeliveryRouteId = null;
        await _db.SaveChangesAsync(ct);
        await PruneEmptyOpenRouteAsync(routeId, ct);
    }

    public async Task TryCompleteInProgressRouteAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order?.DeliveryRouteId is not int routeId) return;
        var route = await _db.DeliveryRoutes.Include(r => r.Stops).FirstOrDefaultAsync(r => r.Id == routeId, ct);
        if (route?.Status != DeliveryRouteStatus.InProgress) return;
        var orders = (await LoadOrdersAsync(route.Stops, ct)).Values.ToList();
        if (orders.Count == 0 || orders.Any(o => !IsTerminal(o.Status))) return;
        if (orders.All(o => o.Status == OrderStatus.Cancelled))
            await CloseTerminalAdministrativelyAsync(route, orders, "all_cancelled", ct);
        else
        {
            // A terminal trip can still be returning; GPS verifies its actual end.
            await TrySetReturnToBranchMetersAsync(route, orders, ct);
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task TrySetReturnToBranchMetersAsync(DeliveryRoute route, IReadOnlyList<Order> orders, CancellationToken ct)
    {
        if (route.ReturnToBranchMeters is > 0) return;
        var branch = await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == route.BranchId, ct);
        if (branch?.Latitude is null || branch.Longitude is null) return;
        var last = orders.Where(o => o.Status == OrderStatus.Delivered)
            .Select(o => (Order: o, Time: o.GetStatusTimes().GetValueOrDefault("delivered")))
            .OrderByDescending(o => o.Time).FirstOrDefault().Order;
        if (last?.Address?.Latitude is null || last.Address.Longitude is null) return;
        route.ReturnToBranchMeters = (int)Math.Round(Math.Max(0, GeoHelper.HaversineDistanceMeters(
            (double)last.Address.Latitude, (double)last.Address.Longitude,
            (double)branch.Latitude, (double)branch.Longitude)));
    }

    public async Task TryFinalizeRouteWhenAllTerminalAsync(int orderId, int? routeIdIfOrderUnlinked = null,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        var id = order?.DeliveryRouteId ?? routeIdIfOrderUnlinked;
        if (!id.HasValue) return;
        var route = await _db.DeliveryRoutes.Include(r => r.Stops).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (route is null || route.Status is DeliveryRouteStatus.Completed or DeliveryRouteStatus.Cancelled || route.Stops.Count == 0) return;
        var orders = (await LoadOrdersAsync(route.Stops, cancellationToken)).Values.ToList();
        if (orders.Count != route.Stops.Count || orders.Any(o => !IsTerminal(o.Status))) return;
        if (route.Status == DeliveryRouteStatus.Open)
            await CompleteOpenRouteWithFullMetaAsync(route, orders, cancellationToken);
        else await TryCompleteInProgressRouteAsync(orderId, cancellationToken);
    }

    public Task<bool> DeliverymanHasPendingOrdersOnActiveRouteAsync(int deliverymanId, int branchId,
        CancellationToken cancellationToken = default, IReadOnlyCollection<int>? excludeOrderIds = null) =>
        _db.Orders.AsNoTracking().AnyAsync(o => o.DeliveryManId == deliverymanId && o.BranchId == branchId
            && o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled
            && (excludeOrderIds == null || !excludeOrderIds.Contains(o.Id)), cancellationToken);

    private IQueryable<DeliveryRoute> ConsolidatableRoutes()
    {
        var now = _clock.UtcNow;
        var start = ColombiaTimeHelper.GetTodayStartInUtcFromUtc(now);
        return _db.DeliveryRoutes.Where(r => r.Status == DeliveryRouteStatus.Open && r.Stops.Any()
            && (r.WorkSessionId == null && r.LastAssignmentAtUtc >= start && r.LastAssignmentAtUtc <= now
                || r.WorkSessionId.HasValue && _db.DeliveryWorkSessions.Any(s => s.Id == r.WorkSessionId
                    && s.DeliverymanId == r.DeliverymanId && s.BranchId == r.BranchId
                    && s.Status == DeliveryWorkSessionStatus.Active && s.StartedAt <= now && s.AutoCloseAt > now)));
    }

    public async Task<int> ConsolidatePendingRoutesAsync(CancellationToken ct = default)
    {
        var cutoff = _clock.UtcNow.AddSeconds(-Math.Max(0, _opt.ConsolidationDelaySeconds));
        var routes = await ConsolidatableRoutes().Include(r => r.Stops).Where(r => r.LastAssignmentAtUtc <= cutoff).ToListAsync(ct);
        var count = 0;
        foreach (var route in routes)
        {
            var orders = await LoadOrdersAsync(route.Stops, ct);
            if (orders.Count != route.Stops.Count) continue;
            if (orders.Values.All(o => IsTerminal(o.Status)))
                await CompleteOpenRouteWithFullMetaAsync(route, orders.Values.ToList(), ct);
            else
            {
                // Delivered members remain part of history; never unlink them just to plan remaining work.
                await ApplyRoutePlanningCoreAsync(route, route.Stops.OrderBy(s => s.StopSequence).ToList(), orders, ct);
                route.ConsolidatedAtUtc = _clock.UtcNow;
                route.Status = DeliveryRouteStatus.InProgress;
                await _db.SaveChangesAsync(ct);
            }
            count++;
        }
        return count;
    }

    public async Task<DateTime?> GetNextPendingConsolidationAtAsync(CancellationToken cancellationToken = default)
    {
        var assigned = await ConsolidatableRoutes().AsNoTracking().OrderBy(r => r.LastAssignmentAtUtc)
            .Select(r => (DateTime?)r.LastAssignmentAtUtc).FirstOrDefaultAsync(cancellationToken);
        return assigned?.AddSeconds(Math.Max(0, _opt.ConsolidationDelaySeconds));
    }

    private Task<Dictionary<int, Order>> LoadOrdersAsync(IEnumerable<DeliveryRouteStop> stops, CancellationToken ct)
    {
        var ids = stops.Select(s => s.OrderId).ToArray();
        return _db.Orders.Include(o => o.Address).Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
    }

    private async Task ApplyRoutePlanningCoreAsync(DeliveryRoute route, List<DeliveryRouteStop> planningStops,
        Dictionary<int, Order> orders, CancellationToken ct, bool preserveRouteStartedAt = false)
    {
        var keywords = ComplexAccessKeywordEvaluator.ParseKeywords(_opt.ComplexAccessKeywords);
        var complex = 0;
        foreach (var stop in planningStops)
        {
            var text = stop.AddressSnapshotText ?? BuildAddressSnapshot(orders[stop.OrderId]);
            stop.AddressSnapshotText = text;
            var (matches, term) = ComplexAccessKeywordEvaluator.Evaluate(text, keywords);
            stop.RequiresComplexAccessBuffer = matches;
            stop.ComplexAccessMatchTerm = term;
            stop.ComplexAccessBonusSeconds = matches ? _opt.ComplexAccessBufferSeconds : 0;
            if (matches) complex++;
        }
        var branch = await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == route.BranchId, ct);
        var hasBranch = branch?.Latitude is not null && branch.Longitude is not null;
        var warnings = new List<string>();
        if (!hasBranch) warnings.Add("La sucursal no tiene coordenadas configuradas; la meta no incluye el tramo desde el local.");
        var missing = planningStops.Where(s => orders[s.OrderId].Address?.Latitude is null
            || orders[s.OrderId].Address?.Longitude is null).Select(s => s.OrderId).ToArray();
        if (missing.Length > 0) warnings.Add($"Pedidos sin coordenadas (#{string.Join(", ", missing)}). Ubica el pin antes de calcular la ruta.");
        var points = new List<(double Latitude, double Longitude)>();
        if (hasBranch) points.Add(((double)branch!.Latitude!.Value, (double)branch.Longitude!.Value));
        foreach (var stop in planningStops)
            if (orders[stop.OrderId].Address is { Latitude: decimal lat, Longitude: decimal lng })
                points.Add(((double)lat, (double)lng));
        if (hasBranch && missing.Length == 0 && points.Count >= 2) points.Add(points[0]);
        var distance = 0;
        var duration = 0;
        if (missing.Length == 0 && points.Count >= 2)
        {
            var metrics = await _routesMetrics.ComputeRouteAsync(points, ct);
            distance = Math.Max(0, metrics.DistanceMeters - metrics.ReturnDistanceMeters);
            duration = metrics.DurationSeconds;
            route.ReturnToBranchMeters = metrics.ReturnDistanceMeters;
            if (distance == 0 && duration == 0) warnings.Add("Google no devolvió métricas; la meta usa únicamente márgenes por pedido.");
        }
        else _logger.LogWarning("Ruta {RouteId}: planificación sin Google por coordenadas incompletas", route.Id);
        route.PlannedDistanceMeters = distance;
        route.PlannedDrivingDurationSeconds = duration;
        route.StopCount = planningStops.Count;
        route.ComplexAccessStopCount = complex;
        route.MetaDurationSeconds = duration + planningStops.Count * _opt.PerOrderBufferSeconds + complex * _opt.ComplexAccessBufferSeconds;
        route.PerOrderBufferSeconds = _opt.PerOrderBufferSeconds;
        route.ComplexAccessBufferSeconds = _opt.ComplexAccessBufferSeconds;
        route.PlanningWarnings = warnings.Count == 0 ? null : string.Join('\n', warnings);
        if (!preserveRouteStartedAt) route.RouteStartedAtUtc = route.LastAssignmentAtUtc.AddSeconds(_opt.ConsolidationDelaySeconds);
    }

    private async Task CompleteOpenRouteWithFullMetaAsync(DeliveryRoute route, List<Order> orders, CancellationToken ct)
    {
        if (route.Stops.Count == 0 || orders.Count != route.Stops.Count || orders.Any(o => !IsTerminal(o.Status))) return;
        await ApplyRoutePlanningCoreAsync(route, route.Stops.OrderBy(s => s.StopSequence).ToList(), orders.ToDictionary(o => o.Id), ct);
        route.ConsolidatedAtUtc = _clock.UtcNow;
        if (orders.All(o => o.Status == OrderStatus.Cancelled))
            await CloseTerminalAdministrativelyAsync(route, orders, "all_cancelled", ct);
        else
        {
            route.Status = DeliveryRouteStatus.InProgress;
            await TrySetReturnToBranchMetersAsync(route, orders, ct);
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task PruneEmptyOpenRouteAsync(int routeId, CancellationToken ct)
    {
        var route = await _db.DeliveryRoutes.Include(r => r.Stops).FirstOrDefaultAsync(r => r.Id == routeId, ct);
        if (route is null || route.Status != DeliveryRouteStatus.Open || route.Stops.Count > 0) return;
        // Empty, never-dispatched drafts have no journey to preserve.
        if (route.DepartedAtUtc is null) _db.DeliveryRoutes.Remove(route);
        else route.Status = DeliveryRouteStatus.Cancelled;
        await _db.SaveChangesAsync(ct);
    }

    private static string? BuildAddressSnapshot(Order order)
    {
        if (order.Address is null) return order.Notes;
        var text = string.Join(" — ", new[] { order.Address.AddressText, order.Address.AdditionalInfo }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(text) ? order.Notes : text;
    }
}

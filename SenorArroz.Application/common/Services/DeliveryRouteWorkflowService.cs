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

    public DeliveryRouteWorkflowService(
        IApplicationDbContext db,
        IGoogleRoutesDrivingMetricsService routesMetrics,
        IOptions<DeliveryRouteOptions> options,
        ILogger<DeliveryRouteWorkflowService> logger,
        IClock clock)
    {
        _db = db;
        _routesMetrics = routesMetrics;
        _opt = options.Value;
        _logger = logger;
        _clock = clock;
    }

    public async Task OnOrderAssignedToDeliverymanAsync(Order order, CancellationToken cancellationToken = default)
    {
        if ((order.Type != OrderType.Delivery
             && !(order.Type == OrderType.Reservation && order.AddressId.HasValue))
            || order.DeliveryManId is null)
            return;

        var tracked = await _db.Orders
            .Include(o => o.Address)
            .FirstOrDefaultAsync(o => o.Id == order.Id, cancellationToken);
        if (tracked is null || tracked.DeliveryManId is null)
            return;

        var oldRouteIds = await _db.DeliveryRouteStops
            .AsNoTracking()
            .Where(s => s.OrderId == tracked.Id)
            .Select(s => s.DeliveryRouteId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var oldStops = await _db.DeliveryRouteStops
            .Where(s => s.OrderId == tracked.Id)
            .ToListAsync(cancellationToken);
        _db.DeliveryRouteStops.RemoveRange(oldStops);
        tracked.DeliveryRouteId = null;
        var colombiaTodayStartUtc = ColombiaTimeHelper.GetTodayStartInUtcFromUtc(_clock.UtcNow);

        var activeRoutes = await _db.DeliveryRoutes
            .Where(r =>
                    r.DeliverymanId == tracked.DeliveryManId.Value
                    && r.BranchId == tracked.BranchId
                    && ((r.Status == DeliveryRouteStatus.InProgress
                         && r.RouteStartedAtUtc >= colombiaTodayStartUtc)
                        || (r.Status == DeliveryRouteStatus.Open
                            && r.LastAssignmentAtUtc >= colombiaTodayStartUtc)))
            .OrderByDescending(r => r.Status == DeliveryRouteStatus.InProgress)
            .ThenByDescending(r => r.LastAssignmentAtUtc)
            .ToListAsync(cancellationToken);

        var activeRouteIds = activeRoutes.Select(r => r.Id).ToList();
        var routeOrderStates = activeRouteIds.Count == 0
            ? []
            : await _db.DeliveryRouteStops
                .AsNoTracking()
                .Where(s => activeRouteIds.Contains(s.DeliveryRouteId)
                            && s.OrderId != tracked.Id)
                .Join(
                    _db.Orders.AsNoTracking(),
                    stop => stop.OrderId,
                    routeOrder => routeOrder.Id,
                    (stop, routeOrder) => new RouteOrderState(
                        stop.DeliveryRouteId,
                        routeOrder.Status))
                .ToListAsync(cancellationToken);

        var statesByRoute = routeOrderStates
            .GroupBy(x => x.RouteId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Status).ToList());

        foreach (var terminalRoute in activeRoutes.Where(r =>
                     statesByRoute.TryGetValue(r.Id, out var states)
                     && states.Count > 0
                     && states.All(IsTerminalOrderStatus)))
        {
            await FinalizeTerminalRouteForNewAssignmentAsync(
                terminalRoute,
                tracked.Id,
                cancellationToken);
        }

        var route = activeRoutes.FirstOrDefault(r =>
            statesByRoute.TryGetValue(r.Id, out var states)
            && states.Any(status => status == OrderStatus.OnTheWay));

        if (route is null)
        {
            route = new DeliveryRoute
            {
                DeliverymanId = tracked.DeliveryManId.Value,
                BranchId = tracked.BranchId,
                Status = DeliveryRouteStatus.Open,
                LastAssignmentAtUtc = _clock.UtcNow,
                PerOrderBufferSeconds = _opt.PerOrderBufferSeconds,
                ComplexAccessBufferSeconds = _opt.ComplexAccessBufferSeconds,
            };
            _db.DeliveryRoutes.Add(route);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var maxSeq = await _db.DeliveryRouteStops
            .Where(s => s.DeliveryRouteId == route.Id)
            .Select(s => (int?)s.StopSequence)
            .MaxAsync(cancellationToken) ?? 0;

        var snapshot = BuildAddressSnapshot(tracked);
        _db.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            DeliveryRouteId = route.Id,
            OrderId = tracked.Id,
            StopSequence = maxSeq + 1,
            AddressSnapshotText = snapshot,
        });
        tracked.DeliveryRouteId = route.Id;
        route.LastAssignmentAtUtc = _clock.UtcNow;
        route.PerOrderBufferSeconds = _opt.PerOrderBufferSeconds;
        route.ComplexAccessBufferSeconds = _opt.ComplexAccessBufferSeconds;

        await _db.SaveChangesAsync(cancellationToken);

        // Una asignacion urgente debe incorporarse a la ruta que ya esta en camino.
        // Se recalculan sus metricas, pero nunca se reinicia el reloj operativo.
        if (route.Status == DeliveryRouteStatus.InProgress)
        {
            try
            {
                var routeStops = await _db.DeliveryRouteStops
                    .Where(s => s.DeliveryRouteId == route.Id)
                    .OrderBy(s => s.StopSequence)
                    .ToListAsync(cancellationToken);
                var routeOrderIds = routeStops.Select(s => s.OrderId).ToList();
                var routeOrders = await _db.Orders
                    .Include(o => o.Address)
                    .Where(o => routeOrderIds.Contains(o.Id))
                    .ToListAsync(cancellationToken);

                await ApplyRoutePlanningCoreAsync(
                    route,
                    routeStops,
                    routeOrders.ToDictionary(o => o.Id),
                    cancellationToken,
                    preserveRouteStartedAt: true);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "El pedido {OrderId} se agrego a la ruta activa {RouteId}, pero no fue posible recalcular sus metricas.",
                    tracked.Id,
                    route.Id);
            }
        }

        foreach (var rid in oldRouteIds.Where(id => id != route.Id))
            await PruneEmptyOpenRouteAsync(rid, cancellationToken);
    }

    private async Task FinalizeTerminalRouteForNewAssignmentAsync(
        DeliveryRoute route,
        int assignedOrderId,
        CancellationToken cancellationToken)
    {
        var stopList = await _db.DeliveryRouteStops
            .Where(s => s.DeliveryRouteId == route.Id && s.OrderId != assignedOrderId)
            .OrderBy(s => s.StopSequence)
            .ToListAsync(cancellationToken);
        if (stopList.Count == 0)
            return;

        var orderIds = stopList.Select(s => s.OrderId).ToList();
        var orders = await _db.Orders
            .Include(o => o.Address)
            .Where(o => orderIds.Contains(o.Id))
            .ToListAsync(cancellationToken);
        if (orders.Count != orderIds.Count || orders.Any(o => !IsTerminalOrderStatus(o.Status)))
            return;

        var completedAtUtc = _clock.UtcNow;
        if (orders.All(o => o.Status == OrderStatus.Cancelled))
        {
            route.Status = DeliveryRouteStatus.Cancelled;
            route.CompletedAtUtc = completedAtUtc;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (route.Status == DeliveryRouteStatus.Open)
        {
            try
            {
                await ApplyRoutePlanningCoreAsync(
                    route,
                    stopList,
                    orders.ToDictionary(o => o.Id),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                route.RouteStartedAtUtc ??=
                    route.LastAssignmentAtUtc.AddSeconds(_opt.ConsolidationDelaySeconds);
                _logger.LogWarning(
                    ex,
                    "No fue posible calcular las metricas de la ruta terminal {RouteId} antes de iniciar una nueva.",
                    route.Id);
            }

            route.ConsolidatedAtUtc = completedAtUtc;
        }

        await TrySetReturnToBranchMetersAsync(route, orders, cancellationToken);

        route.CompletedAtUtc = completedAtUtc;
        if (route.RouteStartedAtUtc.HasValue)
        {
            route.ActualDurationSeconds = (int)Math.Max(
                0,
                (completedAtUtc - route.RouteStartedAtUtc.Value).TotalSeconds);
            route.MetSla = route.MetaDurationSeconds.HasValue
                ? route.ActualDurationSeconds <= route.MetaDurationSeconds.Value
                : null;
        }

        route.Status = DeliveryRouteStatus.Completed;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsTerminalOrderStatus(OrderStatus status) =>
        status is OrderStatus.Delivered or OrderStatus.Cancelled;

    private sealed record RouteOrderState(int RouteId, OrderStatus Status);

    public async Task OnOrderUnassignedAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var stops = await _db.DeliveryRouteStops
            .Where(s => s.OrderId == orderId)
            .ToListAsync(cancellationToken);
        var routeIds = stops.Select(s => s.DeliveryRouteId).Distinct().ToList();

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (stops.Count > 0)
            _db.DeliveryRouteStops.RemoveRange(stops);

        if (order is not null && order.DeliveryRouteId.HasValue)
            order.DeliveryRouteId = null;

        if (stops.Count == 0 && order is null)
            return;

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var rid in routeIds)
            await PruneEmptyOpenRouteAsync(rid, cancellationToken);
    }

    public async Task OnOrderCancelledWhileRouteOpenAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order?.DeliveryRouteId is not int routeId)
            return;

        var route = await _db.DeliveryRoutes.FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken);
        if (route?.Status != DeliveryRouteStatus.Open)
            return;

        var stop = await _db.DeliveryRouteStops
            .FirstOrDefaultAsync(s => s.OrderId == orderId, cancellationToken);
        if (stop is null)
            return;

        _db.DeliveryRouteStops.Remove(stop);
        order.DeliveryRouteId = null;
        await _db.SaveChangesAsync(cancellationToken);
        await PruneEmptyOpenRouteAsync(routeId, cancellationToken);
    }

    public async Task TryCompleteInProgressRouteAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order?.DeliveryRouteId is not int routeId)
            return;

        var route = await _db.DeliveryRoutes
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken);
        if (route is null || route.Status != DeliveryRouteStatus.InProgress)
            return;

        var ids = route.Stops.Select(s => s.OrderId).ToList();
        var orders = await _db.Orders
            .Include(o => o.Address)
            .Where(o => ids.Contains(o.Id))
            .ToListAsync(cancellationToken);

        var allTerminal = orders.Count > 0 && orders.All(o =>
            o.Status == OrderStatus.Delivered || o.Status == OrderStatus.Cancelled);

        if (!allTerminal)
            return;

        if (orders.All(o => o.Status == OrderStatus.Cancelled))
        {
            route.Status = DeliveryRouteStatus.Cancelled;
            route.CompletedAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Todos los pedidos terminaron, pero la ruta continua activa durante el
        // regreso. Se completa con el primer punto GPS dentro de la sucursal.
        await TrySetReturnToBranchMetersAsync(route, orders, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task TrySetReturnToBranchMetersAsync(
        DeliveryRoute route,
        IReadOnlyList<Order> routeOrders,
        CancellationToken cancellationToken)
    {
        if (route.ReturnToBranchMeters is > 0)
            return;

        var branch = await _db.Branches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == route.BranchId, cancellationToken);
        if (branch?.Latitude is null || branch.Longitude is null)
            return;

        Order? lastDelivered = null;
        DateTime? maxDelivered = null;
        foreach (var o in routeOrders.Where(x => x.Status == OrderStatus.Delivered))
        {
            var st = o.GetStatusTimes();
            if (!st.TryGetValue("delivered", out var dt))
                continue;
            if (maxDelivered is null || dt > maxDelivered.Value)
            {
                maxDelivered = dt;
                lastDelivered = o;
            }
        }

        if (lastDelivered?.Address?.Latitude is null || lastDelivered.Address.Longitude is null)
            return;

        var m = GeoHelper.HaversineDistanceMeters(
            (double)lastDelivered.Address.Latitude.Value,
            (double)lastDelivered.Address.Longitude.Value,
            (double)branch.Latitude.Value,
            (double)branch.Longitude.Value);
        route.ReturnToBranchMeters = (int)Math.Round(Math.Max(0, m));
    }

    public async Task TryFinalizeRouteWhenAllTerminalAsync(
        int orderId,
        int? routeIdIfOrderUnlinked = null,
        CancellationToken cancellationToken = default)
    {
        var orderRow = await _db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        var routeId = orderRow?.DeliveryRouteId ?? routeIdIfOrderUnlinked;
        if (routeId is not int rid)
            return;

        var route = await _db.DeliveryRoutes
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.Id == rid, cancellationToken);
        if (route is null || route.Status == DeliveryRouteStatus.Completed)
            return;

        var ids = route.Stops.Select(s => s.OrderId).ToList();
        if (ids.Count == 0)
            return;

        var orders = await _db.Orders
            .Include(o => o.Address)
            .Where(o => ids.Contains(o.Id))
            .ToListAsync(cancellationToken);

        var allTerminal = orders.Count > 0 && orders.All(o =>
            o.Status == OrderStatus.Delivered || o.Status == OrderStatus.Cancelled);
        if (!allTerminal)
            return;

        if (route.Status == DeliveryRouteStatus.Open)
            await CompleteOpenRouteWithFullMetaAsync(route, orders, cancellationToken);
        else if (route.Status == DeliveryRouteStatus.InProgress)
            await TryCompleteInProgressRouteAsync(orderId, cancellationToken);
    }

    public async Task<bool> DeliverymanHasPendingOrdersOnActiveRouteAsync(
        int deliverymanId,
        int branchId,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? excludeOrderIds = null)
    {
        var q =
            from r in _db.DeliveryRoutes
            join s in _db.DeliveryRouteStops on r.Id equals s.DeliveryRouteId
            join o in _db.Orders on s.OrderId equals o.Id
            where r.DeliverymanId == deliverymanId
                  && r.BranchId == branchId
                  && (r.Status == DeliveryRouteStatus.Open || r.Status == DeliveryRouteStatus.InProgress)
                  && o.Status != OrderStatus.Delivered
                  && o.Status != OrderStatus.Cancelled
                  && (excludeOrderIds == null || !excludeOrderIds.Contains(o.Id))
            select o.Id;

        return await q.AnyAsync(cancellationToken);
    }

    public async Task<int> ConsolidatePendingRoutesAsync(CancellationToken cancellationToken = default)
    {
        var delay = TimeSpan.FromSeconds(Math.Max(0, _opt.ConsolidationDelaySeconds));
        var cutoff = _clock.UtcNow - delay;

        var routes = await _db.DeliveryRoutes
            .Include(r => r.Stops)
            .Where(r => r.Status == DeliveryRouteStatus.Open
                        && r.LastAssignmentAtUtc <= cutoff
                        && r.Stops.Any())
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var route in routes)
        {
            var stopList = route.Stops.OrderBy(s => s.StopSequence).ToList();
            var orderIds = stopList.Select(s => s.OrderId).ToList();
            var ordersSnapshot = await _db.Orders
                .AsNoTracking()
                .Where(o => orderIds.Contains(o.Id))
                .ToListAsync(cancellationToken);

            var allTerminal = ordersSnapshot.Count > 0 && ordersSnapshot.All(o =>
                o.Status == OrderStatus.Delivered || o.Status == OrderStatus.Cancelled);

            if (allTerminal)
            {
                var ordersTracked = await _db.Orders
                    .Include(o => o.Address)
                    .Where(o => orderIds.Contains(o.Id))
                    .ToListAsync(cancellationToken);
                await CompleteOpenRouteWithFullMetaAsync(route, ordersTracked, cancellationToken);
                count++;
                continue;
            }

            if (await TryConsolidateRouteAsync(route, cancellationToken))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Google + buffers + meta; actualiza paradas. No cambia Status ni ConsolidatedAt.
    /// </summary>
    private async Task ApplyRoutePlanningCoreAsync(
        DeliveryRoute route,
        List<DeliveryRouteStop> planningStops,
        Dictionary<int, Order> dict,
        CancellationToken cancellationToken,
        bool preserveRouteStartedAt = false)
    {
        var keywords = ComplexAccessKeywordEvaluator.ParseKeywords(_opt.ComplexAccessKeywords);
        var complexBuffer = _opt.ComplexAccessBufferSeconds;
        var k = 0;
        foreach (var stop in planningStops)
        {
            var ord = dict[stop.OrderId];
            var text = stop.AddressSnapshotText ?? BuildAddressSnapshot(ord);
            stop.AddressSnapshotText = text;
            var (matches, term) = ComplexAccessKeywordEvaluator.Evaluate(text, keywords);
            stop.RequiresComplexAccessBuffer = matches;
            stop.ComplexAccessMatchTerm = term;
            stop.ComplexAccessBonusSeconds = matches ? complexBuffer : 0;
            if (matches) k++;
        }

        var branch = await _db.Branches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == route.BranchId, cancellationToken);
        var branchHasCoords = branch?.Latitude is not null && branch?.Longitude is not null;

        var warnings = new List<string>();
        if (!branchHasCoords)
        {
            warnings.Add(
                "La sucursal no tiene coordenadas configuradas; el tiempo y la distancia en ruta no incluyen el tramo desde el local. Configúralas en la ficha de la sucursal.");
        }

        var stopsMissingCoords = planningStops
            .Where(s =>
            {
                var o = dict[s.OrderId];
                return o.Address?.Latitude is null || o.Address.Longitude is null;
            })
            .ToList();

        if (stopsMissingCoords.Count > 0)
        {
            var ids = string.Join(", ", stopsMissingCoords.Select(s => s.OrderId));
            warnings.Add(
                $"Uno o más pedidos no tienen coordenadas en la dirección (#{ids}). Abre cada pedido y ubica el pin en el mapa. Hasta entonces la meta solo usa márgenes por pedido.");
        }

        var waypoints = new List<(double Lat, double Lng)>();
        if (branchHasCoords)
        {
            waypoints.Add(((double)branch!.Latitude!.Value, (double)branch.Longitude!.Value));
        }

        foreach (var stop in planningStops)
        {
            var ord = dict[stop.OrderId];
            if (ord.Address?.Latitude is decimal lat && ord.Address.Longitude is decimal lng)
                waypoints.Add(((double)lat, (double)lng));
        }

        // Una sola consulta representa la ruta completa y conserva el orden:
        // sucursal -> entregas -> sucursal.
        if (branchHasCoords && stopsMissingCoords.Count == 0 && waypoints.Count >= 2)
            waypoints.Add(((double)branch!.Latitude!.Value, (double)branch.Longitude!.Value));

        var n = planningStops.Count;
        var perOrder = _opt.PerOrderBufferSeconds;
        int distMeters = 0;
        int driveSecs = 0;

        var skipGoogle = stopsMissingCoords.Count > 0 || waypoints.Count < 2;
        if (skipGoogle)
        {
            if (stopsMissingCoords.Count == 0 && waypoints.Count < 2)
            {
                warnings.Add(
                    "No hay suficientes puntos con coordenadas para calcular la ruta en mapa. La meta usa solo los márgenes por pedido.");
            }

            _logger.LogWarning(
                "Ruta {RouteId}: planificación sin Google (paradas sin coords o menos de 2 waypoints). Stops={N}, waypoints={W}",
                route.Id, n, waypoints.Count);
        }
        else
        {
            var metrics = await _routesMetrics.ComputeRouteAsync(waypoints, cancellationToken);
            distMeters = Math.Max(0, metrics.DistanceMeters - metrics.ReturnDistanceMeters);
            driveSecs = metrics.DurationSeconds;
            route.ReturnToBranchMeters = metrics.ReturnDistanceMeters;
            if (distMeters == 0 && driveSecs == 0)
            {
                warnings.Add(
                    "No se obtuvo tiempo ni distancia desde Google Maps (revisa la clave de API o la red). La meta usa solo los márgenes por pedido.");
                _logger.LogWarning("Ruta {RouteId}: Google Routes devolvió distancia y duración en cero.", route.Id);
            }
        }

        var meta = driveSecs + n * perOrder + k * complexBuffer;

        route.PlannedDistanceMeters = distMeters;
        route.PlannedDrivingDurationSeconds = driveSecs;
        route.StopCount = n;
        route.ComplexAccessStopCount = k;
        route.MetaDurationSeconds = meta;
        route.PerOrderBufferSeconds = perOrder;
        route.ComplexAccessBufferSeconds = complexBuffer;
        route.PlanningWarnings = warnings.Count > 0 ? string.Join('\n', warnings) : null;
        if (!preserveRouteStartedAt)
            route.RouteStartedAtUtc = route.LastAssignmentAtUtc.AddSeconds(_opt.ConsolidationDelaySeconds);
    }

    private async Task CompleteOpenRouteWithFullMetaAsync(
        DeliveryRoute route,
        List<Order> orders,
        CancellationToken cancellationToken)
    {
        var stopList = route.Stops.OrderBy(s => s.StopSequence).ToList();
        var dict = orders.ToDictionary(o => o.Id);
        if (stopList.Count == 0)
            return;
        if (stopList.Any(s =>
                !dict.TryGetValue(s.OrderId, out var o)
                || (o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled)))
            return;

        await ApplyRoutePlanningCoreAsync(route, stopList, dict, cancellationToken);

        route.ConsolidatedAtUtc = _clock.UtcNow;
        route.Status = orders.All(o => o.Status == OrderStatus.Cancelled)
            ? DeliveryRouteStatus.Cancelled
            : DeliveryRouteStatus.InProgress;

        await TrySetReturnToBranchMetersAsync(route, orders, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> TryConsolidateRouteAsync(DeliveryRoute route, CancellationToken cancellationToken)
    {
        var stopList = route.Stops.OrderBy(s => s.StopSequence).ToList();
        var orderIds = stopList.Select(s => s.OrderId).ToList();
        var orders = await _db.Orders
            .Include(o => o.Address)
            .Where(o => orderIds.Contains(o.Id))
            .ToListAsync(cancellationToken);
        var dict = orders.ToDictionary(o => o.Id);

        var stale = stopList.Where(s =>
        {
            if (!dict.TryGetValue(s.OrderId, out var o))
                return true;
            return o.Status is OrderStatus.Delivered or OrderStatus.Cancelled;
        }).ToList();

        foreach (var s in stale)
        {
            if (dict.TryGetValue(s.OrderId, out var o))
                o.DeliveryRouteId = null;
            _db.DeliveryRouteStops.Remove(s);
        }

        await _db.SaveChangesAsync(cancellationToken);

        route = await _db.DeliveryRoutes
            .Include(r => r.Stops)
            .FirstAsync(r => r.Id == route.Id, cancellationToken);

        if (!route.Stops.Any())
        {
            route.Status = DeliveryRouteStatus.Cancelled;
            await _db.SaveChangesAsync(cancellationToken);
            return false;
        }

        stopList = route.Stops.OrderBy(s => s.StopSequence).ToList();
        orderIds = stopList.Select(s => s.OrderId).ToList();
        orders = await _db.Orders
            .Include(o => o.Address)
            .Where(o => orderIds.Contains(o.Id))
            .ToListAsync(cancellationToken);
        dict = orders.ToDictionary(o => o.Id);

        var eligibleStops = stopList.Where(s =>
            dict.TryGetValue(s.OrderId, out var o)
            && o.Status is not (OrderStatus.Delivered or OrderStatus.Cancelled)).ToList();

        if (eligibleStops.Count == 0)
        {
            route.Status = DeliveryRouteStatus.Cancelled;
            foreach (var s in route.Stops.ToList())
            {
                if (dict.TryGetValue(s.OrderId, out var o))
                    o.DeliveryRouteId = null;
                _db.DeliveryRouteStops.Remove(s);
            }
            await _db.SaveChangesAsync(cancellationToken);
            return false;
        }

        await ApplyRoutePlanningCoreAsync(route, eligibleStops, dict, cancellationToken);
        route.ConsolidatedAtUtc = _clock.UtcNow;
        route.Status = DeliveryRouteStatus.InProgress;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task PruneEmptyOpenRouteAsync(int routeId, CancellationToken cancellationToken)
    {
        var route = await _db.DeliveryRoutes
            .Include(r => r.Stops)
            .FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken);
        if (route is null || route.Status != DeliveryRouteStatus.Open || route.Stops.Any())
            return;

        _db.DeliveryRoutes.Remove(route);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string? BuildAddressSnapshot(Order order)
    {
        if (order.Address is null)
            return order.Notes;
        var parts = new[] { order.Address.AddressText, order.Address.AdditionalInfo }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        var t = string.Join(" — ", parts);
        return string.IsNullOrWhiteSpace(t) ? order.Notes : t;
    }
}

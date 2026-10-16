using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Common.Services;

public sealed class DeliveryAutoCompletionService : IDeliveryAutoCompletionService
{
    internal const double MaximumAcceptedAccuracyMeters = 50;
    internal static readonly TimeSpan MaximumLocationAge = TimeSpan.FromMinutes(2);

    private readonly IApplicationDbContext _db;
    private readonly ISender _sender;
    private readonly IClock _clock;
    private readonly ILogger<DeliveryAutoCompletionService> _logger;
    private readonly IDeliveryAutoCompletionRouteLock _routeLock;

    public DeliveryAutoCompletionService(
        IApplicationDbContext db,
        ISender sender,
        IClock clock,
        ILogger<DeliveryAutoCompletionService> logger,
        IDeliveryAutoCompletionRouteLock routeLock)
    {
        _db = db;
        _sender = sender;
        _clock = clock;
        _logger = logger;
        _routeLock = routeLock;
    }

    public async Task EvaluateLocationAsync(
        DeliverymanLocation location,
        CancellationToken cancellationToken = default)
    {
        if (!location.DeliveryRouteId.HasValue)
            return;

        if (location.GpsEnabled == false
            || !location.AccuracyMeters.HasValue
            || location.AccuracyMeters.Value < 0
            || location.AccuracyMeters.Value > MaximumAcceptedAccuracyMeters)
        {
            _logger.LogInformation(
                "AutoDelivery ignored unreliable location accuracy={AccuracyMeters}",
                location.AccuracyMeters);
            return;
        }

        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        var recordedAtUtc = ColombiaTimeHelper.EnsureUtc(location.RecordedAt);
        if ((nowUtc - recordedAtUtc).Duration() > MaximumLocationAge)
        {
            _logger.LogInformation(
                "AutoDelivery skipped stale location location={LocationId} recordedAt={RecordedAt}",
                location.Id,
                recordedAtUtc);
            return;
        }

        var routeId = location.DeliveryRouteId.Value;
        try
        {
            await _routeLock.ExecuteAsync(
                routeId,
                innerCancellationToken => EvaluateLockedAsync(
                    location,
                    recordedAtUtc,
                    nowUtc,
                    routeId,
                    innerCancellationToken),
                cancellationToken);
        }
        catch (BusinessException ex)
        {
            _logger.LogInformation(
                ex,
                "AutoDelivery skipped order state changed route={RouteId} location={LocationId}",
                routeId,
                location.Id);
        }
    }

    private async Task EvaluateLockedAsync(
        DeliverymanLocation location,
        DateTime recordedAtUtc,
        DateTime nowUtc,
        int routeId,
        CancellationToken cancellationToken)
    {
        var settings = await (
                from route in _db.DeliveryRoutes.AsNoTracking()
                join branch in _db.Branches.AsNoTracking() on route.BranchId equals branch.Id
                where route.Id == routeId
                      && route.DeliverymanId == location.DeliverymanId
                      && (route.Status == DeliveryRouteStatus.Open
                          || route.Status == DeliveryRouteStatus.InProgress)
                select new
                {
                    route.BranchId,
                    branch.DeliveryAutoCompleteEnabled,
                    branch.DeliveryAutoCompleteArrivalRadiusMeters,
                    branch.DeliveryAutoCompleteDepartureRadiusMeters,
                    branch.DeliveryAutoCompleteMinPresenceSeconds,
                })
            .FirstOrDefaultAsync(cancellationToken);
        if (settings is null || !settings.DeliveryAutoCompleteEnabled)
            return;

        if (!SettingsAreValid(
                settings.DeliveryAutoCompleteArrivalRadiusMeters,
                settings.DeliveryAutoCompleteDepartureRadiusMeters,
                settings.DeliveryAutoCompleteMinPresenceSeconds))
        {
            _logger.LogWarning(
                "AutoDelivery skipped invalid branch settings branch={BranchId}",
                settings.BranchId);
            return;
        }

        var pendingStops = await (
                from routeStop in _db.DeliveryRouteStops
                join order in _db.Orders on routeStop.OrderId equals order.Id
                join address in _db.Addresses on order.AddressId equals address.Id
                where routeStop.DeliveryRouteId == routeId
                      && routeStop.AutoDeliveredAtUtc == null
                      && order.Type == OrderType.Delivery
                      && order.Status == OrderStatus.OnTheWay
                      && order.DeliveryManId == location.DeliverymanId
                      && order.DeliveryRouteId == routeId
                      && order.BranchId == settings.BranchId
                      && address.Latitude.HasValue
                      && address.Longitude.HasValue
                orderby routeStop.StopSequence, routeStop.Id
                select new PendingStop(
                    routeStop,
                    order.Id,
                    address.Latitude!.Value,
                    address.Longitude!.Value))
            .ToListAsync(cancellationToken);

        var distances = pendingStops
            .Where(x => CoordinatesAreValid(x.Latitude, x.Longitude))
            .Select(x => new StopDistance(
                x,
                GeoHelper.HaversineDistanceMeters(
                    (double)location.Latitude,
                    (double)location.Longitude,
                    (double)x.Latitude,
                    (double)x.Longitude)))
            .ToList();
        if (distances.Count == 0)
            return;

        var active = distances.FirstOrDefault(x => x.Pending.Stop.ArrivalConfirmedAtUtc.HasValue)
                     ?? distances.FirstOrDefault(x => x.Pending.Stop.ArrivalCandidateAtUtc.HasValue);
        if (active is not null)
        {
            await EvaluateActiveStopAsync(
                active,
                location,
                recordedAtUtc,
                nowUtc,
                settings.DeliveryAutoCompleteArrivalRadiusMeters,
                settings.DeliveryAutoCompleteDepartureRadiusMeters,
                settings.DeliveryAutoCompleteMinPresenceSeconds,
                settings.BranchId,
                cancellationToken);
            // The departure point may be the first arrival sample for a
            // subsequent distinct stop, so do not discard it.
            if (active.DistanceMeters >= settings.DeliveryAutoCompleteDepartureRadiusMeters)
            {
                var next = distances.FirstOrDefault(x =>
                    x.Pending.Stop.Id != active.Pending.Stop.Id
                    && !x.Pending.Stop.ArrivalCandidateAtUtc.HasValue
                    && x.DistanceMeters <= settings.DeliveryAutoCompleteArrivalRadiusMeters);
                if (next is not null)
                {
                    next.Pending.Stop.ArrivalCandidateAtUtc = recordedAtUtc;
                    next.Pending.Stop.ArrivalLastSeenAtUtc = recordedAtUtc;
                    next.Pending.Stop.ArrivalEvidenceCount = 1;
                    next.Pending.Stop.ClosestDistanceMeters = next.DistanceMeters;
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
            return;
        }

        var arrival = distances.FirstOrDefault(
            x => x.DistanceMeters <= settings.DeliveryAutoCompleteArrivalRadiusMeters);
        if (arrival is null)
            return;

        var stop = arrival.Pending.Stop;
        stop.ArrivalCandidateAtUtc = recordedAtUtc;
        stop.ArrivalEvidenceCount = 1;
        stop.ArrivalLastSeenAtUtc = recordedAtUtc;
        stop.ClosestDistanceMeters = arrival.DistanceMeters;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "AutoDelivery arrival candidate order={OrderId} route={RouteId} distance={DistanceMeters}m",
            arrival.Pending.OrderId,
            routeId,
            Math.Round(arrival.DistanceMeters));
    }

    private async Task EvaluateActiveStopAsync(
        StopDistance active,
        DeliverymanLocation location,
        DateTime recordedAtUtc,
        DateTime nowUtc,
        int arrivalRadiusMeters,
        int departureRadiusMeters,
        int minPresenceSeconds,
        int branchId,
        CancellationToken cancellationToken)
    {
        var stop = active.Pending.Stop;
        if (active.DistanceMeters <= arrivalRadiusMeters)
        {
            stop.ArrivalLastSeenAtUtc = Max(stop.ArrivalLastSeenAtUtc, recordedAtUtc);
            stop.ClosestDistanceMeters = stop.ClosestDistanceMeters.HasValue
                ? Math.Min(stop.ClosestDistanceMeters.Value, active.DistanceMeters)
                : active.DistanceMeters;

            if (!stop.ArrivalConfirmedAtUtc.HasValue
                && stop.ArrivalCandidateAtUtc.HasValue
                && recordedAtUtc - stop.ArrivalCandidateAtUtc.Value >= TimeSpan.FromSeconds(minPresenceSeconds))
            {
                stop.ArrivalEvidenceCount = Math.Max(2, stop.ArrivalEvidenceCount + 1);
                stop.ArrivalConfirmedAtUtc = recordedAtUtc;
                _logger.LogInformation(
                    "AutoDelivery arrival confirmed order={OrderId} route={RouteId} evidence={EvidenceCount}",
                    active.Pending.OrderId,
                    stop.DeliveryRouteId,
                    stop.ArrivalEvidenceCount);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (active.DistanceMeters < departureRadiusMeters)
            return;

        if (!stop.ArrivalConfirmedAtUtc.HasValue)
        {
            ResetCandidate(stop);
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var stillEligible = await _db.Orders.AsNoTracking().AnyAsync(
            order => order.Id == active.Pending.OrderId
                     && order.Type == OrderType.Delivery
                     && order.Status == OrderStatus.OnTheWay
                     && order.DeliveryManId == location.DeliverymanId
                     && order.DeliveryRouteId == stop.DeliveryRouteId
                     && order.BranchId == branchId
                     && order.AddressId.HasValue
                     && _db.Addresses.Any(address =>
                         address.Id == order.AddressId.Value
                         && address.Latitude.HasValue
                         && address.Longitude.HasValue
                         && address.Latitude.Value >= -90
                         && address.Latitude.Value <= 90
                         && address.Longitude.Value >= -180
                         && address.Longitude.Value <= 180)
                     && _db.DeliveryRoutes.Any(route =>
                         route.Id == stop.DeliveryRouteId
                         && route.DeliverymanId == location.DeliverymanId
                         && route.BranchId == branchId
                         && (route.Status == DeliveryRouteStatus.Open
                             || route.Status == DeliveryRouteStatus.InProgress)),
            cancellationToken);
        if (!stillEligible)
        {
            _logger.LogInformation(
                "AutoDelivery skipped order state changed order={OrderId} route={RouteId}",
                active.Pending.OrderId,
                stop.DeliveryRouteId);
            return;
        }

        var delivered = await _sender.Send(new ChangeOrderStatusCommand
        {
            Id = active.Pending.OrderId,
            StatusChange = new ChangeOrderStatusDto
            {
                Status = OrderStatus.Delivered,
                Reason = "Entrega automatica por ubicacion GPS",
            },
            IsAutomaticDelivery = true,
            AutoDeliveredAtUtc = nowUtc,
            AutoDeliveryTriggerLocationId = location.Id,
            AutoDeliveryDepartureDistanceMeters = active.DistanceMeters,
        }, cancellationToken);
        if (delivered.Status != OrderStatus.Delivered)
            return;

        _logger.LogInformation(
            "AutoDelivery completed order={OrderId} route={RouteId} departureDistance={DistanceMeters}m",
            active.Pending.OrderId,
            stop.DeliveryRouteId,
            Math.Round(active.DistanceMeters));
    }

    private const int MaximumHistoryPoints = 3000;
    private DateTime Now => ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);

    /// <summary>
    /// Called only for a fresh point near the branch. Recovers late GPS history,
    /// never auto-delivers a stop without its own arrival and departure evidence.
    /// </summary>
    public async Task<DeliveryReturnReview?> ReconcileReturnAsync(
        DeliverymanLocation latestLocation, CancellationToken cancellationToken = default)
    {
        if (latestLocation.DeliveryRouteId is not int routeId
            || !DeliveryEvidenceRules.Fresh(latestLocation, Now)) return null;
        DeliveryReturnReview? review = null;
        await _routeLock.ExecuteAsync(routeId, async ct =>
        {
            var route = await LoadReturnRouteAsync(routeId, latestLocation.DeliverymanId, ct);
            if (route is null || route.Status != DeliveryRouteStatus.InProgress) return;
            var evidence = await ReturnContextAsync(route, latestLocation, ct);
            if (evidence is null) return;

            if (SettingsAreValid(route.Branch.DeliveryAutoCompleteArrivalRadiusMeters,
                route.Branch.DeliveryAutoCompleteDepartureRadiusMeters,
                route.Branch.DeliveryAutoCompleteMinPresenceSeconds)
                && route.Branch.DeliveryAutoCompleteEnabled)
            {
                // Reconstruct fresh state in memory, including earlier delivered
                // stops, so one visit cannot accidentally count for two customers.
                var visits = BuildVisits(route, replay: true);
                var leftBranch = false;
                foreach (var point in evidence.Points)
                {
                    if (!leftBranch)
                    {
                        leftBranch = DeliveryEvidenceRules.Reliable(point)
                            && DeliveryEvidenceRules.Distance(point, route.Branch.Latitude!.Value,
                                route.Branch.Longitude!.Value) >=
                               Math.Max(120, route.Branch.DeliveryTrackingAllowedDistanceMeters * 2);
                        if (!leftBranch) continue;
                    }
                    var departure = DeliveryEvidenceRules.Advance(visits, point,
                        route.Branch.DeliveryAutoCompleteArrivalRadiusMeters,
                        route.Branch.DeliveryAutoCompleteDepartureRadiusMeters,
                        route.Branch.DeliveryAutoCompleteMinPresenceSeconds);
                    if (departure is not null)
                        await CompleteRecoveredStopAsync(route, departure, ct);
                }
            }

            await CompleteReturnedRouteIfTerminalAsync(route, latestLocation.RecordedAt, ct);
            if (route.Status == DeliveryRouteStatus.Completed) return;
            var pendingOrders = await _db.Orders.AsNoTracking()
                .Where(o => o.DeliveryRouteId == route.Id
                    && o.DeliveryManId == route.DeliverymanId
                    && o.BranchId == route.BranchId
                    && o.Type == OrderType.Delivery
                    && o.Status == OrderStatus.OnTheWay)
                .ToListAsync(ct);
            // Only offer orders dispatched before the driver's actual return.
            var pendingIds = pendingOrders.Where(o => AssignedAt(o, route) <= evidence.Return.LastOutsideAt)
                .Select(o => o.Id).Distinct().OrderBy(id => id).ToArray();
            if (pendingIds.Length > 0)
                review = new DeliveryReturnReview(route.Id, latestLocation.Id, pendingIds);
        }, cancellationToken);
        return review;
    }

    /// <summary>Human-confirmed deliveries; not recorded as automatic GPS deliveries.</summary>
    public async Task ConfirmReturnedOrdersAsync(
        int routeId, int deliverymanId, int branchId, long returnLocationId,
        IReadOnlyList<int> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds is null || orderIds.Count is < 1 or > 50
            || orderIds.Any(id => id <= 0) || orderIds.Distinct().Count() != orderIds.Count
            || returnLocationId <= 0)
            throw new BusinessException("Selecciona los pedidos que realmente entregaste.");

        await _routeLock.ExecuteAsync(routeId, async ct =>
        {
            var route = await LoadReturnRouteAsync(routeId, deliverymanId, ct, includeCompleted: true);
            if (route is null || route.BranchId != branchId)
                throw new BusinessException("La ruta no pertenece a tu sesión.");
            var selected = await _db.Orders.AsNoTracking()
                .Where(o => orderIds.Contains(o.Id)).ToListAsync(ct);
            if (selected.Count != orderIds.Count ||
                selected.Any(o => o.DeliveryRouteId != routeId
                    || o.DeliveryManId != deliverymanId || o.BranchId != branchId
                    || o.Type != OrderType.Delivery))
                throw new BusinessException("La asignación cambió. Actualiza tu ruta.");
            if (selected.All(o => o.Status == OrderStatus.Delivered)) return; // idempotent retry
            if (selected.Any(o => o.Status is not (OrderStatus.Delivered or OrderStatus.OnTheWay)))
                throw new BusinessException("La ruta contiene pedidos que ya no pueden entregarse.");

            var latest = await _db.DeliverymanLocations.AsNoTracking()
                .Where(p => p.DeliverymanId == deliverymanId)
                .OrderByDescending(p => p.RecordedAt).ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync(ct);
            if (latest?.DeliveryRouteId != routeId)
                throw new BusinessException("No se confirmó el regreso de esta ruta a la sucursal.");
            var evidence = await ReturnContextAsync(route, latest, ct);
            if (evidence is null ||
                !evidence.Points.Any(p => p.Id == returnLocationId
                    && p.RecordedAt >= evidence.Return.EnteredAt) ||
                selected.Any(o => AssignedAt(o, route) > evidence.Return.LastOutsideAt))
                throw new BusinessException("El regreso ya no es válido. Actualiza la ruta.");

            // Lock selected orders in stable order to prevent a concurrent cashier
            // reassignment from racing with a batch confirmation.
            if (_db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
                foreach (var id in orderIds.OrderBy(id => id))
                    await _db.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT id FROM \"order\" WHERE id = {id} AND tenant_id = {route.TenantId} FOR UPDATE", ct);

            selected = await _db.Orders.AsNoTracking()
                .Where(o => orderIds.Contains(o.Id)).ToListAsync(ct);
            if (selected.Count != orderIds.Count ||
                selected.Any(o => o.DeliveryRouteId != routeId
                    || o.DeliveryManId != deliverymanId
                    || o.Status is not (OrderStatus.Delivered or OrderStatus.OnTheWay)))
                throw new BusinessException("La asignación cambió. No se registraron entregas.");

            foreach (var order in selected.Where(o => o.Status == OrderStatus.OnTheWay).OrderBy(o => o.Id))
                await _sender.Send(new ChangeOrderStatusCommand
                {
                    Id = order.Id,
                    StatusChange = new ChangeOrderStatusDto
                    {
                        Status = OrderStatus.Delivered,
                        Reason = "Entrega confirmada al regresar a sucursal"
                    }
                }, ct);
            AuditReturn(route, "return_confirmed",
                new { orderIds, returnLocationId, recordedAtUtc = Now });
            await CompleteReturnedRouteIfTerminalAsync(route, latest.RecordedAt, ct);
            await _db.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    private Task<DeliveryRoute?> LoadReturnRouteAsync(
        int routeId, int deliverymanId, CancellationToken ct, bool includeCompleted = false) =>
        _db.DeliveryRoutes.Include(r => r.Branch)
            .Include(r => r.Stops).ThenInclude(s => s.Order).ThenInclude(o => o.Address)
            .FirstOrDefaultAsync(r => r.Id == routeId && r.DeliverymanId == deliverymanId &&
                (r.Status == DeliveryRouteStatus.InProgress || r.Status == DeliveryRouteStatus.Open
                    || (includeCompleted && r.Status == DeliveryRouteStatus.Completed)), ct);

    private static DateTime AssignedAt(Order order, DeliveryRoute route)
    {
        var times = order.GetStatusTimes();
        DateTime? at = null;
        if (times.TryGetValue("ontheway", out var dispatched)) at = dispatched;
        if (times.TryGetValue(Order.DeliveryManAssignedStatusTimeKey, out var assigned)
            && (!at.HasValue || assigned > at.Value)) at = assigned;
        return ColombiaTimeHelper.EnsureUtc(at ?? route.LastAssignmentAtUtc);
    }

    private static List<DeliveryEvidenceRules.Visit> BuildVisits(DeliveryRoute route, bool replay) =>
        route.Stops.OrderBy(s => s.StopSequence).ThenBy(s => s.Id)
            .Where(s => (replay || s.AutoDeliveredAtUtc is null)
                && s.Order.Type == OrderType.Delivery
                && s.Order.DeliveryManId == route.DeliverymanId
                && s.Order.DeliveryRouteId == route.Id
                && s.Order.BranchId == route.BranchId
                && (s.Order.Status == OrderStatus.OnTheWay
                    || (replay && s.Order.Status == OrderStatus.Delivered))
                && s.Order.Address?.Latitude != null
                && s.Order.Address.Longitude != null)
            .Select(s => new DeliveryEvidenceRules.Visit(replay
                    ? new DeliveryRouteStop
                    {
                        Id = s.Id, DeliveryRouteId = route.Id,
                        OrderId = s.OrderId, StopSequence = s.StopSequence
                    } : s,
                s.OrderId, s.Order.Address!.Latitude!.Value,
                s.Order.Address.Longitude!.Value, AssignedAt(s.Order, route)))
            .ToList();

    private sealed record ReturnContext(DeliveryEvidenceRules.ReturnEvidence Return,
        IReadOnlyList<DeliverymanLocation> Points);

    private async Task<ReturnContext?> ReturnContextAsync(
        DeliveryRoute route, DeliverymanLocation latest, CancellationToken ct)
    {
        if (route.Status != DeliveryRouteStatus.InProgress ||
            !DeliveryEvidenceRules.Fresh(latest, Now)
            || latest.DeliveryRouteId != route.Id
            || latest.WorkSessionId is not int sessionId
            || route.Branch.Latitude is null || route.Branch.Longitude is null
            || DeliveryEvidenceRules.Distance(latest, route.Branch.Latitude.Value,
                route.Branch.Longitude.Value) > route.Branch.DeliveryTrackingAllowedDistanceMeters)
            return null;

        if (await _db.DeliverymanLocations.AsNoTracking().AnyAsync(p =>
            p.DeliverymanId == route.DeliverymanId
            && (p.RecordedAt > latest.RecordedAt
                || (p.RecordedAt == latest.RecordedAt && p.Id > latest.Id)), ct))
            return null;

        var session = await _db.DeliveryWorkSessions.AsNoTracking().FirstOrDefaultAsync(s =>
            s.Id == sessionId && s.DeliverymanId == route.DeliverymanId
            && s.BranchId == route.BranchId && s.Status == DeliveryWorkSessionStatus.Active
            && s.AutoCloseAt > Now, ct);
        if (session is null) return null;
        var start = route.Stops.Select(s => AssignedAt(s.Order, route))
            .DefaultIfEmpty(route.LastAssignmentAtUtc).Min();
        if (start < session.StartedAt) start = session.StartedAt;

        var points = await _db.DeliverymanLocations.AsNoTracking()
            .Where(p => p.DeliveryRouteId == route.Id && p.DeliverymanId == route.DeliverymanId
                && p.WorkSessionId == sessionId && p.RecordedAt >= start
                && p.RecordedAt <= latest.RecordedAt)
            .OrderBy(p => p.RecordedAt).ThenBy(p => p.Id)
            .Take(MaximumHistoryPoints + 1).ToListAsync(ct);
        if (points.Count > MaximumHistoryPoints)
        {
            _logger.LogWarning("AutoDelivery history limit route={RouteId}; manual review required", route.Id);
            return null;
        }
        var found = DeliveryEvidenceRules.FindReturn(points,
            route.Branch.Latitude.Value, route.Branch.Longitude.Value,
            route.Branch.DeliveryTrackingAllowedDistanceMeters, Now);
        return found is null ? null : new ReturnContext(found, points);
    }

    private async Task CompleteRecoveredStopAsync(
        DeliveryRoute route, DeliveryEvidenceRules.Departure departure, CancellationToken ct)
    {
        var stop = route.Stops.FirstOrDefault(s => s.OrderId == departure.Visit.OrderId);
        if (stop is null || stop.Order.Status != OrderStatus.OnTheWay || stop.AutoDeliveredAtUtc.HasValue)
            return;
        var order = await _db.Orders.AsNoTracking().Include(o => o.Address)
            .FirstOrDefaultAsync(o => o.Id == stop.OrderId, ct);
        if (order is null || order.Status != OrderStatus.OnTheWay
            || order.DeliveryManId != route.DeliverymanId || order.DeliveryRouteId != route.Id
            || order.BranchId != route.BranchId
            || order.Address?.Latitude != departure.Visit.Latitude
            || order.Address.Longitude != departure.Visit.Longitude
            || order.Address.UpdatedAt > departure.Visit.Stop.ArrivalCandidateAtUtc)
            return;

        stop.ArrivalCandidateAtUtc = departure.Visit.Stop.ArrivalCandidateAtUtc;
        stop.ArrivalConfirmedAtUtc = departure.Visit.Stop.ArrivalConfirmedAtUtc;
        stop.ArrivalEvidenceCount = departure.Visit.Stop.ArrivalEvidenceCount;
        stop.ArrivalLastSeenAtUtc = departure.Visit.Stop.ArrivalLastSeenAtUtc;
        stop.ClosestDistanceMeters = departure.Visit.Stop.ClosestDistanceMeters;
        await _db.SaveChangesAsync(ct);
        var completed = await _sender.Send(new ChangeOrderStatusCommand
        {
            Id = order.Id,
            StatusChange = new ChangeOrderStatusDto
            {
                Status = OrderStatus.Delivered,
                Reason = "Entrega recuperada por evidencia GPS al regresar"
            },
            IsAutomaticDelivery = true,
            AutoDeliveredAtUtc = Now,
            AutoDeliveryTriggerLocationId = departure.Point.Id,
            AutoDeliveryDepartureDistanceMeters = departure.DistanceMeters
        }, ct);
        if (completed.Status != OrderStatus.Delivered) return;
        AuditReturn(route, "return_recovered",
            new { orderId = order.Id, departureLocationId = departure.Point.Id,
                observedAtUtc = departure.Point.RecordedAt, recordedAtUtc = Now });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "AutoDelivery completed order={OrderId} route={RouteId} recovered=true",
            order.Id, route.Id);
    }

    private async Task CompleteReturnedRouteIfTerminalAsync(
        DeliveryRoute route, DateTime returnedAt, CancellationToken ct)
    {
        var ids = route.Stops.Select(s => s.OrderId).ToArray();
        if (ids.Length == 0) return;
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => ids.Contains(o.Id)).ToListAsync(ct);
        if (orders.Count != ids.Length
            || orders.Any(o => o.Status is not (OrderStatus.Delivered or OrderStatus.Cancelled)))
            return;
        route.CompletedAtUtc = returnedAt;
        if (route.RouteStartedAtUtc.HasValue)
        {
            route.ActualDurationSeconds = (int)Math.Max(0,
                (returnedAt - route.RouteStartedAtUtc.Value).TotalSeconds);
            route.MetSla = route.MetaDurationSeconds.HasValue
                ? route.ActualDurationSeconds <= route.MetaDurationSeconds.Value : null;
        }
        route.Status = DeliveryRouteStatus.Completed;
        await _db.SaveChangesAsync(ct);
    }

    private void AuditReturn(DeliveryRoute route, string operation, object metadata) =>
        _db.EntityAuditLogs.Add(new EntityAuditLog
        {
            TenantId = route.TenantId,
            BranchId = route.BranchId,
            EntityType = "delivery_route",
            EntityId = route.Id,
            OperationType = operation,
            ChangedAt = Now,
            BusinessDate = Now.AddHours(-5).Date,
            ChangedByUserId = route.DeliverymanId,
            SummaryText = $"Ruta #{route.Id}: {operation}",
            MetadataJson = JsonSerializer.Serialize(metadata)
        });

    private static bool SettingsAreValid(int arrivalRadiusMeters, int departureRadiusMeters, int minPresenceSeconds) =>
        arrivalRadiusMeters is >= 10 and <= 150
        && departureRadiusMeters is >= 20 and <= 500
        && departureRadiusMeters > arrivalRadiusMeters
        && minPresenceSeconds is >= 5 and <= 300;

    private static bool CoordinatesAreValid(decimal latitude, decimal longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    private static DateTime Max(DateTime? current, DateTime value) =>
        !current.HasValue || value > current.Value ? value : current.Value;

    private static void ResetCandidate(DeliveryRouteStop stop)
    {
        stop.ArrivalCandidateAtUtc = null;
        stop.ArrivalConfirmedAtUtc = null;
        stop.ArrivalEvidenceCount = 0;
        stop.ArrivalLastSeenAtUtc = null;
        stop.ClosestDistanceMeters = null;
    }

    private sealed record PendingStop(
        DeliveryRouteStop Stop,
        int OrderId,
        decimal Latitude,
        decimal Longitude);

    private sealed record StopDistance(PendingStop Pending, double DistanceMeters);
}

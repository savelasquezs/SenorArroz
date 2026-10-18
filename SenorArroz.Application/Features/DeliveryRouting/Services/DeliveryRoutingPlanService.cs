using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.DeliveryRouting.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.DeliveryRouting.Services;

public sealed class DeliveryRoutingPlanService : IDeliveryRoutingPlanService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> BranchLocks = new();
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IRoutingCostMatrixProvider _matrixProvider;
    private readonly IDeliveryRouteOptimizer _optimizer;
    private readonly IKitchenPreparationEstimator _kitchenEstimator;
    private readonly IDeliverymanAvailabilityService _availability;
    private readonly IGoogleRoutesDrivingMetricsService _googleRoutes;
    private readonly IOrderNotificationService _notifications;
    private readonly DeliveryRoutingOptions _options;
    private readonly ILogger<DeliveryRoutingPlanService> _logger;

    public DeliveryRoutingPlanService(IApplicationDbContext db, IClock clock,
        IRoutingCostMatrixProvider matrixProvider, IDeliveryRouteOptimizer optimizer,
        IKitchenPreparationEstimator kitchenEstimator, IDeliverymanAvailabilityService availability,
        IGoogleRoutesDrivingMetricsService googleRoutes, IOrderNotificationService notifications,
        IOptions<DeliveryRoutingOptions> options, ILogger<DeliveryRoutingPlanService> logger)
    {
        _db = db;
        _clock = clock;
        _matrixProvider = matrixProvider;
        _optimizer = optimizer;
        _kitchenEstimator = kitchenEstimator;
        _availability = availability;
        _googleRoutes = googleRoutes;
        _notifications = notifications;
        _options = options.Value;
        _logger = logger;
    }

    public Task<DeliveryRoutingPlanDto> GetOrCreateActivePlanAsync(int branchId, CancellationToken cancellationToken = default) =>
        ExecuteCalculationAsync(branchId, false, cancellationToken);

    public Task<DeliveryRoutingPlanDto> RecalculateAsync(int branchId, CancellationToken cancellationToken = default) =>
        ExecuteCalculationAsync(branchId, true, cancellationToken);

    private async Task<DeliveryRoutingPlanDto> ExecuteCalculationAsync(int branchId, bool force, CancellationToken ct)
    {
        var measurement = new RoutingMeasurement(_options.Measurement, _clock, _logger);
        var waiting = Stopwatch.StartNew();
        var branchLock = BranchLocks.GetOrAdd(branchId, _ => new SemaphoreSlim(1, 1));
        await branchLock.WaitAsync(ct);
        measurement.LockWaitMs = waiting.ElapsedMilliseconds;
        try { return await RecalculateCoreAsync(branchId, force, measurement, ct); }
        catch (OperationCanceledException) { measurement.Write(branchId, "cancelled"); throw; }
        catch { measurement.Write(branchId, "failed"); throw; }
        finally { branchLock.Release(); }
    }

    private IQueryable<Order> CandidateQuery(int branchId, DateTime now) => _db.Orders.AsNoTracking()
        .Include(x => x.Address).ThenInclude(x => x!.Neighborhood)
        .Where(DeliveryRoutingPolicy.Eligible(branchId, now, _options));

    private async Task<DeliveryRoutingPlanDto> RecalculateCoreAsync(
        int branchId, bool force, RoutingMeasurement measurement, CancellationToken ct)
    {
        if (!_options.Enabled) throw new BusinessException("El enrutador dinamico no esta habilitado.");
        var now = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc);
        var inputs = Stopwatch.StartNew();
        var branch = await _db.Branches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == branchId, ct)
            ?? throw new NotFoundException("Sucursal no encontrada.");
        measurement.TenantId = branch.TenantId;
        var orders = await CandidateQuery(branchId, now).OrderBy(x => x.Id).ToListAsync(ct);
        measurement.CandidateCount = orders.Count;
        var estimates = await _kitchenEstimator.EstimateAsync(branchId, orders.Select(x => x.Id).ToArray(), now, ct);
        var branchHasLocation = DeliveryRoutingPolicy.Coordinates(branch.Latitude, branch.Longitude);
        var capacity = branchHasLocation
            ? await _availability.GetCapacityAsync(branchId, (double)branch.Latitude!.Value,
                (double)branch.Longitude!.Value, now, ct)
            : new DeliveryRoutingCapacity([], 0, 0, ["La sucursal no tiene coordenadas válidas."]);
        var fingerprint = Fingerprint(branch, orders, capacity, estimates, now);
        measurement.InputMs = inputs.ElapsedMilliseconds;
        if (!force)
        {
            var current = await LoadPlanEntityAsync(branchId, ct);
            if (current?.InputFingerprint == fingerprint && current.GeneratedAtUtc <= now
                && now - current.GeneratedAtUtc <= TimeSpan.FromSeconds(Math.Clamp(_options.PlanMaxAgeSeconds, 30, 300)))
            {
                var cached = Map(current);
                cached = cached with { Capacity = cached.Capacity with { Couriers = capacity.Couriers } };
                measurement.Write(branchId, "reused", cached);
                return cached;
            }
        }

        var cutoff = now.AddSeconds(Math.Clamp(_options.PreparationLookaheadSeconds, 0, 3600));
        var validOrders = orders.Where(x => DeliveryRoutingPolicy.Coordinates(x.Address?.Latitude, x.Address?.Longitude)
            && (x.Status == OrderStatus.Ready || DeliveryRoutingPolicy.ReadyAt(x, estimates, now, _options) <= cutoff)).ToList();
        RoutingCostMatrix? matrix = null;
        var optimization = new DeliveryRouteOptimizationResult([], Enumerable.Range(0, validOrders.Count).ToArray(), 0, []);
        if (branchHasLocation && validOrders.Count > 0 && capacity.Slots.Count > 0)
        {
            matrix = _matrixProvider.Create((double)branch.Latitude!.Value, (double)branch.Longitude!.Value,
                validOrders.Select(x => Node(x, estimates, now)).ToList());
            try { optimization = _optimizer.Optimize(new DeliveryRouteOptimizationRequest(matrix, capacity.Slots, now)); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Solver unavailable for branch {BranchId}", branchId);
                optimization = optimization with { Warnings = ["No fue posible calcular propuestas; la selección manual sigue disponible."] };
            }
        }
        var plan = new DeliveryRoutingPlan
        {
            BranchId = branchId, Status = DeliveryRoutingPlanStatus.Active, GeneratedAtUtc = now,
            InputFingerprint = fingerprint, AvailableSlotCount = capacity.AvailableNow,
            SoonSlotCount = capacity.AvailableSoon, SolverDurationMs = optimization.SolverDurationMs,
            MatrixSource = matrix?.Source ?? RoutingMatrixSource.Approximate,
            Warnings = JoinWarnings(capacity.Warnings.Concat(optimization.Warnings)),
        };
        var indexes = validOrders.Select((order, index) => (order.Id, index)).ToDictionary(x => x.Id, x => x.index);
        var proposed = new HashSet<int>();
        var longWait = new HashSet<int>();
        foreach (var optimized in optimization.Routes)
        {
            var routeOrders = optimized.NodeIndexes.Select(index => validOrders[index]).ToList();
            var slot = capacity.Slots[optimized.VehicleIndex];
            // Do not hold ready food indefinitely for a predicted complement.
            var deadline = routeOrders.Select(o => DeliveryRoutingPolicy.WaitDeadline(o, _options)).Min();
            if (routeOrders.Any(o => o.Status == OrderStatus.Ready) &&
                (now.AddSeconds(DeliveryRoutingPolicy.DepartureSeconds(routeOrders, estimates, now, slot.AvailableAtSeconds, _options))
                    > (deadline > now.AddSeconds(slot.AvailableAtSeconds) ? deadline : now.AddSeconds(slot.AvailableAtSeconds))
                 || routeOrders.Any(o => o.Status != OrderStatus.Ready && estimates.TryGetValue(o.Id, out var estimate)
                     && estimate.ReadyWithinWindowProbability < .6)))
            {
                foreach (var pending in routeOrders.Where(o => o.Status != OrderStatus.Ready)) longWait.Add(pending.Id);
                routeOrders = routeOrders.Where(o => o.Status == OrderStatus.Ready).ToList();
            }
            if (routeOrders.Count == 0) continue;
            var proposal = BuildProposal(plan.Proposals.Count + 1, routeOrders, matrix!, indexes, slot, estimates, now);
            proposal.Score = optimized.Score;
            // Google is outside the publication transaction; no network call holds DB locks.
            await ValidateProposalAsync(proposal, routeOrders, branch, measurement, ct);
            plan.Proposals.Add(proposal);
            foreach (var stop in proposal.Stops) plan.Stops.Add(stop);
            foreach (var order in routeOrders) proposed.Add(order.Id);
        }
        foreach (var order in orders.Where(o => !proposed.Contains(o.Id)))
        {
            plan.Stops.Add(new DeliveryRouteProposalStop
            {
                OrderId = order.Id,
                EstimatedReadyAtUtc = DeliveryRoutingPolicy.ReadyAt(order, estimates, now, _options),
                ConservativeReadyAtUtc = estimates.GetValueOrDefault(order.Id)?.ConservativeReadyAtUtc,
                PreparationConfidence = estimates.GetValueOrDefault(order.Id)?.Confidence ?? "unknown",
                ReadyWithinWindowProbability = estimates.GetValueOrDefault(order.Id)?.ReadyWithinWindowProbability ?? 0,
                PreparationSampleSize = estimates.GetValueOrDefault(order.Id)?.SampleSize ?? 0,
                ServiceSeconds = Math.Max(0, _options.ServiceSecondsPerOrder),
                WasReadyAtGeneration = order.Status == OrderStatus.Ready,
                UnroutedReason = !DeliveryRoutingPolicy.Coordinates(order.Address?.Latitude, order.Address?.Longitude)
                    ? "requiresLocation" : DeliveryRoutingPolicy.ReadyAt(order, estimates, now, _options) > cutoff
                    ? "preparingLater" : longWait.Contains(order.Id) ? "waitingWouldDelayReadyOrders"
                    : capacity.Slots.Count == 0 ? "noCapacity"
                    : order.Status != OrderStatus.Ready && estimates.GetValueOrDefault(order.Id)?.ReadyWithinWindowProbability < .6
                        ? "preparationUncertain" : "notSelectedByOptimizer"
            });
        }

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var latestOrders = await CandidateQuery(branchId, DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc))
            .OrderBy(x => x.Id).ToListAsync(ct);
        if (OrderSignature(latestOrders) != OrderSignature(orders))
            throw new RoutingPlanStaleException("Los pedidos cambiaron durante el cálculo. Actualiza las rutas.");
        var activePlans = await _db.DeliveryRoutingPlans
            .Where(x => x.BranchId == branchId && x.Status == DeliveryRoutingPlanStatus.Active).ToListAsync(ct);
        foreach (var active in activePlans) active.Status = DeliveryRoutingPlanStatus.Superseded;
        if (activePlans.Count > 0) await _db.SaveChangesAsync(ct);
        plan.GenerationNumber = (await _db.DeliveryRoutingPlans.Where(x => x.BranchId == branchId)
            .MaxAsync(x => (long?)x.GenerationNumber, ct) ?? 0) + 1;
        _db.DeliveryRoutingPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
        var persisted = await LoadPlanEntityAsync(branchId, ct)
            ?? throw new InvalidOperationException("No fue posible cargar el plan generado.");
        if (transaction is not null) await transaction.CommitAsync(ct);
        _logger.LogInformation(
            "DeliveryRoutePlanGenerated BranchId={BranchId} PlanId={PlanId} Version={Version} Orders={Orders} Proposals={Proposals} Unrouted={Unrouted} AvailableNow={AvailableNow} AvailableSoon={AvailableSoon} SolverDurationMs={SolverDurationMs} MatrixSource={MatrixSource}",
            branchId, persisted.Id, persisted.GenerationNumber, orders.Count, persisted.Proposals.Count,
            persisted.Stops.Count(x => x.DeliveryRouteProposalId == null), capacity.AvailableNow,
            capacity.AvailableSoon, persisted.SolverDurationMs, persisted.MatrixSource);
        try { await _notifications.NotifyDeliveryRoutingPlanChanged(branchId, persisted.Id, persisted.GenerationNumber); }
        catch (Exception ex) { _logger.LogWarning(ex, "Plan {PlanId} persisted; notification failed", persisted.Id); }
        var result = Map(persisted);
        result = result with { Capacity = result.Capacity with { Couriers = capacity.Couriers } };
        measurement.Write(branchId, "generated", result);
        return result;
    }

    public async Task<DeliveryRouteProposalDto> PreviewAsync(int branchId, IReadOnlyList<int> orderIds,
        CancellationToken cancellationToken = default, long? expectedPlanVersion = null)
    {
        var ids = orderIds.Distinct().ToArray();
        if (ids.Length == 0 || ids.Length != orderIds.Count) throw new BusinessException("Selecciona pedidos sin duplicados.");
        var now = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc);
        if (expectedPlanVersion.HasValue && !await _db.DeliveryRoutingPlans.AsNoTracking().AnyAsync(p =>
            p.BranchId == branchId && p.Status == DeliveryRoutingPlanStatus.Active
            && p.GenerationNumber == expectedPlanVersion.Value && p.GeneratedAtUtc <= now
            && p.GeneratedAtUtc >= now.AddSeconds(-Math.Clamp(_options.PlanMaxAgeSeconds, 30, 300)), cancellationToken))
            throw new RoutingPlanStaleException();
        var branch = await _db.Branches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == branchId, cancellationToken)
            ?? throw new NotFoundException("Sucursal no encontrada.");
        if (!DeliveryRoutingPolicy.Coordinates(branch.Latitude, branch.Longitude))
            throw new BusinessException("La sucursal no tiene coordenadas válidas.");
        var byId = await CandidateQuery(branchId, now).Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (byId.Count != ids.Length) throw new BusinessException("La selección contiene pedidos que ya no pertenecen al plan activo.");
        var orders = ids.Select(id => byId[id]).ToList();
        if (orders.Any(x => !DeliveryRoutingPolicy.Coordinates(x.Address?.Latitude, x.Address?.Longitude)))
            throw new BusinessException("Todos los pedidos seleccionados deben tener coordenadas válidas.");
        var estimates = await _kitchenEstimator.EstimateAsync(branchId, ids, now, cancellationToken);
        if (orders.Any(x => x.Status != OrderStatus.Ready && DeliveryRoutingPolicy.ReadyAt(x, estimates, now, _options)
            > now.AddSeconds(Math.Clamp(_options.PreparationLookaheadSeconds, 0, 3600))))
            throw new BusinessException("Hay pedidos cuya preparación está fuera de la ventana de salida.");
        var matrix = _matrixProvider.Create((double)branch.Latitude!.Value, (double)branch.Longitude!.Value,
            orders.Select(o => Node(o, estimates, now)).ToList());
        var indexes = orders.Select((o, i) => (o.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var proposal = BuildProposal(0, orders, matrix, indexes, new RoutingVehicleSlot(0), estimates, now);
        foreach (var stop in proposal.Stops) stop.Order = byId[stop.OrderId];
        return MapProposal(proposal, proposal.Stops.Select(MapStop).ToList(), false) with
        { Status = "preview", SelectionFingerprint = DeliveryRoutingPolicy.SelectionFingerprint(orders) };
    }

    private RoutingNode Node(Order order, IReadOnlyDictionary<int, KitchenPreparationEstimate> estimates, DateTime now) =>
        new(order.Id, (double)order.Address!.Latitude!.Value, (double)order.Address.Longitude!.Value,
            order.Type == OrderType.Reservation ? order.PrepareAt ?? order.CreatedAt : order.CreatedAt,
            DeliveryRoutingPolicy.ReadyAt(order, estimates, now, _options), order.Status == OrderStatus.Ready,
            Math.Max(0, _options.ServiceSecondsPerOrder))
        {
            WaitDeadlineAtUtc = DeliveryRoutingPolicy.WaitDeadline(order, _options),
            ReadyWithinWindowProbability = estimates.GetValueOrDefault(order.Id)?.ReadyWithinWindowProbability ?? 0
        };

    private DeliveryRouteProposal BuildProposal(int sequence, IReadOnlyList<Order> orders, RoutingCostMatrix matrix,
        IReadOnlyDictionary<int, int> indexes, RoutingVehicleSlot slot,
        IReadOnlyDictionary<int, KitchenPreparationEstimate> estimates, DateTime now)
    {
        var departure = DeliveryRoutingPolicy.DepartureSeconds(orders, estimates, now, slot.AvailableAtSeconds, _options);
        var pending = orders.Any(o => o.Status != OrderStatus.Ready);
        var proposal = new DeliveryRouteProposal
        {
            Sequence = sequence, Status = DeliveryRouteProposalStatus.Available,
            Recommendation = slot.AvailableAtSeconds > 0 ? DeliveryRouteRecommendation.Next
                : pending ? DeliveryRouteRecommendation.Wait : DeliveryRouteRecommendation.LeaveNow,
            ExpectedDepartureAtUtc = now.AddSeconds(departure), WaitSeconds = departure,
            WaitDeadlineAtUtc = orders.Select(o => DeliveryRoutingPolicy.WaitDeadline(o, _options)).Min(),
            GoogleValidationStatus = GoogleRouteValidationStatus.NotRequested,
            DirectionSpreadDegrees = DirectionSpread(orders.Select(o => matrix.BearingFromBranchDegrees[indexes[o.Id] + 1]))
        };
        var elapsed = departure;
        var previous = 0;
        for (var i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            var index = indexes[order.Id] + 1;
            var travel = (int)matrix.DurationSeconds[previous, index];
            elapsed += travel;
            proposal.ApproximateDrivingDurationSeconds += travel;
            proposal.ApproximateDistanceMeters += (int)matrix.DistanceMeters[previous, index];
            var anchor = order.Type == OrderType.Reservation ? order.PrepareAt ?? order.CreatedAt : order.CreatedAt;
            proposal.WorstAgeAtDeliverySeconds = Math.Max(proposal.WorstAgeAtDeliverySeconds,
                Math.Max(0, (int)(now.AddSeconds(elapsed) - anchor).TotalSeconds));
            proposal.Stops.Add(new DeliveryRouteProposalStop
            {
                OrderId = order.Id, StopSequence = i + 1,
                EstimatedReadyAtUtc = DeliveryRoutingPolicy.ReadyAt(order, estimates, now, _options),
                ConservativeReadyAtUtc = estimates.GetValueOrDefault(order.Id)?.ConservativeReadyAtUtc,
                PreparationConfidence = estimates.GetValueOrDefault(order.Id)?.Confidence ?? "unknown",
                ReadyWithinWindowProbability = estimates.GetValueOrDefault(order.Id)?.ReadyWithinWindowProbability ?? 0,
                PreparationSampleSize = estimates.GetValueOrDefault(order.Id)?.SampleSize ?? 0,
                EstimatedArrivalAtUtc = now.AddSeconds(elapsed), TravelFromPreviousSeconds = travel,
                ServiceSeconds = matrix.Nodes[index - 1].ServiceSeconds,
                BearingFromBranchDegrees = matrix.BearingFromBranchDegrees[index],
                WasReadyAtGeneration = order.Status == OrderStatus.Ready, IsSuggestedWait = order.Status != OrderStatus.Ready
            });
            elapsed += matrix.Nodes[index - 1].ServiceSeconds;
            previous = index;
        }
        proposal.LastDeliverySeconds = elapsed - Math.Max(0, slot.AvailableAtSeconds);
        return proposal;
    }

    private async Task ValidateProposalAsync(DeliveryRouteProposal proposal, IReadOnlyList<Order> orders,
        Branch branch, RoutingMeasurement measurement, CancellationToken ct)
    {
        if (proposal.Sequence > Math.Max(0, _options.MaximumFinalistsToValidate)) return;
        var points = new List<(double Latitude, double Longitude)>
        { ((double)branch.Latitude!.Value, (double)branch.Longitude!.Value) };
        points.AddRange(orders.Select(o => ((double)o.Address!.Latitude!.Value, (double)o.Address.Longitude!.Value)));
        points.Add(points[0]);
        var timer = Stopwatch.StartNew();
        measurement.GoogleCalls++;
        try
        {
            var metrics = await _googleRoutes.ComputeRouteAsync(points, ct);
            timer.Stop();
            if (metrics.DurationSeconds > 0 && metrics.DistanceMeters > 0)
            {
                proposal.ValidatedDrivingDurationSeconds = Math.Max(0, metrics.DurationSeconds - metrics.ReturnDurationSeconds);
                proposal.ValidatedDistanceMeters = Math.Max(0, metrics.DistanceMeters - metrics.ReturnDistanceMeters);
                proposal.GoogleValidationStatus = GoogleRouteValidationStatus.Validated;
                _logger.LogInformation("ComputeRoutesSuccess GoogleDurationMs={GoogleDurationMs} ValidatedRoutesCount=1",
                    timer.ElapsedMilliseconds);
                return;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _logger.LogWarning(ex, "ComputeRoutes failed; retaining approximate route"); }
        finally { timer.Stop(); measurement.GoogleMs += timer.ElapsedMilliseconds; }
        measurement.GoogleFailures++;
        proposal.GoogleValidationStatus = GoogleRouteValidationStatus.Degraded;
        proposal.PlanningWarnings = "Google no pudo validar la ruta; se conservan las métricas aproximadas.";
        _logger.LogWarning("ComputeRoutesFailures GoogleDurationMs={GoogleDurationMs} ValidatedRoutesCount=0",
            timer.ElapsedMilliseconds);
    }

    private Task<DeliveryRoutingPlan?> LoadPlanEntityAsync(int branchId, CancellationToken ct) =>
        _db.DeliveryRoutingPlans.AsNoTracking().AsSplitQuery()
            .Include(x => x.Proposals).ThenInclude(x => x.Stops).ThenInclude(x => x.Order)
                .ThenInclude(x => x.Address).ThenInclude(x => x!.Neighborhood)
            .Include(x => x.Stops).ThenInclude(x => x.Order).ThenInclude(x => x.Address).ThenInclude(x => x!.Neighborhood)
            .Where(x => x.BranchId == branchId && x.Status == DeliveryRoutingPlanStatus.Active)
            .OrderByDescending(x => x.GenerationNumber).FirstOrDefaultAsync(ct);

    private DeliveryRoutingPlanDto Map(DeliveryRoutingPlan plan)
    {
        var eligible = DeliveryRoutingPolicy.Eligible(plan.BranchId, _clock.UtcNow, _options).Compile();
        var changed = false;
        var proposals = new List<DeliveryRouteProposalDto>();
        foreach (var proposal in plan.Proposals.Where(x => x.Status == DeliveryRouteProposalStatus.Available).OrderBy(x => x.Sequence))
        {
            var visible = proposal.Stops.Where(s => s.Order is not null && eligible(s.Order)).OrderBy(x => x.StopSequence).ToList();
            var stale = visible.Count != proposal.Stops.Count || visible.Any(s => s.WasReadyAtGeneration != (s.Order.Status == OrderStatus.Ready));
            changed |= stale;
            if (visible.Count > 0) proposals.Add(MapProposal(proposal, visible.Select(MapStop).ToList(), stale));
        }
        var proposed = proposals.SelectMany(p => p.Stops).Select(s => s.OrderId).ToHashSet();
        var unrouted = plan.Stops.Where(s => s.DeliveryRouteProposalId == null && !proposed.Contains(s.OrderId)
            && s.Order is not null && eligible(s.Order)).Select(MapStop).ToList();
        changed |= unrouted.Count != plan.Stops.Count(s => s.DeliveryRouteProposalId == null);
        return new DeliveryRoutingPlanDto(plan.Id, plan.GenerationNumber, changed ? "stale" : Camel(plan.Status.ToString()),
            plan.GeneratedAtUtc, Camel(plan.MatrixSource.ToString()),
            new DeliveryRoutingCapacityDto(plan.AvailableSlotCount, plan.SoonSlotCount), plan.SolverDurationMs,
            JoinWarnings(new[] { plan.Warnings ?? "", changed ? "El plan cambió; actualiza antes de tomar una ruta." : "" }),
            proposals, unrouted);
    }

    private static DeliveryRouteProposalDto MapProposal(DeliveryRouteProposal proposal, List<DeliveryRoutingStopDto> stops, bool stale)
    {
        var ready = stops.Where(s => s.IsReady).Select(s => s.OrderId).ToArray();
        var pending = stops.Where(s => !s.IsReady).Select(s => s.OrderId).ToArray();
        return new DeliveryRouteProposalDto(proposal.Id, proposal.Sequence, stale ? "stale" : Camel(proposal.Status.ToString()),
            Camel(proposal.Recommendation.ToString()), proposal.ExpectedDepartureAtUtc, proposal.WaitSeconds,
            proposal.ApproximateDrivingDurationSeconds, proposal.ApproximateDistanceMeters,
            proposal.ValidatedDrivingDurationSeconds, proposal.ValidatedDistanceMeters, Camel(proposal.GoogleValidationStatus.ToString()),
            proposal.LastDeliverySeconds, proposal.WorstAgeAtDeliverySeconds, proposal.DirectionSpreadDegrees, proposal.Score,
            !stale && ready.Length > 0, pending.Length == 0, stale ? Array.Empty<int>() : ready, pending,
            proposal.PlanningWarnings, stops) { WaitDeadlineAtUtc = proposal.WaitDeadlineAtUtc };
    }

    private static DeliveryRoutingStopDto MapStop(DeliveryRouteProposalStop stop) => new(
        stop.OrderId, stop.StopSequence, Camel(stop.Order.Type?.ToString() ?? string.Empty), Camel(stop.Order.Status.ToString()),
        stop.Order.Address?.AddressText ?? stop.Order.ExternalDeliveryAddress ?? string.Empty,
        stop.Order.Address?.AdditionalInfo, stop.Order.Address?.Neighborhood?.Name,
        stop.EstimatedReadyAtUtc, stop.EstimatedArrivalAtUtc, stop.TravelFromPreviousSeconds, stop.ServiceSeconds,
        stop.BearingFromBranchDegrees, stop.Order.Status == OrderStatus.Ready,
        stop.Order.Status != OrderStatus.Ready, stop.UnroutedReason)
        {
            ConservativeReadyAtUtc = stop.ConservativeReadyAtUtc, PreparationConfidence = stop.PreparationConfidence,
            ReadyWithinWindowProbability = stop.ReadyWithinWindowProbability, PreparationSampleSize = stop.PreparationSampleSize
        };

    private static double DirectionSpread(IEnumerable<double> bearings)
    {
        var values = bearings.ToArray();
        var maximum = 0d;
        for (var i = 0; i < values.Length; i++)
        for (var j = i + 1; j < values.Length; j++)
        {
            maximum = Math.Max(maximum, RoutingDirectionHelper.Separation(values[i], values[j]));
        }
        return Math.Round(maximum, 2);
    }

    private static string OrderSignature(IReadOnlyList<Order> orders) => JsonSerializer.Serialize(orders.Select(o => new
    {
        o.Id, o.Status, o.Type, o.CreatedAt, o.UpdatedAt, o.PrepareAt, o.ReservedFor, o.AddressId,
        latitude = o.Address?.Latitude, longitude = o.Address?.Longitude, addressUpdatedAt = o.Address?.UpdatedAt
    }));

    private string Fingerprint(Branch branch, IReadOnlyList<Order> orders, DeliveryRoutingCapacity capacity,
        IReadOnlyDictionary<int, KitchenPreparationEstimate> estimates, DateTime now)
    {
        var raw = JsonSerializer.Serialize(new
        {
            algorithm = "routing-corridors-v3", orders = OrderSignature(orders), branch.Latitude, branch.Longitude,
            capacity.AvailableNow, capacity.AvailableSoon,
            slots = capacity.Slots.Select(s => new { s.AvailableAtSeconds, s.DeliverymanId, s.WorkSessionId, s.CurrentRouteId, s.Confidence }),
            readiness = orders.Select(o => DeliveryRoutingPolicy.ReadyAt(o, estimates, now, _options) <= now
                ? "due" : DeliveryRoutingPolicy.ReadyAt(o, estimates, now, _options).ToString("O")),
            options = _options
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static string? JoinWarnings(IEnumerable<string> warnings)
    {
        var result = string.Join('\n', warnings.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
    private static string Camel(string value) => string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}

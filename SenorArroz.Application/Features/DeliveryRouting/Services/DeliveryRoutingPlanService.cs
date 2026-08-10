using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
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

    public DeliveryRoutingPlanService(
        IApplicationDbContext db,
        IClock clock,
        IRoutingCostMatrixProvider matrixProvider,
        IDeliveryRouteOptimizer optimizer,
        IKitchenPreparationEstimator kitchenEstimator,
        IDeliverymanAvailabilityService availability,
        IGoogleRoutesDrivingMetricsService googleRoutes,
        IOrderNotificationService notifications,
        IOptions<DeliveryRoutingOptions> options,
        ILogger<DeliveryRoutingPlanService> logger)
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

    public async Task<DeliveryRoutingPlanDto> GetOrCreateActivePlanAsync(
        int branchId,
        CancellationToken cancellationToken = default)
    {
        return await RecalculateAsync(branchId, cancellationToken);
    }

    public async Task<DeliveryRoutingPlanDto> RecalculateAsync(
        int branchId,
        CancellationToken cancellationToken = default)
    {
        var branchLock = BranchLocks.GetOrAdd(branchId, _ => new SemaphoreSlim(1, 1));
        await branchLock.WaitAsync(cancellationToken);
        try
        {
            return await RecalculateCoreAsync(branchId, cancellationToken);
        }
        finally
        {
            branchLock.Release();
        }
    }

    private async Task<DeliveryRoutingPlanDto> RecalculateCoreAsync(
        int branchId,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            throw new BusinessException("El enrutador dinamico no esta habilitado.");

        var nowUtc = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc);
        var branch = await _db.Branches
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == branchId, cancellationToken)
            ?? throw new NotFoundException("Sucursal no encontrada.");
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(x => x.Address)
            .ThenInclude(x => x!.Neighborhood)
            .Where(x => x.BranchId == branchId
                        && (x.Type == OrderType.Delivery || x.Type == OrderType.Reservation)
                        && (x.Status == OrderStatus.Taken
                            || x.Status == OrderStatus.InPreparation
                            || x.Status == OrderStatus.Ready)
                        && x.DeliveryManId == null
                        && x.DeliveryRouteId == null
                        && string.IsNullOrEmpty(x.ExternalFulfillmentProvider))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var warnings = new List<string>();
        var validOrders = orders
            .Where(x => x.Address?.Latitude is not null && x.Address.Longitude is not null)
            .ToList();
        var missingCoordinates = orders.Except(validOrders).ToList();
        var estimates = await _kitchenEstimator.EstimateAsync(
            branchId,
            orders.Select(x => x.Id).ToArray(),
            nowUtc,
            cancellationToken);

        DeliveryRoutingCapacity capacity;
        RoutingCostMatrix? matrix = null;
        DeliveryRouteOptimizationResult optimization;
        if (branch.Latitude is null || branch.Longitude is null)
        {
            warnings.Add("La sucursal no tiene coordenadas configuradas.");
            capacity = new DeliveryRoutingCapacity([], 0, 0, warnings);
            optimization = new DeliveryRouteOptimizationResult(
                [],
                Enumerable.Range(0, validOrders.Count).ToArray(),
                0,
                warnings);
        }
        else
        {
            capacity = await _availability.GetCapacityAsync(
                branchId,
                (double)branch.Latitude.Value,
                (double)branch.Longitude.Value,
                nowUtc,
                cancellationToken);
            warnings.AddRange(capacity.Warnings);
            var nodes = validOrders.Select(order => new RoutingNode(
                order.Id,
                (double)order.Address!.Latitude!.Value,
                (double)order.Address.Longitude!.Value,
                order.Type == OrderType.Reservation ? order.PrepareAt ?? order.CreatedAt : order.CreatedAt,
                estimates.TryGetValue(order.Id, out var estimate) ? estimate.EstimatedReadyAtUtc : nowUtc,
                order.Status == OrderStatus.Ready,
                Math.Max(0, _options.ServiceSecondsPerOrder))).ToList();
            matrix = _matrixProvider.Create(
                (double)branch.Latitude.Value,
                (double)branch.Longitude.Value,
                nodes);
            optimization = _optimizer.Optimize(new DeliveryRouteOptimizationRequest(
                matrix,
                capacity.Slots,
                nowUtc));
            warnings.AddRange(optimization.Warnings);
        }

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var activePlans = await _db.DeliveryRoutingPlans
            .Where(x => x.BranchId == branchId && x.Status == DeliveryRoutingPlanStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (var active in activePlans)
            active.Status = DeliveryRoutingPlanStatus.Superseded;
        if (activePlans.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
        var generation = (await _db.DeliveryRoutingPlans
            .Where(x => x.BranchId == branchId)
            .MaxAsync(x => (long?)x.GenerationNumber, cancellationToken) ?? 0) + 1;
        var fingerprint = Fingerprint(orders, capacity, estimates);
        var plan = new DeliveryRoutingPlan
        {
            BranchId = branchId,
            GenerationNumber = generation,
            Status = DeliveryRoutingPlanStatus.Active,
            GeneratedAtUtc = nowUtc,
            InputFingerprint = fingerprint,
            AvailableSlotCount = capacity.AvailableNow,
            SoonSlotCount = capacity.AvailableSoon,
            SolverDurationMs = optimization.SolverDurationMs,
            MatrixSource = matrix?.Source ?? RoutingMatrixSource.Approximate,
            Warnings = JoinWarnings(warnings),
        };
        _db.DeliveryRoutingPlans.Add(plan);
        await _db.SaveChangesAsync(cancellationToken);

        var orderById = orders.ToDictionary(x => x.Id);
        var nodeIndexByOrderId = validOrders
            .Select((order, index) => new { order.Id, Index = index })
            .ToDictionary(x => x.Id, x => x.Index);
        var proposedOrderIds = new HashSet<int>();
        var proposalSequence = 1;
        foreach (var optimized in optimization.Routes)
        {
            var routeOrders = optimized.NodeIndexes.Select(index => validOrders[index]).ToList();
            if (routeOrders.Count == 0)
                continue;
            foreach (var order in routeOrders)
                proposedOrderIds.Add(order.Id);

            var proposal = await CreateProposalAsync(
                plan,
                proposalSequence++,
                optimized,
                routeOrders,
                matrix!,
                nodeIndexByOrderId,
                capacity.Slots[optimized.VehicleIndex],
                estimates,
                branch,
                nowUtc,
                cancellationToken);
            _db.DeliveryRouteProposals.Add(proposal);
        }

        var unrouted = orders.Where(x => !proposedOrderIds.Contains(x.Id)).ToList();
        foreach (var order in unrouted)
        {
            _db.DeliveryRouteProposalStops.Add(new DeliveryRouteProposalStop
            {
                DeliveryRoutingPlanId = plan.Id,
                OrderId = order.Id,
                EstimatedReadyAtUtc = estimates.TryGetValue(order.Id, out var estimate)
                    ? estimate.EstimatedReadyAtUtc
                    : nowUtc,
                ServiceSeconds = Math.Max(0, _options.ServiceSecondsPerOrder),
                WasReadyAtGeneration = order.Status == OrderStatus.Ready,
                UnroutedReason = order.Address?.Latitude is null || order.Address.Longitude is null
                    ? "requiresLocation"
                    : capacity.Slots.Count == 0
                        ? "noCapacity"
                        : "notSelectedByOptimizer",
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        var persisted = await LoadPlanEntityAsync(branchId, cancellationToken)
                        ?? throw new InvalidOperationException("No fue posible cargar el plan generado.");
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation(
            "DeliveryRoutePlanGenerated BranchId={BranchId} PlanId={PlanId} Version={Version} Orders={Orders} Proposals={Proposals} Unrouted={Unrouted} AvailableNow={AvailableNow} AvailableSoon={AvailableSoon} SolverDurationMs={SolverDurationMs} MatrixSource={MatrixSource}",
            branchId,
            persisted.Id,
            persisted.GenerationNumber,
            orders.Count,
            persisted.Proposals.Count,
            persisted.Stops.Count(x => x.DeliveryRouteProposalId == null),
            capacity.AvailableNow,
            capacity.AvailableSoon,
            persisted.SolverDurationMs,
            persisted.MatrixSource);
        await _notifications.NotifyDeliveryRoutingPlanChanged(branchId, persisted.Id, persisted.GenerationNumber);
        return Map(persisted);
    }

    public async Task<DeliveryRouteProposalDto> PreviewAsync(
        int branchId,
        IReadOnlyList<int> orderIds,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = orderIds.Distinct().ToArray();
        if (distinctIds.Length == 0)
            throw new BusinessException("Selecciona al menos un pedido.");
        var nowUtc = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc);
        var branch = await _db.Branches.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == branchId, cancellationToken)
            ?? throw new NotFoundException("Sucursal no encontrada.");
        if (branch.Latitude is null || branch.Longitude is null)
            throw new BusinessException("La sucursal no tiene coordenadas configuradas.");
        var ordersById = await _db.Orders.AsNoTracking()
            .Include(x => x.Address).ThenInclude(x => x!.Neighborhood)
            .Where(x => x.BranchId == branchId && distinctIds.Contains(x.Id)
                        && (x.Status == OrderStatus.Taken || x.Status == OrderStatus.InPreparation || x.Status == OrderStatus.Ready)
                        && x.DeliveryManId == null && x.DeliveryRouteId == null
                        && string.IsNullOrEmpty(x.ExternalFulfillmentProvider))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (ordersById.Count != distinctIds.Length)
            throw new BusinessException("La seleccion contiene pedidos que ya no pertenecen al plan activo.");
        var orders = distinctIds.Select(id => ordersById[id]).ToList();
        if (orders.Any(x => x.Address?.Latitude is null || x.Address.Longitude is null))
            throw new BusinessException("Todos los pedidos seleccionados deben tener coordenadas.");
        var estimates = await _kitchenEstimator.EstimateAsync(branchId, distinctIds, nowUtc, cancellationToken);
        var nodes = orders.Select(order => new RoutingNode(
            order.Id,
            (double)order.Address!.Latitude!.Value,
            (double)order.Address.Longitude!.Value,
            order.Type == OrderType.Reservation ? order.PrepareAt ?? order.CreatedAt : order.CreatedAt,
            estimates[order.Id].EstimatedReadyAtUtc,
            order.Status == OrderStatus.Ready,
            Math.Max(0, _options.ServiceSecondsPerOrder))).ToList();
        var matrix = _matrixProvider.Create((double)branch.Latitude.Value, (double)branch.Longitude.Value, nodes);
        var stops = new List<DeliveryRoutingStopDto>(orders.Count);
        var elapsed = 0;
        var distanceMeters = 0;
        var previous = 0;
        var worstAge = 0;
        for (var index = 0; index < orders.Count; index++)
        {
            var order = orders[index];
            var matrixIndex = index + 1;
            var travel = (int)matrix.DurationSeconds[previous, matrixIndex];
            elapsed = Math.Max(elapsed, Math.Max(0, (int)(estimates[order.Id].EstimatedReadyAtUtc - nowUtc).TotalSeconds));
            elapsed += travel;
            distanceMeters += (int)matrix.DistanceMeters[previous, matrixIndex];
            var arrival = nowUtc.AddSeconds(elapsed);
            var priorityAnchor = order.Type == OrderType.Reservation ? order.PrepareAt ?? order.CreatedAt : order.CreatedAt;
            worstAge = Math.Max(worstAge, Math.Max(0, (int)(arrival - priorityAnchor).TotalSeconds));
            stops.Add(new DeliveryRoutingStopDto(
                order.Id, index + 1, Camel(order.Type?.ToString() ?? string.Empty), Camel(order.Status.ToString()),
                order.Address!.AddressText, order.Address.AdditionalInfo, order.Address.Neighborhood?.Name,
                estimates[order.Id].EstimatedReadyAtUtc, arrival, travel, nodes[index].ServiceSeconds,
                matrix.BearingFromBranchDegrees[matrixIndex], order.Status == OrderStatus.Ready,
                order.Status != OrderStatus.Ready, null));
            elapsed += nodes[index].ServiceSeconds;
            previous = matrixIndex;
        }
        var readyIds = orders.Where(x => x.Status == OrderStatus.Ready).Select(x => x.Id).ToArray();
        var waitIds = orders.Where(x => x.Status != OrderStatus.Ready).Select(x => x.Id).ToArray();
        return new DeliveryRouteProposalDto(
            0,
            0,
            "preview",
            waitIds.Length > 0 ? "wait" : "leaveNow",
            _clock.UtcNow,
            orders.Where(x => x.Status != OrderStatus.Ready).Select(x => Math.Max(0, (int)(estimates[x.Id].EstimatedReadyAtUtc - nowUtc).TotalSeconds)).DefaultIfEmpty(0).Max(),
            stops.Sum(x => x.TravelFromPreviousSeconds),
            distanceMeters,
            null,
            null,
            "notRequested",
            elapsed,
            worstAge,
            DirectionSpread(stops.Select(x => x.BearingFromBranchDegrees)),
            0,
            readyIds.Length > 0,
            waitIds.Length == 0,
            readyIds,
            waitIds,
            null,
            stops);
    }

    private async Task<DeliveryRouteProposal> CreateProposalAsync(
        DeliveryRoutingPlan plan,
        int sequence,
        OptimizedRoute optimized,
        IReadOnlyList<Order> routeOrders,
        RoutingCostMatrix matrix,
        IReadOnlyDictionary<int, int> nodeIndexByOrderId,
        RoutingVehicleSlot slot,
        IReadOnlyDictionary<int, KitchenPreparationEstimate> estimates,
        Branch branch,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var readyOrders = routeOrders.Where(x => x.Status == OrderStatus.Ready).ToList();
        var waitOrders = routeOrders.Where(x => x.Status != OrderStatus.Ready).ToList();
        var maxReadyAt = waitOrders
            .Select(x => estimates[x.Id].EstimatedReadyAtUtc)
            .DefaultIfEmpty(nowUtc)
            .Max();
        var waitSeconds = Math.Max(0, (int)(maxReadyAt - nowUtc).TotalSeconds);
        var expectedDeparture = nowUtc.AddSeconds(Math.Max(slot.AvailableAtSeconds,
            readyOrders.Count > 0 && waitSeconds <= _options.KitchenWaitReferenceSeconds ? waitSeconds : 0));
        var recommendation = slot.AvailableAtSeconds > 0
            ? DeliveryRouteRecommendation.Next
            : waitOrders.Count > 0 && readyOrders.Count > 0 && waitSeconds <= _options.KitchenWaitReferenceSeconds
                ? DeliveryRouteRecommendation.Wait
                : DeliveryRouteRecommendation.LeaveNow;
        var proposal = new DeliveryRouteProposal
        {
            DeliveryRoutingPlanId = plan.Id,
            Sequence = sequence,
            Status = DeliveryRouteProposalStatus.Available,
            Recommendation = recommendation,
            ExpectedDepartureAtUtc = expectedDeparture,
            WaitSeconds = recommendation == DeliveryRouteRecommendation.Wait ? waitSeconds : 0,
            ApproximateDrivingDurationSeconds = 0,
            ApproximateDistanceMeters = 0,
            GoogleValidationStatus = GoogleRouteValidationStatus.NotRequested,
            DirectionSpreadDegrees = DirectionSpread(routeOrders.Select(x => matrix.BearingFromBranchDegrees[nodeIndexByOrderId[x.Id] + 1])),
            Score = optimized.Score,
        };

        var elapsed = Math.Max(0, slot.AvailableAtSeconds);
        var previousMatrixIndex = 0;
        for (var index = 0; index < routeOrders.Count; index++)
        {
            var order = routeOrders[index];
            var matrixIndex = nodeIndexByOrderId[order.Id] + 1;
            var travel = (int)matrix.DurationSeconds[previousMatrixIndex, matrixIndex];
            var distance = (int)matrix.DistanceMeters[previousMatrixIndex, matrixIndex];
            proposal.ApproximateDrivingDurationSeconds += travel;
            proposal.ApproximateDistanceMeters += distance;
            elapsed = Math.Max(elapsed, Math.Max(0, (int)(estimates[order.Id].EstimatedReadyAtUtc - nowUtc).TotalSeconds));
            elapsed += travel;
            var arrival = nowUtc.AddSeconds(elapsed);
            var priorityAnchor = order.Type == OrderType.Reservation ? order.PrepareAt ?? order.CreatedAt : order.CreatedAt;
            proposal.WorstAgeAtDeliverySeconds = Math.Max(
                proposal.WorstAgeAtDeliverySeconds,
                Math.Max(0, (int)(arrival - priorityAnchor).TotalSeconds));
            proposal.Stops.Add(new DeliveryRouteProposalStop
            {
                DeliveryRoutingPlanId = plan.Id,
                OrderId = order.Id,
                StopSequence = index + 1,
                EstimatedReadyAtUtc = estimates[order.Id].EstimatedReadyAtUtc,
                EstimatedArrivalAtUtc = arrival,
                TravelFromPreviousSeconds = travel,
                ServiceSeconds = matrix.Nodes[matrixIndex - 1].ServiceSeconds,
                BearingFromBranchDegrees = matrix.BearingFromBranchDegrees[matrixIndex],
                WasReadyAtGeneration = order.Status == OrderStatus.Ready,
                IsSuggestedWait = order.Status != OrderStatus.Ready,
            });
            elapsed += matrix.Nodes[matrixIndex - 1].ServiceSeconds;
            previousMatrixIndex = matrixIndex;
        }
        proposal.LastDeliverySeconds = Math.Max(0, elapsed - slot.AvailableAtSeconds);

        if (sequence <= Math.Max(0, _options.MaximumFinalistsToValidate))
        {
            var waypoints = new List<(double Latitude, double Longitude)>
            {
                ((double)branch.Latitude!.Value, (double)branch.Longitude!.Value)
            };
            waypoints.AddRange(routeOrders.Select(x => ((double)x.Address!.Latitude!.Value, (double)x.Address.Longitude!.Value)));
            waypoints.Add(((double)branch.Latitude.Value, (double)branch.Longitude.Value));
            var started = System.Diagnostics.Stopwatch.StartNew();
            var metrics = await _googleRoutes.ComputeRouteAsync(waypoints, cancellationToken);
            started.Stop();
            if (metrics.DurationSeconds > 0 && metrics.DistanceMeters > 0)
            {
                proposal.ValidatedDrivingDurationSeconds = Math.Max(0, metrics.DurationSeconds - metrics.ReturnDurationSeconds);
                proposal.ValidatedDistanceMeters = Math.Max(0, metrics.DistanceMeters - metrics.ReturnDistanceMeters);
                proposal.GoogleValidationStatus = GoogleRouteValidationStatus.Validated;
                _logger.LogInformation(
                    "ComputeRoutesSuccess GoogleDurationMs={GoogleDurationMs} ValidatedRoutesCount=1",
                    started.ElapsedMilliseconds);
            }
            else
            {
                proposal.GoogleValidationStatus = GoogleRouteValidationStatus.Degraded;
                proposal.PlanningWarnings = "Google no pudo validar la ruta; se conservan las metricas aproximadas.";
                _logger.LogWarning(
                    "ComputeRoutesFailures GoogleDurationMs={GoogleDurationMs} ValidatedRoutesCount=0",
                    started.ElapsedMilliseconds);
            }
        }

        return proposal;
    }

    private Task<DeliveryRoutingPlan?> LoadPlanEntityAsync(int branchId, CancellationToken cancellationToken) =>
        _db.DeliveryRoutingPlans
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Proposals)
            .ThenInclude(x => x.Stops)
            .ThenInclude(x => x.Order)
            .ThenInclude(x => x.Address)
            .ThenInclude(x => x!.Neighborhood)
            .Include(x => x.Proposals)
            .ThenInclude(x => x.ClaimedByDeliveryman)
            .Include(x => x.Stops)
            .ThenInclude(x => x.Order)
            .ThenInclude(x => x.Address)
            .ThenInclude(x => x!.Neighborhood)
            .Where(x => x.BranchId == branchId && x.Status == DeliveryRoutingPlanStatus.Active)
            .OrderByDescending(x => x.GenerationNumber)
            .FirstOrDefaultAsync(cancellationToken);

    private DeliveryRoutingPlanDto Map(DeliveryRoutingPlan plan)
    {
        var proposals = plan.Proposals.OrderBy(x => x.Sequence).Select(proposal =>
        {
            var stops = proposal.Stops.OrderBy(x => x.StopSequence).Select(MapStop).ToList();
            var readyIds = stops.Where(x => x.IsReady).Select(x => x.OrderId).ToArray();
            var waitIds = stops.Where(x => x.IsSuggestedWait).Select(x => x.OrderId).ToArray();
            return new DeliveryRouteProposalDto(
                proposal.Id,
                proposal.Sequence,
                Camel(proposal.Status.ToString()),
                Camel(proposal.Recommendation.ToString()),
                proposal.ExpectedDepartureAtUtc,
                proposal.WaitSeconds,
                proposal.ApproximateDrivingDurationSeconds,
                proposal.ApproximateDistanceMeters,
                proposal.ValidatedDrivingDurationSeconds,
                proposal.ValidatedDistanceMeters,
                Camel(proposal.GoogleValidationStatus.ToString()),
                proposal.LastDeliverySeconds,
                proposal.WorstAgeAtDeliverySeconds,
                proposal.DirectionSpreadDegrees,
                proposal.Score,
                readyIds.Length > 0,
                waitIds.Length == 0,
                readyIds,
                waitIds,
                proposal.PlanningWarnings,
                stops);
        }).ToList();
        var proposalOrderIds = proposals.SelectMany(x => x.Stops).Select(x => x.OrderId).ToHashSet();
        var unrouted = plan.Stops
            .Where(x => x.DeliveryRouteProposalId == null)
            .Where(x => !proposalOrderIds.Contains(x.OrderId))
            .Select(MapStop)
            .ToList();
        return new DeliveryRoutingPlanDto(
            plan.Id,
            plan.GenerationNumber,
            Camel(plan.Status.ToString()),
            plan.GeneratedAtUtc,
            Camel(plan.MatrixSource.ToString()),
            new DeliveryRoutingCapacityDto(plan.AvailableSlotCount, plan.SoonSlotCount),
            plan.SolverDurationMs,
            plan.Warnings,
            proposals,
            unrouted);
    }

    private static DeliveryRoutingStopDto MapStop(DeliveryRouteProposalStop stop)
    {
        var address = stop.Order.Address;
        return new DeliveryRoutingStopDto(
            stop.OrderId,
            stop.StopSequence,
            Camel(stop.Order.Type?.ToString() ?? string.Empty),
            Camel(stop.Order.Status.ToString()),
            address?.AddressText ?? stop.Order.ExternalDeliveryAddress ?? string.Empty,
            address?.AdditionalInfo,
            address?.Neighborhood?.Name,
            stop.EstimatedReadyAtUtc,
            stop.EstimatedArrivalAtUtc,
            stop.TravelFromPreviousSeconds,
            stop.ServiceSeconds,
            stop.BearingFromBranchDegrees,
            stop.Order.Status == OrderStatus.Ready,
            stop.IsSuggestedWait && stop.Order.Status != OrderStatus.Ready,
            stop.UnroutedReason);
    }

    private static double DirectionSpread(IEnumerable<double> bearings)
    {
        var values = bearings.ToArray();
        var maximum = 0d;
        for (var i = 0; i < values.Length; i++)
        for (var j = i + 1; j < values.Length; j++)
        {
            var difference = Math.Abs(values[i] - values[j]);
            maximum = Math.Max(maximum, Math.Min(difference, 360 - difference));
        }
        return Math.Round(maximum, 2);
    }

    private static string Fingerprint(
        IReadOnlyList<Order> orders,
        DeliveryRoutingCapacity capacity,
        IReadOnlyDictionary<int, KitchenPreparationEstimate> estimates)
    {
        var raw = string.Join('|', orders.Select(x =>
            $"{x.Id}:{x.Status}:{x.Type}:{x.Address?.Latitude}:{x.Address?.Longitude}:{estimates.GetValueOrDefault(x.Id)?.EstimatedReadyAtUtc:O}"))
                  + $"|{capacity.AvailableNow}:{capacity.AvailableSoon}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static string? JoinWarnings(IEnumerable<string> warnings)
    {
        var result = string.Join('\n', warnings.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string Camel(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}

using System.Diagnostics;
using Google.OrTools.ConstraintSolver;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.Services;

public sealed class OrToolsDeliveryRouteOptimizer : IDeliveryRouteOptimizer
{
    private readonly DeliveryRoutingOptions _options;
    public OrToolsDeliveryRouteOptimizer(IOptions<DeliveryRoutingOptions> options) => _options = options.Value;

    public DeliveryRouteOptimizationResult Optimize(DeliveryRouteOptimizationRequest request)
    {
        if (request.Matrix.Nodes.Count == 0 || request.Vehicles.Count == 0)
            return new DeliveryRouteOptimizationResult([], Enumerable.Range(0, request.Matrix.Nodes.Count).ToArray(), 0,
                request.Vehicles.Count == 0 ? ["No hay capacidad de salida disponible."] : []);

        var stopwatch = Stopwatch.StartNew();
        var budget = Math.Clamp(_options.SolverTimeLimitMs, 1, 30_000);
        var initial = Solve(request, Math.Max(1, budget / 2));
        var remaining = budget - (int)stopwatch.ElapsedMilliseconds;
        if (remaining < 1 || initial.Routes.Count == 0)
            return initial with { SolverDurationMs = (int)stopwatch.ElapsedMilliseconds };

        var included = initial.Routes.SelectMany(route => route.NodeIndexes).ToHashSet();
        var refined = Solve(request, remaining, included, initial.Routes);
        var result = refined.Routes.Count > 0 && refined.Routes.Sum(route => route.NodeIndexes.Count) == included.Count
            ? refined : initial;
        return result with { SolverDurationMs = (int)stopwatch.ElapsedMilliseconds };
    }

    private DeliveryRouteOptimizationResult Solve(DeliveryRouteOptimizationRequest request, int milliseconds,
        HashSet<int>? included = null, IReadOnlyList<OptimizedRoute>? initialRoutes = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var warnings = request.Matrix.Warnings.ToList();
        var nodeCount = request.Matrix.Nodes.Count + 1;
        var horizon = Math.Max(86_400, Math.Min(604_800, _options.SoftLastDeliveryTargetSeconds * 4L));
        var geographicWeight = included is null ? 0 : SecondaryCostBound(request, horizon) + 1;
        var corridors = included is null ? null : RoutingDirectionHelper.GroupCorridors(request.Matrix, included,
            request.Vehicles.Count(vehicle => vehicle.AvailableAtSeconds <= horizon));
        var corridorCosts = new long[nodeCount, nodeCount];
        long maximumCorridorCost = 0;
        if (included is not null)
            foreach (var from in included)
            foreach (var to in included)
                maximumCorridorCost = Math.Max(maximumCorridorCost,
                    corridorCosts[from + 1, to + 1] = CorridorCost(request.Matrix, from + 1, to + 1));
        var crossingCost = maximumCorridorCost * (request.Matrix.Nodes.Count + (long)request.Vehicles.Count) + 1;
        using var manager = new RoutingIndexManager(nodeCount, request.Vehicles.Count, 0);
        using var routing = new RoutingModel(manager);
        var transit = routing.RegisterTransitCallback((fromIndex, toIndex) =>
        {
            var from = manager.IndexToNode(fromIndex);
            var to = manager.IndexToNode(toIndex);
            return Math.Max(0, request.Matrix.DurationSeconds[from, to])
                + (from == 0 ? 0 : Math.Max(0, request.Matrix.Nodes[from - 1].ServiceSeconds));
        });
        var cost = routing.RegisterTransitCallback((fromIndex, toIndex) =>
        {
            var from = manager.IndexToNode(fromIndex);
            var to = manager.IndexToNode(toIndex);
            return Math.Max(0, request.Matrix.DurationSeconds[from, to])
                + (from == 0 ? 0 : Math.Max(0, request.Matrix.Nodes[from - 1].ServiceSeconds))
                + (included is null
                    ? DirectionPenalty(request.Matrix.BearingFromBranchDegrees, from, to, _options.DirectionPenaltyPerDegreeSeconds)
                    : (from > 0 && to > 0 && corridors![from] != corridors[to]
                        ? crossingCost + corridorCosts[from, to] : 0) * geographicWeight);
        });
        // Direction preferences are a cost, never fictional driving time.
        routing.SetArcCostEvaluatorOfAllVehicles(cost);
        routing.SetFixedCostOfAllVehicles(Math.Max(0, _options.RouteDispatchCostSeconds));
        routing.AddDimension(transit, 0, horizon, false, "Time");
        var time = routing.GetMutableDimension("Time");
        var solver = routing.solver();
        for (var vehicle = 0; vehicle < request.Vehicles.Count; vehicle++)
        {
            var available = Math.Max(0, request.Vehicles[vehicle].AvailableAtSeconds);
            if (available > horizon)
            {
                // An unavailable slot cannot depart now or make an empty native domain.
                routing.NextVar(routing.Start(vehicle)).SetValue(routing.End(vehicle));
                continue;
            }
            time.CumulVar(routing.Start(vehicle)).SetRange(available, horizon);
            time.SetCumulVarSoftUpperBound(routing.Start(vehicle), available, 2);
            routing.AddVariableMinimizedByFinalizer(time.CumulVar(routing.Start(vehicle)));
        }
        for (var nodeIndex = 1; nodeIndex < nodeCount; nodeIndex++)
        {
            var node = request.Matrix.Nodes[nodeIndex - 1];
            var index = manager.NodeToIndex(nodeIndex);
            if (included is not null)
                routing.ActiveVar(index).SetValue(included.Contains(nodeIndex - 1) ? 1 : 0);
            var readyIn = Math.Max(0, (long)Math.Ceiling((node.EstimatedReadyAtUtc - request.GeneratedAtUtc).TotalSeconds));
            var age = Math.Max(0, (long)(request.GeneratedAtUtc - node.PriorityAnchorUtc).TotalSeconds);
            routing.AddDisjunction([index], Math.Max(1, _options.DroppedOrderBasePenaltySeconds)
                * (node.IsReady ? 2L : 1L) + age * 2);
            if (readyIn > horizon)
            {
                routing.NextVar(index).SetValue(index);
                warnings.Add("Pedidos fuera del horizonte: no se incluyen en salidas inmediatas.");
                continue;
            }
            for (var vehicle = 0; vehicle < request.Vehicles.Count; vehicle++)
            {
                var usesVehicle = solver.MakeIsEqualCstVar(routing.VehicleVar(index), vehicle);
                var departure = time.CumulVar(routing.Start(vehicle));
                solver.Add(departure >= readyIn * usesVehicle);
                if (node.WaitDeadlineAtUtc is { } deadline)
                {
                    var latest = Math.Max(Math.Max(0, request.Vehicles[vehicle].AvailableAtSeconds),
                        Math.Max(0, (long)(deadline - request.GeneratedAtUtc).TotalSeconds));
                    solver.Add(departure <= latest + horizon * (1 - usesVehicle));
                }
            }
            time.SetCumulVarSoftUpperBound(index, Math.Max(0, _options.SoftLastDeliveryTargetSeconds - age), 10);
            if (!node.IsReady && node.ReadyWithinWindowProbability < .6)
            {
                for (var other = 1; other < nodeCount; other++)
                {
                    if (!request.Matrix.Nodes[other - 1].IsReady) continue;
                    var readyIndex = manager.NodeToIndex(other);
                    var sameVehicle = solver.MakeIsEqualVar(routing.VehicleVar(index), routing.VehicleVar(readyIndex));
                    solver.Add(sameVehicle + routing.ActiveVar(index) + routing.ActiveVar(readyIndex) <= 2);
                }
            }
        }
        var bearings = request.Matrix.BearingFromBranchDegrees;
        var sectors = bearings.Select(RoutingDirectionHelper.Sector).ToArray();
        var maximumSpread = Math.Clamp(_options.MaximumRouteDirectionSpreadDegrees, 1, 90);
        for (var first = 1; first < nodeCount; first++)
        for (var second = first + 1; second < nodeCount; second++)
        {
            if (RoutingDirectionHelper.CanShare(sectors[first], bearings[first], sectors[second], bearings[second], maximumSpread))
                continue;
            var firstIndex = manager.NodeToIndex(first);
            var secondIndex = manager.NodeToIndex(second);
            var sameVehicle = solver.MakeIsEqualVar(routing.VehicleVar(firstIndex), routing.VehicleVar(secondIndex));
            solver.Add(sameVehicle + routing.ActiveVar(firstIndex) + routing.ActiveVar(secondIndex) <= 2);
        }

        var search = operations_research_constraint_solver.DefaultRoutingSearchParameters();
        search.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.ParallelCheapestInsertion;
        search.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
        search.TimeLimit = new Google.Protobuf.WellKnownTypes.Duration
        { Seconds = milliseconds / 1000, Nanos = (milliseconds % 1000) * 1_000_000 };
        using var seed = initialRoutes is null ? null : routing.ReadAssignmentFromRoutes(
            Enumerable.Range(0, request.Vehicles.Count).Select(vehicle => initialRoutes
                .Where(route => route.VehicleIndex == vehicle).SelectMany(route => route.NodeIndexes)
                .Select(index => (long)index + 1).ToArray()).ToArray(), true);
        using var solution = seed is null ? routing.SolveWithParameters(search)
            : routing.SolveFromAssignmentWithParameters(seed, search);
        stopwatch.Stop();
        if (solution is null)
            return new DeliveryRouteOptimizationResult([], Enumerable.Range(0, request.Matrix.Nodes.Count).ToArray(),
                (int)stopwatch.ElapsedMilliseconds, ["OR-Tools no encontró solución; puedes seleccionar pedidos manualmente."]);

        var routes = new List<OptimizedRoute>();
        var routed = new HashSet<int>();
        for (var vehicle = 0; vehicle < request.Vehicles.Count; vehicle++)
        {
            var nodes = new List<int>();
            long distance = 0;
            var index = routing.Start(vehicle);
            var previous = 0;
            while (!routing.IsEnd(index))
            {
                var node = manager.IndexToNode(index);
                if (node > 0)
                {
                    nodes.Add(node - 1);
                    routed.Add(node - 1);
                    distance += request.Matrix.DistanceMeters[previous, node];
                }
                previous = node;
                index = solution.Value(routing.NextVar(index));
            }
            if (nodes.Count == 0) continue;
            var duration = solution.Value(time.CumulVar(index)) - solution.Value(time.CumulVar(routing.Start(vehicle)));
            routes.Add(new OptimizedRoute(vehicle, nodes, Math.Max(0, duration), distance, solution.ObjectiveValue()));
        }
        return new DeliveryRouteOptimizationResult(routes,
            Enumerable.Range(0, request.Matrix.Nodes.Count).Where(i => !routed.Contains(i)).ToArray(),
            (int)stopwatch.ElapsedMilliseconds, warnings.Distinct().ToArray());
    }

    private static long DirectionPenalty(double[] bearings, int fromNode, int toNode, int secondsPerDegree)
    {
        if (fromNode == 0 || toNode == 0 || secondsPerDegree <= 0) return 0;
        return (long)Math.Round(RoutingDirectionHelper.Separation(bearings[fromNode], bearings[toNode]) * secondsPerDegree);
    }

    private static long CorridorCost(RoutingCostMatrix matrix, int from, int to) => from == 0 || to == 0 ? 0
        : RoutingDirectionHelper.CorridorTransitionCost(matrix.BearingFromBranchDegrees[from],
            matrix.BearingFromBranchDegrees[to], matrix.DistanceMeters[0, from], matrix.DistanceMeters[0, to],
            matrix.DistanceMeters[from, to]);

    private long SecondaryCostBound(DeliveryRouteOptimizationRequest request, long horizon)
    {
        var lateness = request.Matrix.Nodes.Sum(node => Math.Max(0L,
            (long)(request.GeneratedAtUtc - node.PriorityAnchorUtc).TotalSeconds
                - _options.SoftLastDeliveryTargetSeconds));
        return checked(horizon * (10L * request.Matrix.Nodes.Count + 3L * request.Vehicles.Count)
            + lateness * 10 + Math.Max(0, _options.RouteDispatchCostSeconds) * (long)request.Vehicles.Count);
    }
}

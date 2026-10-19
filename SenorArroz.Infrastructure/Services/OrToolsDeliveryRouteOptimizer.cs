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
        int Remaining() => Math.Max(0, budget - (int)stopwatch.ElapsedMilliseconds);
        var initial = SelectOldest(request, stopwatch, budget * 3 / 5);
        var arrivalLimits = ArrivalLimits(request, initial.Routes);
        var included = initial.Routes.SelectMany(route => route.NodeIndexes).ToHashSet();
        var refined = included.Count == 0 || Remaining() <= 0 ? initial
            : Solve(request, Math.Max(1, Remaining() * 2 / 3), included, initial.Routes, arrivalLimits);
        var result = refined.Routes.Count > 0 && refined.Routes.Sum(route => route.NodeIndexes.Count) == included.Count
            ? refined : initial;
        result = Rebalance(request, result, arrivalLimits, Remaining());
        var warnings = initial.Warnings.Concat(result.Warnings).Distinct().ToArray();
        result = result with { Warnings = warnings };
        return result with { SolverDurationMs = (int)stopwatch.ElapsedMilliseconds };
    }

    private DeliveryRouteOptimizationResult SelectOldest(DeliveryRouteOptimizationRequest request, Stopwatch timer, int budget)
    {
        var ready = Ordered(request, Enumerable.Range(0, request.Matrix.Nodes.Count).Where(i => request.Matrix.Nodes[i].IsReady)).ToArray();
        if (ready.Select(i => request.Matrix.Nodes[i].PriorityAnchorUtc).Distinct().Count() <= 1)
        {
            var fallback = EmptyPaths(request);
            foreach (var node in Ordered(request, Enumerable.Range(0, request.Matrix.Nodes.Count)))
                Insert(request, fallback, node, new Dictionary<int, long>());
            var solved = Solve(request, Math.Max(1, budget * 2 / 3 - (int)timer.ElapsedMilliseconds));
            return solved.Routes.Count > 0 ? solved : Result(request, fallback, request.Matrix.Warnings.Concat(
                ["Se conservó la propuesta factible inicial al alcanzar el límite de búsqueda."]));
        }

        var paths = EmptyPaths(request);
        var selected = new HashSet<int>();
        var reservations = new Dictionary<int, long>();
        var limited = false;
        foreach (var node in ready)
        {
            if (!Insert(request, paths, node, reservations))
            {
                if (!Enumerable.Range(0, paths.Length).Any(vehicle => Fits(request, vehicle, [node], reservations))) continue;
                var remaining = budget - (int)timer.ElapsedMilliseconds;
                if (remaining <= 0) { limited = true; continue; }
                var required = selected.Append(node).ToHashSet();
                var redistributed = Solve(request, Math.Min(120, remaining), required, arrivalLimits: reservations,
                    firstStrategy: FirstSolutionStrategy.Types.Value.PathMostConstrainedArc);
                if (!required.IsSubsetOf(redistributed.Routes.SelectMany(route => route.NodeIndexes))) continue;
                paths = Paths(request, redistributed.Routes);
            }
            selected.Add(node);
            foreach (var (path, vehicle) in paths.Select((path, vehicle) => (path, vehicle)))
            {
                var schedule = RoutingScheduleHelper.Calculate(request, vehicle, path);
                for (var position = 0; position < path.Count; position++)
                    if (request.Matrix.Nodes[path[position]].DeliveryDueAtUtc.HasValue)
                        reservations.TryAdd(path[position], RoutingScheduleHelper.LatestArrival(request, path[position], schedule.Arrivals[position], _options));
            }
        }
        return Result(request, paths, request.Matrix.Warnings.Concat(limited
            ? ["La búsqueda alcanzó su presupuesto; algunos pendientes requieren otro cálculo, no se ha demostrado que sean inviables."] : []));
    }

    private static IEnumerable<int> Ordered(DeliveryRouteOptimizationRequest request, IEnumerable<int> indexes) =>
        indexes.OrderBy(i => request.Matrix.Nodes[i].IsReady ? 0 : 1)
            .ThenBy(i => request.Matrix.Nodes[i].PriorityAnchorUtc)
            .ThenBy(i => request.Matrix.DurationSeconds[0, i + 1]).ThenBy(i => request.Matrix.Nodes[i].OrderId);

    private static List<int>[] EmptyPaths(DeliveryRouteOptimizationRequest request) =>
        Enumerable.Range(0, request.Vehicles.Count).Select(_ => new List<int>()).ToArray();

    private static List<int>[] Paths(DeliveryRouteOptimizationRequest request, IReadOnlyList<OptimizedRoute> routes) =>
        Enumerable.Range(0, request.Vehicles.Count).Select(vehicle => routes.FirstOrDefault(route => route.VehicleIndex == vehicle)
            ?.NodeIndexes.ToList() ?? []).ToArray();

    private bool Fits(DeliveryRouteOptimizationRequest request, int vehicle, IReadOnlyList<int> path, IReadOnlyDictionary<int, long> limits) =>
        RoutingScheduleHelper.Fits(request, vehicle, path, RoutingScheduleHelper.Calculate(request, vehicle, path), limits, _options);

    private bool Insert(DeliveryRouteOptimizationRequest request, List<int>[] paths, int node, IReadOnlyDictionary<int, long> limits)
    {
        (int Vehicle, List<int> Path, (long Lateness, long MaximumDelay, long TotalDelay, int NewRoute, long Geography, long Cost) Rank)? best = null;
        for (var vehicle = 0; vehicle < paths.Length; vehicle++)
        {
            var current = paths[vehicle];
            var original = RoutingScheduleHelper.Calculate(request, vehicle, current);
            for (var position = 0; position <= current.Count; position++)
            {
                var candidate = new List<int>(current);
                candidate.Insert(position, node);
                var schedule = RoutingScheduleHelper.Calculate(request, vehicle, candidate);
                if (!RoutingScheduleHelper.Fits(request, vehicle, candidate, schedule, limits, _options)) continue;
                var delays = current.Select((index, offset) => Math.Max(0, schedule.Arrivals[candidate.IndexOf(index)] - original.Arrivals[offset])).ToArray();
                var lateness = candidate.Select((index, offset) => request.Matrix.Nodes[index].DeliveryDueAtUtc.HasValue
                    ? Math.Max(0, schedule.Arrivals[offset] - RoutingScheduleHelper.DueSeconds(request, request.Matrix.Nodes[index])) : 0).Sum();
                var rank = (lateness, limits.Count == 0 ? 0 : delays.DefaultIfEmpty(0).Max(), limits.Count == 0 ? 0 : delays.Sum(),
                    current.Count == 0 ? 1 : 0, current.Count == 0 ? 0 : current.Min(other => CorridorCost(request.Matrix, other + 1, node + 1)),
                    schedule.Duration - original.Duration);
                if (best is null || rank.CompareTo(best.Value.Rank) < 0) best = (vehicle, candidate, rank);
            }
        }
        if (best is not { } chosen) return false;
        paths[chosen.Vehicle] = chosen.Path;
        return true;
    }

    private DeliveryRouteOptimizationResult Solve(DeliveryRouteOptimizationRequest request, int milliseconds,
        HashSet<int>? included = null, IReadOnlyList<OptimizedRoute>? initialRoutes = null,
        IReadOnlyDictionary<int, long>? arrivalLimits = null, FirstSolutionStrategy.Types.Value? firstStrategy = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var warnings = request.Matrix.Warnings.ToList();
        var nodeCount = request.Matrix.Nodes.Count + 1;
        const long horizon = 172_800;
        var geographic = included is not null && firstStrategy is null;
        var geographicWeight = geographic ? SecondaryCostBound(request, horizon) + 1 : 0;
        var corridors = !geographic ? null : RoutingDirectionHelper.GroupCorridors(request.Matrix, included!,
            request.Vehicles.Count(vehicle => vehicle.AvailableAtSeconds <= horizon));
        var corridorCosts = new long[nodeCount, nodeCount];
        long maximumCorridorCost = 0;
        if (geographic)
            foreach (var from in included!)
            foreach (var to in included!)
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
                + (!geographic
                    ? DirectionPenalty(request.Matrix.BearingFromBranchDegrees, from, to, _options.DirectionPenaltyPerDegreeSeconds)
                    : (from > 0 && to > 0 && corridors![from] != corridors[to]
                        ? crossingCost + corridorCosts[from, to] : 0) * geographicWeight);
        });
        routing.SetArcCostEvaluatorOfAllVehicles(cost);
        routing.SetFixedCostOfAllVehicles(Math.Max(0, _options.RouteDispatchCostSeconds));
        routing.AddDimension(transit, 0, horizon, false, "Time");
        var time = routing.GetMutableDimension("Time");
        routing.AddDimension(transit, 0, horizon, true, "RouteElapsed");
        var elapsed = routing.GetMutableDimension("RouteElapsed");
        var solver = routing.solver();
        var youngest = request.Matrix.Nodes.Max(node => node.PriorityAnchorUtc);
        for (var vehicle = 0; vehicle < request.Vehicles.Count; vehicle++)
        {
            var available = Math.Max(0, request.Vehicles[vehicle].AvailableAtSeconds);
            if (available > 86_400)
            {
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
            time.CumulVar(index).SetMax(86_400);
            if (included is not null)
                routing.ActiveVar(index).SetValue(included.Contains(nodeIndex - 1) ? 1 : 0);
            var readyIn = Math.Max(0, (long)Math.Ceiling((node.EstimatedReadyAtUtc - request.GeneratedAtUtc).TotalSeconds));
            var age = Math.Max(0, (long)(request.GeneratedAtUtc - node.PriorityAnchorUtc).TotalSeconds);
            routing.AddDisjunction([index], Math.Max(1, _options.DroppedOrderBasePenaltySeconds)
                * (node.IsReady ? 2L : 1L) + age * 2);
            if (readyIn > 86_400)
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
                solver.Add(time.CumulVar(index) <= departure
                    + (node.DeliveryTimeLimitSeconds ?? (included is null
                        ? Math.Clamp(_options.MaximumDeliveryAfterDepartureSeconds, 60, 86_400) : RoutingScheduleHelper.NormalLimit(_options)))
                    + horizon * (1 - usesVehicle));
                if (node.WaitDeadlineAtUtc is { } deadline)
                {
                    var latest = Math.Max(Math.Max(0, request.Vehicles[vehicle].AvailableAtSeconds),
                        Math.Max(0, (long)(deadline - request.GeneratedAtUtc).TotalSeconds));
                    solver.Add(departure <= latest + horizon * (1 - usesVehicle));
                }
            }
            time.SetCumulVarSoftUpperBound(index, Math.Max(0, _options.SoftLastDeliveryTargetSeconds), 10);
            elapsed.SetCumulVarSoftUpperBound(index, 0, 1 + Math.Min(20, Math.Max(0, (long)(youngest - node.PriorityAnchorUtc).TotalSeconds) / 300));
            if (node.DeliveryDueAtUtc.HasValue)
                time.SetCumulVarSoftUpperBound(index, RoutingScheduleHelper.DueSeconds(request, node), 100);
            if (arrivalLimits?.TryGetValue(nodeIndex - 1, out var latestArrival) == true)
                time.CumulVar(index).SetMax(latestArrival);
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
        for (var first = 1; first < nodeCount; first++)
        for (var second = first + 1; second < nodeCount; second++)
        {
            if (RoutingScheduleHelper.CanShare(request.Matrix, first - 1, second - 1, _options))
                continue;
            var firstIndex = manager.NodeToIndex(first);
            var secondIndex = manager.NodeToIndex(second);
            var sameVehicle = solver.MakeIsEqualVar(routing.VehicleVar(firstIndex), routing.VehicleVar(secondIndex));
            solver.Add(sameVehicle + routing.ActiveVar(firstIndex) + routing.ActiveVar(secondIndex) <= 2);
        }

        var search = operations_research_constraint_solver.DefaultRoutingSearchParameters();
        search.FirstSolutionStrategy = firstStrategy ?? FirstSolutionStrategy.Types.Value.ParallelCheapestInsertion;
        search.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
        if (firstStrategy.HasValue) search.SolutionLimit = 1;
        milliseconds = Math.Max(1, milliseconds - (int)stopwatch.ElapsedMilliseconds);
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

    private Dictionary<int, long> ArrivalLimits(DeliveryRouteOptimizationRequest request,
        IReadOnlyList<OptimizedRoute> routes)
    {
        var limits = new Dictionary<int, long>();
        foreach (var route in routes)
        {
            var schedule = RoutingScheduleHelper.Calculate(request, route.VehicleIndex, route.NodeIndexes);
            for (var i = 0; i < route.NodeIndexes.Count; i++)
                limits[route.NodeIndexes[i]] = RoutingScheduleHelper.LatestArrival(request, route.NodeIndexes[i], schedule.Arrivals[i], _options);
        }
        return limits;
    }

    private DeliveryRouteOptimizationResult Rebalance(DeliveryRouteOptimizationRequest request,
        DeliveryRouteOptimizationResult result, Dictionary<int, long> limits, int budgetMs)
    {
        var timer = Stopwatch.StartNew();
        var paths = Paths(request, result.Routes);
        var corridors = RoutingDirectionHelper.GroupCorridors(request.Matrix,
            Enumerable.Range(0, request.Matrix.Nodes.Count).ToHashSet(), request.Vehicles.Count);
        var baseline = result.Routes.SelectMany(route =>
        {
            var schedule = RoutingScheduleHelper.Calculate(request, route.VehicleIndex, route.NodeIndexes);
            return route.NodeIndexes.Select((node, position) => (node, arrival: schedule.Arrivals[position]));
        }).ToDictionary(pair => pair.node, pair => pair.arrival);
        Complete(paths, limits);
        var rank = Rank(paths);
        while (timer.ElapsedMilliseconds < budgetMs)
        {
            List<int>[]? best = null;
            Dictionary<int, long>? bestLimits = null;
            var bestRank = rank;
            for (var from = 0; from < paths.Length && timer.ElapsedMilliseconds < budgetMs; from++)
            for (var position = 0; position < paths[from].Count && timer.ElapsedMilliseconds < budgetMs; position++)
            {
                for (var to = 0; to < paths.Length && timer.ElapsedMilliseconds < budgetMs; to++)
                for (var offset = 0; offset <= paths[to].Count && timer.ElapsedMilliseconds < budgetMs; offset++)
                {
                    var candidate = paths.Select(path => new List<int>(path)).ToArray();
                    var node = candidate[from][position];
                    candidate[from].RemoveAt(position);
                    if (offset > candidate[to].Count) continue;
                    candidate[to].Insert(offset, node);
                    Evaluate(candidate);
                    if (from == to || offset >= paths[to].Count) continue;
                    candidate = paths.Select(path => new List<int>(path)).ToArray();
                    (candidate[from][position], candidate[to][offset]) = (candidate[to][offset], candidate[from][position]);
                    Evaluate(candidate);
                }
            }
            if (best is null) break;
            paths = best;
            rank = bestRank;
            foreach (var pair in bestLimits!) limits.TryAdd(pair.Key, pair.Value);

            void Evaluate(List<int>[] candidate)
            {
                if (timer.ElapsedMilliseconds >= budgetMs || !candidate.Select((path, vehicle) => Fits(request, vehicle, path, limits)).All(fits => fits)) return;
                var candidateLimits = new Dictionary<int, long>(limits);
                Complete(candidate, candidateLimits);
                var candidateRank = Rank(candidate);
                var priority = ComparePriority(candidate, best ?? paths);
                if (priority > 0 || priority == 0 && candidateRank.CompareTo(bestRank) >= 0) return;
                best = candidate;
                bestLimits = candidateLimits;
                bestRank = candidateRank;
            }
        }
        return Result(request, paths, result.Warnings);

        void Complete(List<int>[] candidate, Dictionary<int, long> candidateLimits)
        {
            var selected = candidate.SelectMany(path => path).ToHashSet();
            foreach (var node in Ordered(request, Enumerable.Range(0, request.Matrix.Nodes.Count).Where(i => !selected.Contains(i))))
            {
                if (timer.ElapsedMilliseconds >= budgetMs) break;
                if (Insert(request, candidate, node, candidateLimits)) Anchor(candidate, candidateLimits);
            }
        }

        void Anchor(List<int>[] candidate, Dictionary<int, long> candidateLimits)
        {
            foreach (var (path, vehicle) in candidate.Select((path, vehicle) => (path, vehicle)))
            {
                var schedule = RoutingScheduleHelper.Calculate(request, vehicle, path);
                for (var position = 0; position < path.Count; position++)
                {
                    candidateLimits.TryAdd(path[position], RoutingScheduleHelper.LatestArrival(request, path[position], schedule.Arrivals[position], _options));
                }
            }
        }

        int ComparePriority(List<int>[] candidate, List<int>[] other)
        {
            var selected = candidate.SelectMany(path => path).ToHashSet();
            var previous = other.SelectMany(path => path).ToHashSet();
            foreach (var group in Ordered(request, Enumerable.Range(0, request.Matrix.Nodes.Count))
                .GroupBy(index => (request.Matrix.Nodes[index].IsReady, request.Matrix.Nodes[index].PriorityAnchorUtc)))
            {
                var difference = group.Count(previous.Contains) - group.Count(selected.Contains);
                if (difference != 0) return difference;
            }
            return 0;
        }

        (int Coverage, long ReservationLateness, int Singles, int CorridorChanges, long Geography, long MaximumDelay, long TotalDelay, long Arrival, long Cost) Rank(List<int>[] candidate)
        {
            long lateness = 0, maximumDelay = 0, totalDelay = 0, geography = 0, arrivals = 0, cost = 0;
            var crossings = 0;
            foreach (var (path, vehicle) in candidate.Select((path, vehicle) => (path, vehicle)))
            {
                var schedule = RoutingScheduleHelper.Calculate(request, vehicle, path);
                cost += schedule.Duration + (path.Count > 0 ? Math.Max(0, _options.RouteDispatchCostSeconds) : 0);
                for (var position = 0; position < path.Count; position++)
                {
                    var node = request.Matrix.Nodes[path[position]];
                    var arrival = schedule.Arrivals[position];
                    var delay = baseline.TryGetValue(path[position], out var original) ? Math.Max(0, arrival - original) : 0;
                    maximumDelay = Math.Max(maximumDelay, delay);
                    totalDelay += delay;
                    if (node.DeliveryDueAtUtc.HasValue) lateness += Math.Max(0, arrival - RoutingScheduleHelper.DueSeconds(request, node));
                    arrivals += arrival * (1 + Math.Min(20, Math.Max(0, (long)(request.GeneratedAtUtc - node.PriorityAnchorUtc).TotalSeconds) / 300));
                    if (position > 0 && corridors[path[position - 1] + 1] != corridors[path[position] + 1])
                    {
                        crossings++;
                        geography += CorridorCost(request.Matrix, path[position - 1] + 1, path[position] + 1);
                    }
                }
            }
            return (-candidate.Sum(path => path.Count), lateness, candidate.Count(path => path.Count == 1), crossings, geography, maximumDelay, totalDelay, arrivals, cost);
        }
    }

    private DeliveryRouteOptimizationResult Result(DeliveryRouteOptimizationRequest request, List<int>[] paths, IEnumerable<string> warnings)
    {
        var routes = paths.Select((path, vehicle) =>
        {
            var schedule = RoutingScheduleHelper.Calculate(request, vehicle, path);
            return new OptimizedRoute(vehicle, path.ToArray(), schedule.Duration, schedule.Distance, 0);
        }).Where(route => route.NodeIndexes.Count > 0).ToArray();
        var selected = paths.SelectMany(path => path).ToHashSet();
        var pending = Enumerable.Range(0, request.Matrix.Nodes.Count).Where(i => !selected.Contains(i)).ToArray();
        var reasons = pending.ToDictionary(index => index, index => paths.All(path => path.Any(other =>
            !RoutingScheduleHelper.CanShare(request.Matrix, index, other, _options))) ? "directionConflict" : "additionalDelayOrDurationLimit");
        return new(routes, pending, 0, warnings.Distinct().ToArray()) { UnroutedReasons = reasons };
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

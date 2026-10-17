using System.Diagnostics;
using Google.OrTools.ConstraintSolver;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
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
        var warnings = request.Matrix.Warnings.ToList();
        var nodeCount = request.Matrix.Nodes.Count + 1;
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
                + DirectionPenalty(request.Matrix.BearingFromBranchDegrees, from, to, _options.DirectionPenaltyPerDegreeSeconds);
        });
        // Direction preferences are a cost, never fictional driving time.
        routing.SetArcCostEvaluatorOfAllVehicles(cost);
        routing.SetFixedCostOfAllVehicles(Math.Max(0, _options.RouteDispatchCostSeconds));
        var horizon = Math.Max(86_400, Math.Min(604_800, _options.SoftLastDeliveryTargetSeconds * 4L));
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
            var readyIn = Math.Max(0, (long)Math.Ceiling((node.EstimatedReadyAtUtc - request.GeneratedAtUtc).TotalSeconds));
            var age = Math.Max(0, (long)(request.GeneratedAtUtc - node.PriorityAnchorUtc).TotalSeconds);
            routing.AddDisjunction([index], Math.Max(1, _options.DroppedOrderBasePenaltySeconds) + age * 2);
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
        var search = operations_research_constraint_solver.DefaultRoutingSearchParameters();
        search.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.ParallelCheapestInsertion;
        search.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
        var milliseconds = Math.Clamp(_options.SolverTimeLimitMs, 1, 30_000);
        search.TimeLimit = new Google.Protobuf.WellKnownTypes.Duration
        { Seconds = milliseconds / 1000, Nanos = (milliseconds % 1000) * 1_000_000 };
        using var solution = routing.SolveWithParameters(search);
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
        var difference = Math.Abs(bearings[fromNode] - bearings[toNode]);
        return (long)Math.Round(Math.Min(difference, 360 - difference) * secondsPerDegree);
    }
}

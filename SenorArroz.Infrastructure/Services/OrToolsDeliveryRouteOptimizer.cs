using System.Diagnostics;
using Google.OrTools.ConstraintSolver;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.Services;

public sealed class OrToolsDeliveryRouteOptimizer : IDeliveryRouteOptimizer
{
    private readonly DeliveryRoutingOptions _options;

    public OrToolsDeliveryRouteOptimizer(IOptions<DeliveryRoutingOptions> options)
    {
        _options = options.Value;
    }

    public DeliveryRouteOptimizationResult Optimize(DeliveryRouteOptimizationRequest request)
    {
        if (request.Matrix.Nodes.Count == 0 || request.Vehicles.Count == 0)
        {
            return new DeliveryRouteOptimizationResult(
                [],
                Enumerable.Range(0, request.Matrix.Nodes.Count).ToArray(),
                0,
                request.Vehicles.Count == 0 ? ["No hay capacidad de salida disponible."] : []);
        }

        var stopwatch = Stopwatch.StartNew();
        var nodeCount = request.Matrix.Nodes.Count + 1;
        var manager = new RoutingIndexManager(nodeCount, request.Vehicles.Count, 0);
        var routing = new RoutingModel(manager);

        var transitCallback = routing.RegisterTransitCallback((fromIndex, toIndex) =>
        {
            var fromNode = manager.IndexToNode(fromIndex);
            var toNode = manager.IndexToNode(toIndex);
            var serviceSeconds = fromNode == 0 ? 0 : request.Matrix.Nodes[fromNode - 1].ServiceSeconds;
            var directionPenalty = DirectionPenalty(
                request.Matrix.BearingFromBranchDegrees,
                fromNode,
                toNode,
                _options.DirectionPenaltyPerDegreeSeconds);
            return request.Matrix.DurationSeconds[fromNode, toNode] + serviceSeconds + directionPenalty;
        });

        routing.SetArcCostEvaluatorOfAllVehicles(transitCallback);
        var horizon = Math.Max(86_400, _options.SoftLastDeliveryTargetSeconds * 4L);
        routing.AddDimension(transitCallback, horizon, horizon, false, "Time");
        var timeDimension = routing.GetMutableDimension("Time");

        for (var vehicle = 0; vehicle < request.Vehicles.Count; vehicle++)
        {
            var availableAt = Math.Max(0, request.Vehicles[vehicle].AvailableAtSeconds);
            timeDimension.CumulVar(routing.Start(vehicle)).SetRange(availableAt, horizon);
        }

        for (var nodeIndex = 1; nodeIndex < nodeCount; nodeIndex++)
        {
            var node = request.Matrix.Nodes[nodeIndex - 1];
            var solverIndex = manager.NodeToIndex(nodeIndex);
            var readyIn = Math.Max(0, (long)(node.EstimatedReadyAtUtc - request.GeneratedAtUtc).TotalSeconds);
            timeDimension.CumulVar(solverIndex).SetMin(readyIn);
            timeDimension.SetCumulVarSoftUpperBound(
                solverIndex,
                _options.SoftLastDeliveryTargetSeconds,
                10);

            var ageSeconds = Math.Max(0, (long)(request.GeneratedAtUtc - node.PriorityAnchorUtc).TotalSeconds);
            var dropPenalty = _options.DroppedOrderBasePenaltySeconds + ageSeconds * 2;
            routing.AddDisjunction([solverIndex], dropPenalty);
        }

        var search = operations_research_constraint_solver.DefaultRoutingSearchParameters();
        search.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.ParallelCheapestInsertion;
        search.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
        search.TimeLimit = new Google.Protobuf.WellKnownTypes.Duration
        {
            Seconds = _options.SolverTimeLimitMs / 1000,
            Nanos = (_options.SolverTimeLimitMs % 1000) * 1_000_000
        };

        var solution = routing.SolveWithParameters(search);
        stopwatch.Stop();
        if (solution is null)
        {
            return new DeliveryRouteOptimizationResult(
                [],
                Enumerable.Range(0, request.Matrix.Nodes.Count).ToArray(),
                (int)stopwatch.ElapsedMilliseconds,
                ["OR-Tools no encontro una solucion dentro del limite configurado."]);
        }

        var routes = new List<OptimizedRoute>();
        var routedNodes = new HashSet<int>();
        for (var vehicle = 0; vehicle < request.Vehicles.Count; vehicle++)
        {
            var routeNodes = new List<int>();
            long distance = 0;
            var index = routing.Start(vehicle);
            var previousNode = 0;
            while (!routing.IsEnd(index))
            {
                var node = manager.IndexToNode(index);
                if (node > 0)
                {
                    routeNodes.Add(node - 1);
                    routedNodes.Add(node - 1);
                    distance += request.Matrix.DistanceMeters[previousNode, node];
                }

                previousNode = node;
                index = solution.Value(routing.NextVar(index));
            }

            if (routeNodes.Count == 0)
                continue;

            var duration = solution.Value(timeDimension.CumulVar(index))
                           - request.Vehicles[vehicle].AvailableAtSeconds;
            routes.Add(new OptimizedRoute(
                vehicle,
                routeNodes,
                Math.Max(0, duration),
                distance,
                solution.ObjectiveValue()));
        }

        var unrouted = Enumerable.Range(0, request.Matrix.Nodes.Count)
            .Where(index => !routedNodes.Contains(index))
            .ToArray();
        return new DeliveryRouteOptimizationResult(
            routes,
            unrouted,
            (int)stopwatch.ElapsedMilliseconds,
            request.Matrix.Warnings);
    }

    private static long DirectionPenalty(double[] bearings, int fromNode, int toNode, int secondsPerDegree)
    {
        if (fromNode == 0 || toNode == 0 || secondsPerDegree <= 0)
            return 0;

        var difference = Math.Abs(bearings[fromNode] - bearings[toNode]);
        var circularDifference = Math.Min(difference, 360 - difference);
        return (long)Math.Round(circularDifference * secondsPerDegree);
    }
}

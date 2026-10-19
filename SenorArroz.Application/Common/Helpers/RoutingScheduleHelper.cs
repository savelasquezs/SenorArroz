using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;

namespace SenorArroz.Application.Common.Helpers;

public static class RoutingScheduleHelper
{
    public sealed record Schedule(long Departure, long[] Arrivals, long Duration, long Driving, long Distance);

    public static int NormalLimit(DeliveryRoutingOptions options) => Math.Clamp(
        options.MaximumCompletionDeliverySeconds, Math.Clamp(options.MaximumDeliveryAfterDepartureSeconds, 60, 86_400), 86_400);

    public static int? ExceptionLimit(long directSeconds, DeliveryRoutingOptions options) =>
        directSeconds > Math.Clamp(options.MaximumDeliveryAfterDepartureSeconds, 60, 86_400)
            ? (int)Math.Clamp(directSeconds + Math.Max(0, options.LongDistanceMarginSeconds), NormalLimit(options), 86_400)
            : null;

    public static int DeliveryLimit(RoutingNode node, DeliveryRoutingOptions options) =>
        node.DeliveryTimeLimitSeconds ?? NormalLimit(options);

    public static bool CanShare(RoutingCostMatrix matrix, int first, int second, DeliveryRoutingOptions options)
    {
        var radius = Math.Max(0, options.CentralGroupingRadiusMeters);
        double Distance(int node) => matrix.StraightDistanceFromBranchMeters.Length > node + 1
            ? matrix.StraightDistanceFromBranchMeters[node + 1] : matrix.DistanceMeters[0, node + 1];
        return Distance(first) <= radius || Distance(second) <= radius
            || RoutingDirectionHelper.CanShare(RoutingDirectionHelper.Sector(matrix.BearingFromBranchDegrees[first + 1]),
                matrix.BearingFromBranchDegrees[first + 1], RoutingDirectionHelper.Sector(matrix.BearingFromBranchDegrees[second + 1]),
                matrix.BearingFromBranchDegrees[second + 1], Math.Clamp(options.MaximumRouteDirectionSpreadDegrees, 1, 90));
    }

    public static Schedule Calculate(DeliveryRouteOptimizationRequest request, int vehicle, IReadOnlyList<int> path)
    {
        var departure = Math.Max(Math.Max(0, request.Vehicles[vehicle].AvailableAtSeconds), path.Select(index =>
            Math.Max(0, (long)Math.Ceiling((request.Matrix.Nodes[index].EstimatedReadyAtUtc - request.GeneratedAtUtc).TotalSeconds)))
            .DefaultIfEmpty(0).Max());
        var elapsed = departure;
        long driving = 0, distance = 0;
        var previous = 0;
        var arrivals = new long[path.Count];
        for (var position = 0; position < path.Count; position++)
        {
            var index = path[position] + 1;
            var travel = Math.Max(0, request.Matrix.DurationSeconds[previous, index]);
            elapsed += travel;
            driving += travel;
            distance += Math.Max(0, request.Matrix.DistanceMeters[previous, index]);
            arrivals[position] = elapsed;
            elapsed += Math.Max(0, request.Matrix.Nodes[path[position]].ServiceSeconds);
            previous = index;
        }
        return new(departure, arrivals, elapsed - departure + Math.Max(0, request.Matrix.DurationSeconds[previous, 0]), driving, distance);
    }

    public static long DueSeconds(DeliveryRouteOptimizationRequest request, RoutingNode node) =>
        node.DeliveryDueAtUtc is { } due ? Math.Max(0, (long)(due - request.GeneratedAtUtc).TotalSeconds) : long.MaxValue;

    public static long LatestArrival(DeliveryRouteOptimizationRequest request, int index, long arrival, DeliveryRoutingOptions options)
    {
        var due = DueSeconds(request, request.Matrix.Nodes[index]);
        return Math.Min(arrival + Math.Max(0, options.MaximumAdditionalDelaySeconds), Math.Max(arrival, due));
    }

    public static bool Fits(DeliveryRouteOptimizationRequest request, int vehicle, IReadOnlyList<int> path,
        Schedule schedule, IReadOnlyDictionary<int, long> limits, DeliveryRoutingOptions options)
    {
        if (schedule.Departure > 86_400) return false;
        var hasReady = path.Any(index => request.Matrix.Nodes[index].IsReady);
        for (var position = 0; position < path.Count; position++)
        {
            var index = path[position];
            var node = request.Matrix.Nodes[index];
            if (schedule.Arrivals[position] > 86_400 || schedule.Arrivals[position] - schedule.Departure > DeliveryLimit(node, options)
                || limits.TryGetValue(index, out var latest) && schedule.Arrivals[position] > latest
                || hasReady && !node.IsReady && node.ReadyWithinWindowProbability < .6) return false;
            if (node.WaitDeadlineAtUtc is { } deadline && schedule.Departure > Math.Max(
                Math.Max(0, request.Vehicles[vehicle].AvailableAtSeconds), Math.Max(0, (long)(deadline - request.GeneratedAtUtc).TotalSeconds))) return false;
            for (var other = 0; other < position; other++)
                if (!CanShare(request.Matrix, path[other], index, options)) return false;
        }
        return true;
    }
}

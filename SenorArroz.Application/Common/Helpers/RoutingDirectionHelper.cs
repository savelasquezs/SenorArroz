using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Application.Common.Helpers;

public static class RoutingDirectionHelper
{
    public static int Sector(double bearing) => (int)Math.Floor((Normalize(bearing) + 22.5) / 45) % 8;

    public static double Separation(double first, double second)
    {
        var difference = Math.Abs(Normalize(first) - Normalize(second));
        return Math.Min(difference, 360 - difference);
    }

    public static bool CanShare(int firstSector, double firstBearing, int secondSector, double secondBearing,
        double maximumSpread)
    {
        var sectorDistance = Math.Abs(firstSector - secondSector);
        sectorDistance = Math.Min(sectorDistance, 8 - sectorDistance);
        return sectorDistance <= Math.Ceiling(maximumSpread / 45)
            && Separation(firstBearing, secondBearing) <= maximumSpread + 1e-8;
    }

    public static long CorridorTransitionCost(double firstBearing, double secondBearing,
        long firstDistanceFromBranch, long secondDistanceFromBranch, long distanceBetween)
        => (long)Math.Round(CorridorSeparationCost(firstBearing, secondBearing,
            firstDistanceFromBranch, secondDistanceFromBranch, distanceBetween));

    private static double CorridorSeparationCost(double firstBearing, double secondBearing,
        long firstDistanceFromBranch, long secondDistanceFromBranch, long distanceBetween)
    {
        var separation = Separation(firstBearing, secondBearing);
        var lateralDetour = Math.Max(0d, distanceBetween
            - Math.Abs((double)firstDistanceFromBranch - secondDistanceFromBranch));
        return separation * separation / 400 + lateralDetour / 100;
    }

    public static int[] GroupCorridors(RoutingCostMatrix matrix, IReadOnlySet<int> included, int capacity)
    {
        var groups = Enumerable.Repeat(-1, matrix.Nodes.Count + 1).ToArray();
        var nodes = included.Order().Select(index => index + 1).ToArray();
        if (nodes.Length == 0 || capacity <= 0) return groups;

        var localSpacing = new double[groups.Length];
        foreach (var node in nodes)
        {
            var neighbors = nodes.Where(other => other != node)
                .Select(other => Math.Max(matrix.DistanceMeters[node, other], matrix.DistanceMeters[other, node]))
                .Order().Take(2).ToArray();
            localSpacing[node] = Math.Max(100, neighbors.Length == 0 ? 0 : neighbors[^1]);
        }

        var costs = new double[groups.Length, groups.Length];
        foreach (var first in nodes)
        foreach (var second in nodes)
        {
            var distance = Math.Max(matrix.DistanceMeters[first, second], matrix.DistanceMeters[second, first]);
            costs[first, second] = CorridorSeparationCost(matrix.BearingFromBranchDegrees[first],
                matrix.BearingFromBranchDegrees[second], matrix.DistanceMeters[0, first],
                matrix.DistanceMeters[0, second], distance)
                + 4d * distance / Math.Min(localSpacing[first], localSpacing[second]);
        }

        var clockwise = nodes.OrderBy(node => Normalize(matrix.BearingFromBranchDegrees[node])).ToArray();
        var boundary = Enumerable.Range(0, clockwise.Length).MaxBy(index => Normalize(
            matrix.BearingFromBranchDegrees[clockwise[(index + 1) % clockwise.Length]]
            - matrix.BearingFromBranchDegrees[clockwise[index]]));
        var attempts = Math.Min(8, nodes.Length);
        return Enumerable.Range(0, attempts)
            .Select(attempt => ClusterFrom(clockwise[(boundary + 1 + attempt * nodes.Length / attempts) % nodes.Length]))
            .MinBy(result => result.Score).Groups;

        (int[] Groups, double Score) ClusterFrom(int initialCenter)
        {
            var candidate = Enumerable.Repeat(-1, groups.Length).ToArray();
            var centers = new List<int> { initialCenter };
            while (centers.Count < Math.Min(capacity, nodes.Length))
            {
                var next = nodes.Where(node => !centers.Contains(node))
                    .MaxBy(node => centers.Min(center => costs[node, center]));
                if (centers.Min(center => costs[next, center]) <= 0) break;
                centers.Add(next);
            }

            for (var iteration = 0; iteration < 20; iteration++)
            {
                foreach (var node in nodes)
                    candidate[node] = centers.Contains(node) ? centers.IndexOf(node)
                        : Enumerable.Range(0, centers.Count).MinBy(group => costs[node, centers[group]]);

                var changed = false;
                for (var group = 0; group < centers.Count; group++)
                {
                    var members = nodes.Where(node => candidate[node] == group).ToArray();
                    var next = members.MinBy(first => members.Sum(second => costs[first, second]));
                    changed |= next != centers[group];
                    centers[group] = next;
                }
                if (!changed) break;
            }

            foreach (var node in nodes)
                candidate[node] = centers.Contains(node) ? centers.IndexOf(node)
                    : Enumerable.Range(0, centers.Count).MinBy(group => costs[node, centers[group]]);
            return (candidate, nodes.Sum(node => costs[node, centers[candidate[node]]]));
        }
    }

    private static double Normalize(double bearing) => (bearing % 360 + 360) % 360;
}

using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class ApproximateRoutingMatrixParityTests
{
    [Theory]
    [InlineData(0, 1.3, 20)]
    [InlineData(1, 1.3, 20)]
    [InlineData(6, 1.3, 20)]
    [InlineData(6, 0.5, 0)]
    [InlineData(30, 1.7, 31)]
    public void MatrixMatchesPreviousDirectedCalculation(int count, double roadFactor, int speedKph)
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        (double Latitude, double Longitude) branch = (10, -70);
        var nodes = Enumerable.Range(0, count).Select(i =>
            new RoutingNode(i + 1, 10 + (i / 2) * .002, -70 + (i / 2) * .003,
                now, now, true, 60)).ToArray();
        var options = new DeliveryRoutingOptions
        {
            ApproximateRoadFactor = roadFactor,
            ApproximateUrbanSpeedKph = speedKph
        };
        var matrix = new ApproximateRoutingCostMatrixProvider(Options.Create(options))
            .Create(branch.Latitude, branch.Longitude, nodes);
        var points = new[] { branch }.Concat(nodes.Select(n => (n.Latitude, n.Longitude))).ToArray();
        var speed = Math.Max(1, options.ApproximateUrbanSpeedKph * 1000 / 3600);
        Assert.Equal(count + 1, matrix.DistanceMeters.GetLength(0));
        Assert.Equal(count + 1, matrix.DistanceMeters.GetLength(1));

        for (var from = 0; from < points.Length; from++)
        for (var to = 0; to < points.Length; to++)
        {
            var distance = from == to ? 0L : (long)Math.Ceiling(
                DirectedHaversine(points[from], points[to]) * Math.Max(1, options.ApproximateRoadFactor));
            var duration = (long)Math.Ceiling(distance / speed);
            Assert.Equal(distance, matrix.DistanceMeters[from, to]);
            Assert.Equal(duration, matrix.DurationSeconds[from, to]);
        }

        for (var i = 0; i < count; i++)
        {
            Assert.Equal(DirectedHaversine(branch, points[i + 1]), matrix.StraightDistanceFromBranchMeters[i + 1]);
            Assert.Equal(RoutingScheduleHelper.ExceptionLimit(matrix.DurationSeconds[0, i + 1], options),
                matrix.Nodes[i].DeliveryTimeLimitSeconds);
        }
    }

    // Reference: the previous implementation evaluated this formula in both directions.
    private static double DirectedHaversine((double Latitude, double Longitude) from,
        (double Latitude, double Longitude) to)
    {
        static double Rad(double value) => value * Math.PI / 180;
        var deltaLat = Rad(to.Latitude - from.Latitude);
        var deltaLon = Rad(to.Longitude - from.Longitude);
        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
            + Math.Cos(Rad(from.Latitude)) * Math.Cos(Rad(to.Latitude))
            * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
        return 6_371_000d * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

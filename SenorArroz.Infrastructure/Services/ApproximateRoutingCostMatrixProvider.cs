using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Services;

public sealed class ApproximateRoutingCostMatrixProvider : IRoutingCostMatrixProvider
{
    private readonly DeliveryRoutingOptions _options;

    public ApproximateRoutingCostMatrixProvider(IOptions<DeliveryRoutingOptions> options)
    {
        _options = options.Value;
    }

    public RoutingCostMatrix Create(
        double branchLatitude,
        double branchLongitude,
        IReadOnlyList<RoutingNode> nodes)
    {
        var count = nodes.Count + 1;
        var durations = new long[count, count];
        var distances = new long[count, count];
        var bearings = new double[count];
        var coordinates = new (double Latitude, double Longitude)[count];
        coordinates[0] = (branchLatitude, branchLongitude);

        for (var index = 0; index < nodes.Count; index++)
        {
            coordinates[index + 1] = (nodes[index].Latitude, nodes[index].Longitude);
            bearings[index + 1] = Bearing(
                branchLatitude,
                branchLongitude,
                nodes[index].Latitude,
                nodes[index].Longitude);
        }

        var speedMetersPerSecond = Math.Max(1, _options.ApproximateUrbanSpeedKph * 1000 / 3600);
        var roadFactor = Math.Max(1, _options.ApproximateRoadFactor);

        for (var from = 0; from < count; from++)
        {
            for (var to = 0; to < count; to++)
            {
                if (from == to)
                    continue;

                var straightMeters = Haversine(coordinates[from], coordinates[to]);
                var estimatedMeters = (long)Math.Ceiling(straightMeters * roadFactor);
                distances[from, to] = estimatedMeters;
                durations[from, to] = (long)Math.Ceiling(estimatedMeters / speedMetersPerSecond);
            }
        }

        return new RoutingCostMatrix(
            nodes,
            durations,
            distances,
            bearings,
            RoutingMatrixSource.Approximate,
            DateTime.UtcNow,
            []);
    }

    private static double Haversine(
        (double Latitude, double Longitude) from,
        (double Latitude, double Longitude) to)
    {
        const double earthRadiusMeters = 6_371_000;
        var lat1 = DegreesToRadians(from.Latitude);
        var lat2 = DegreesToRadians(to.Latitude);
        var deltaLat = DegreesToRadians(to.Latitude - from.Latitude);
        var deltaLon = DegreesToRadians(to.Longitude - from.Longitude);
        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
                + Math.Cos(lat1) * Math.Cos(lat2)
                * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
        return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double Bearing(double originLat, double originLon, double targetLat, double targetLon)
    {
        var lat1 = DegreesToRadians(originLat);
        var lat2 = DegreesToRadians(targetLat);
        var deltaLon = DegreesToRadians(targetLon - originLon);
        var y = Math.Sin(deltaLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2)
                - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(deltaLon);
        return (RadiansToDegrees(Math.Atan2(y, x)) + 360) % 360;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
    private static double RadiansToDegrees(double radians) => radians * 180 / Math.PI;
}

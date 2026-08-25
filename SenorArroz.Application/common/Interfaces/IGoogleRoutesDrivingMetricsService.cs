namespace SenorArroz.Application.Common.Interfaces;

public readonly record struct DrivingRouteMetrics(
    int DistanceMeters,
    int DurationSeconds,
    int ReturnDistanceMeters,
    int ReturnDurationSeconds,
    string? EncodedPolyline = null);

/// <summary>
/// Tiempo, distancia y geometría opcional de conducción entre waypoints ordenados.
/// </summary>
public interface IGoogleRoutesDrivingMetricsService
{
    /// <param name="orderedWaypoints">Lat/lng en orden de visita.</param>
    Task<DrivingRouteMetrics> ComputeRouteAsync(
        IReadOnlyList<(double Latitude, double Longitude)> orderedWaypoints,
        CancellationToken cancellationToken = default);
}

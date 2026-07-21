namespace SenorArroz.Application.Common.Interfaces;

public readonly record struct DrivingRouteMetrics(
    int DistanceMeters,
    int DurationSeconds,
    int ReturnDistanceMeters,
    int ReturnDurationSeconds);

/// <summary>
/// Tiempo y distancia de conducción entre waypoints ordenados (sin geometría).
/// </summary>
public interface IGoogleRoutesDrivingMetricsService
{
    /// <param name="orderedWaypoints">Lat/lng en orden de visita.</param>
    Task<DrivingRouteMetrics> ComputeRouteAsync(
        IReadOnlyList<(double Latitude, double Longitude)> orderedWaypoints,
        CancellationToken cancellationToken = default);
}

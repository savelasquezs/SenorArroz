namespace SenorArroz.Application.Common.Interfaces;

/// <summary>
/// Tiempo y distancia de conducción entre waypoints ordenados (sin geometría).
/// </summary>
public interface IGoogleRoutesDrivingMetricsService
{
    /// <param name="orderedWaypoints">Lat/lng en orden de visita.</param>
    Task<(int DistanceMeters, int DurationSeconds)> ComputeRouteAsync(
        IReadOnlyList<(double Latitude, double Longitude)> orderedWaypoints,
        CancellationToken cancellationToken = default);
}

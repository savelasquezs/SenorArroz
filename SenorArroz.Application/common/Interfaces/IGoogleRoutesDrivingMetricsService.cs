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
    /// <summary>Consulta Essentials sin tráfico en tiempo real.</summary>
    Task<DrivingRouteMetrics> ComputeRouteAsync(
        IReadOnlyList<(double Latitude, double Longitude)> orderedWaypoints,
        CancellationToken cancellationToken = default);

    /// <summary>Consulta Pro explícita para validaciones del optimizador que necesiten tráfico.</summary>
    Task<DrivingRouteMetrics> ComputeTrafficAwareRouteAsync(
        IReadOnlyList<(double Latitude, double Longitude)> orderedWaypoints,
        CancellationToken cancellationToken = default);
}

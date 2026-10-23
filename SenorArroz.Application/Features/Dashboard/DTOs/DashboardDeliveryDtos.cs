namespace SenorArroz.Application.Features.Dashboard.DTOs;

/// <summary>
/// Respuesta <c>GET /api/dashboard/delivery</c> (rango de fechas obligatorio en query).
/// </summary>
public class DashboardDeliveryResponseDto
{
    public double AvgPrepMinutes { get; set; }
    public double AvgDeliveryMinutes { get; set; }
    public List<DeliverymanEfficiencyApiDto> Deliverymen { get; set; } = new();
    public List<string> EvolutionLabels { get; set; } = new();
    public List<int> EvolutionDeliveries { get; set; } = new();
    public List<int> EvolutionFees { get; set; } = new();

    /// <summary>Ventas totales (suma <c>order.Total</c> pedidos entregados) por bucket, alineado con <see cref="EvolutionLabels"/>.</summary>
    public List<long> EvolutionSalesTotals { get; set; } = new();

    /// <summary>100 × suma fees domicilio / suma ventas entregadas en el periodo (pedidos entregados, todos los tipos).</summary>
    public double PeriodFeeToSalesPercent { get; set; }

    /// <summary>
    /// Rutas completadas en el mismo rango; series temporales alineadas a <see cref="EvolutionLabels"/>.
    /// </summary>
    public DashboardDeliveryRouteMetricsDto? RouteMetrics { get; set; }
}

public class DeliverymanEfficiencyApiDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DeliveredCount { get; set; }
    public double AvgDeliveryMinutes { get; set; }
    public int DeliveryFeeTotal { get; set; }

    public int RouteCompletedCount { get; set; }

    /// <summary>% rutas dentro de SLA entre las que tienen <c>metSla</c> definido (mismo periodo / filtros).</summary>
    public double? RouteOnTimePercent { get; set; }

    /// <summary>Duración real media de ruta (min), solo rutas con tiempo registrado.</summary>
    public double AvgRouteActualMinutes { get; set; }
}

/// <summary>Métricas de <c>delivery_route</c> cerradas para el dashboard de domicilios.</summary>
public class DashboardDeliveryRouteMetricsDto
{
    public DashboardDeliveryPerformanceDto? Performance { get; set; }
    public int CompletedRoutesCount { get; set; }
    public int RoutesWithSlaDataCount { get; set; }
    public double PeriodOnTimePercent { get; set; }
    public double PeriodDelayedPercent { get; set; }
    public double AvgActualRouteMinutes { get; set; }
    public double AvgMetaRouteMinutes { get; set; }
    public double AvgDelayMinutesWhenDelayed { get; set; }
    public double TotalDistanceKm { get; set; }

    public List<int> EvolutionRoutesCompleted { get; set; } = new();
    public List<double?> EvolutionOnTimePercent { get; set; } = new();
    public List<double?> EvolutionDelayedPercent { get; set; } = new();
    public List<double?> EvolutionAvgDelayMinutes { get; set; } = new();
    public List<double?> EvolutionAvgActualRouteMinutes { get; set; } = new();

    public List<DashboardDeliveryRouteHistoryItemDto> RecentRoutes { get; set; } = new();
}

public class DashboardDeliveryRouteHistoryItemDto
{
    public int Id { get; set; }
    public int DeliverymanId { get; set; }
    public string DeliverymanName { get; set; } = string.Empty;
    public DateTime? CompletedAtUtc { get; set; }
    public int? ActualDurationSeconds { get; set; }
    public int? MetaDurationSeconds { get; set; }
    public int? VarianceSeconds { get; set; }
    public bool? MetSla { get; set; }
    public int TotalDistanceMeters { get; set; }
}

/// <summary>Paradas de una ruta cerrada (detalle para dashboard).</summary>
public class DashboardDeliveryRouteStopsResponseDto
{
    public bool ReturnIsInconsistent { get; set; }
    public int RouteId { get; set; }
    public decimal? BranchLatitude { get; set; }
    public decimal? BranchLongitude { get; set; }
    public List<List<DashboardRoutePointDto>> Segments { get; set; } = new();
    public double GpsCoveragePercent { get; set; }
    public DateTime? DepartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public List<DashboardDeliveryRouteStopItemDto> Stops { get; set; } = new();
}

public class DashboardDeliveryRouteStopItemDto
{
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? OnTheWayAtUtc { get; set; }
    public int OrderId { get; set; }
    public int StopSequence { get; set; }
    public string? AddressSnapshotText { get; set; }
    public string? CustomerName { get; set; }
    public string? AddressText { get; set; }
}

public class DashboardRoutePointDto
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime RecordedAt { get; set; }
}

public class DashboardDeliveryPerformanceSummaryDto
{
    public int InconsistentReturnCount { get; set; }
    public int DeliveredCount { get; set; }
    public int CompletedRoutesCount { get; set; }
    public int SlaSampleCount { get; set; }
    public int RouteSampleCount { get; set; }
    public int OrderSampleCount { get; set; }
    public int GpsSampleCount { get; set; }
    public int DistanceSampleCount { get; set; }
    public int EstimatedDistanceSampleCount { get; set; }
    public int StageSampleCount { get; set; }
    public double? OnTimePercent { get; set; }
    public double? AvgRouteMinutes { get; set; }
    public double? AvgMetaMinutes { get; set; }
    public double? AvgOrderMinutes { get; set; }
    public double? P90OrderMinutes { get; set; }
    public double? MinutesPerKm { get; set; }
    public double? GpsMinutesPerKm { get; set; }
    public double? EstimatedMinutesPerKm { get; set; }
    public double? OrdersPerRoute { get; set; }
    public double? AvgDispatchMinutes { get; set; }
    public double? AvgDeliveryStageMinutes { get; set; }
    public double? AvgReturnMinutes { get; set; }
    public double TotalGpsKm { get; set; }
}

public class DashboardDeliveryPerformanceRowDto : DashboardDeliveryPerformanceSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BranchId { get; set; }
}

public class DashboardDeliveryPerformanceRouteDto
{
    public bool ReturnIsInconsistent { get; set; }
    public int Id { get; set; }
    public int DeliverymanId { get; set; }
    public string DeliverymanName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public DateTime? DepartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int DeliveredCount { get; set; }
    public double? DurationMinutes { get; set; }
    public double? MetaMinutes { get; set; }
    public bool? MetSla { get; set; }
    public double? GpsDistanceKm { get; set; }
    public double PlannedDistanceKm { get; set; }
    public double GpsCoveragePercent { get; set; }
}

public class DashboardDeliveryPerformanceDto
{
    public DashboardDeliveryPerformanceSummaryDto? Team { get; set; }
    public List<DashboardDeliveryPerformanceSummaryDto> TeamEvolution { get; set; } = new();
    public DashboardDeliveryPerformanceSummaryDto Current { get; set; } = new();
    public DashboardDeliveryPerformanceSummaryDto Previous { get; set; } = new();
    public List<DashboardDeliveryPerformanceRowDto> Drivers { get; set; } = new();
    public List<DashboardDeliveryPerformanceRowDto> DriverOptions { get; set; } = new();
    public List<string> Labels { get; set; } = new();
    public List<DashboardDeliveryPerformanceSummaryDto> Evolution { get; set; } = new();
    public List<DashboardDeliveryPerformanceRouteDto> RecentRoutes { get; set; } = new();
}

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

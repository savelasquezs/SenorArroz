using System.Text.Json.Serialization;

namespace SenorArroz.Application.Features.Deliverymen.DTOs;

public class DeliveryRouteSummaryItemDto
{
    public int Id { get; set; }

    /// <summary>Suma planeada + regreso sucursal (metros).</summary>
    public int TotalDistanceMeters { get; set; }

    /// <summary>Inicio operativo (última asignación + delay consolidación), UTC.</summary>
    public DateTime? RouteStartedAtUtc { get; set; }

    /// <summary>route_started_at + meta (tiempo meta total), UTC.</summary>
    public DateTime? PlannedEndAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Segundos reales desde inicio hasta cierre.</summary>
    public int? ActualDurationSeconds { get; set; }

    /// <summary>Meta en segundos (manejo + buffers). Null si la ruta se cerró sin consolidación completa.</summary>
    public int? MetaDurationSeconds { get; set; }

    /// <summary>actual_duration_seconds − meta_duration_seconds; positivo = tardó más que la meta.</summary>
    public int? VarianceSeconds { get; set; }
}

public class DeliverymanRouteDayStatsDto
{
    public int CompletedRoutesCount { get; set; }

    /// <summary>Suma de todas las rutas completadas en el período (metros).</summary>
    public int TotalDistanceMeters { get; set; }

    [JsonPropertyName("routes")]
    public List<DeliveryRouteSummaryItemDto> Routes { get; set; } = new();
}

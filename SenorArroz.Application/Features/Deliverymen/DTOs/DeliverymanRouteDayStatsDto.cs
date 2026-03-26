using System.Text.Json.Serialization;

namespace SenorArroz.Application.Features.Deliverymen.DTOs;

public class DeliveryRouteSummaryItemDto
{
    public int Id { get; set; }

    /// <summary>Suma planeada + regreso sucursal (metros).</summary>
    public int TotalDistanceMeters { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}

public class DeliverymanRouteDayStatsDto
{
    public int CompletedRoutesCount { get; set; }

    /// <summary>Suma de todas las rutas completadas en el período (metros).</summary>
    public int TotalDistanceMeters { get; set; }

    [JsonPropertyName("routes")]
    public List<DeliveryRouteSummaryItemDto> Routes { get; set; } = new();
}

using System.Text.Json.Serialization;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Deliverymen.DTOs;

public class DeliverymanDaySummaryDto
{
    /// <summary>Ciclo actual (post-última liquidación en el día) — cuadre y liquidación.</summary>
    public DeliverymanDayStatsDto Stats { get; set; } = null!;
    public List<OrderDto> Orders { get; set; } = new();

    /// <summary>Agregado del día calendario (o rango) sin filtro de ciclo — solo lectura.</summary>
    [JsonPropertyName("fullDayStats")]
    public DeliverymanDayStatsDto FullDayStats { get; set; } = null!;

    [JsonPropertyName("fullDayOrders")]
    public List<OrderDto> FullDayOrders { get; set; } = new();
}

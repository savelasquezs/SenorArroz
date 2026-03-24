using System.Text.Json.Serialization;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Deliverymen.DTOs;

public class DeliverymanDayStatsDto
{
    public int DeliverymanId { get; set; }
    public string DeliverymanName { get; set; } = string.Empty;
    public int OrdersCount { get; set; }
    [JsonPropertyName("totalCash")]
    public decimal TotalCollected { get; set; }
    public decimal TotalAdvances { get; set; }
    public decimal TotalDeliveryFee { get; set; }
    public decimal CashToDeliver { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal CurrentBalance { get; set; }
    [JsonPropertyName("averageDeliveryTime")]
    public int AverageDeliveryTimeMinutes { get; set; }

    /// <summary>Tarjeta bloqueada (liquidación total hasta desbloqueo).</summary>
    public bool DayBlocked { get; set; }

    [JsonPropertyName("liquidationMode")]
    public DeliverymanDayLiquidationMode LiquidationMode { get; set; }

    /// <summary>Pedidos delivery asignados en estado OnTheWay (no entregados).</summary>
    [JsonPropertyName("ordersOnTheWayCount")]
    public int OrdersOnTheWayCount { get; set; }
}

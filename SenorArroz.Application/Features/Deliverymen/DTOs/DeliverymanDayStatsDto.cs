using System.Text.Json.Serialization;

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
}

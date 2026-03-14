using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Deliverymen.DTOs;

public class DeliverymanDaySummaryDto
{
    public DeliverymanDayStatsDto Stats { get; set; } = null!;
    public List<OrderDto> Orders { get; set; } = new();
}

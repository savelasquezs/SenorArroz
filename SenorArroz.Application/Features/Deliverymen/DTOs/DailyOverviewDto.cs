using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;

namespace SenorArroz.Application.Features.Deliverymen.DTOs;

public class DailyOverviewDto
{
    public List<DeliverymanDayStatsDto> Deliverymen { get; set; } = new();
    public List<DeliverymanAdvanceDto> Advances { get; set; } = new();
}

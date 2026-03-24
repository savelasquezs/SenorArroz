using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;

namespace SenorArroz.Application.Features.Deliverymen.DTOs;

public class SettleDeliverymanDayResultDto
{
    public List<DeliverymanAdvanceDto> Advances { get; set; } = new();
    public decimal SurplusApplied { get; set; }
}

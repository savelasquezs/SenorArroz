using MediatR;
using SenorArroz.Application.Features.Deliverymen.DTOs;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public class SettleDeliverymanDayCommand : IRequest<SettleDeliverymanDayResultDto>
{
    public int DeliverymanId { get; set; }
    public SettleDeliverymanDayDto Settlement { get; set; } = null!;
}

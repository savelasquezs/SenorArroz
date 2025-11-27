using MediatR;
using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;

namespace SenorArroz.Application.Features.DeliverymanAdvances.Commands;

public class CreateAdvanceCommand : IRequest<DeliverymanAdvanceDto>
{
    public CreateDeliverymanAdvanceDto Advance { get; set; } = null!;
}


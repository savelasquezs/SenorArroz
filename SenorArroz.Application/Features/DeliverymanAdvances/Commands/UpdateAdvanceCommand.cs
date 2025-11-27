using MediatR;
using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;

namespace SenorArroz.Application.Features.DeliverymanAdvances.Commands;

public class UpdateAdvanceCommand : IRequest<DeliverymanAdvanceDto>
{
    public int Id { get; set; }
    public UpdateDeliverymanAdvanceDto Advance { get; set; } = null!;
}


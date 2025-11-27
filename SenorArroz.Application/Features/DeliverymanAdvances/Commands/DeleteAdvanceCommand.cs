using MediatR;

namespace SenorArroz.Application.Features.DeliverymanAdvances.Commands;

public class DeleteAdvanceCommand : IRequest<bool>
{
    public int Id { get; set; }
}


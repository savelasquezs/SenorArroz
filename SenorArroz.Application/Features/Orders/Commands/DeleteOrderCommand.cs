using MediatR;

namespace SenorArroz.Application.Features.Orders.Commands;

public class DeleteOrderCommand : IRequest<Unit>
{
    public int Id { get; set; }
}

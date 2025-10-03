using MediatR;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class DeleteOrderHandler : IRequestHandler<DeleteOrderCommand, Unit>
{
    private readonly IOrderRepository _orderRepository;

    public DeleteOrderHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Unit> Handle(DeleteOrderCommand request, CancellationToken cancellationToken)
    {
        await _orderRepository.DeleteAsync(request.Id);
        return Unit.Value;
    }
}

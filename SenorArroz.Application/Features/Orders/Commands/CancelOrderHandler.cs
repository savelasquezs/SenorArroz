using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class CancelOrderHandler : IRequestHandler<CancelOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;

    public CancelOrderHandler(IOrderRepository orderRepository, IMapper mapper)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
    }

    public async Task<OrderDto> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.CancelOrderAsync(
            request.Id, 
            request.Cancellation.Reason);

        return _mapper.Map<OrderDto>(order);
    }
}

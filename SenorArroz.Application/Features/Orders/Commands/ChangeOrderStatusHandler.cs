using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class ChangeOrderStatusHandler : IRequestHandler<ChangeOrderStatusCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;

    public ChangeOrderStatusHandler(IOrderRepository orderRepository, IMapper mapper)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
    }

    public async Task<OrderDto> Handle(ChangeOrderStatusCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.ChangeStatusAsync(
            request.Id, 
            request.StatusChange.Status, 
            request.StatusChange.Reason);

        return _mapper.Map<OrderDto>(order);
    }
}

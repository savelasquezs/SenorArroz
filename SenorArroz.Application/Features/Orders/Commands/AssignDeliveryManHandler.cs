using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class AssignDeliveryManHandler : IRequestHandler<AssignDeliveryManCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;

    public AssignDeliveryManHandler(IOrderRepository orderRepository, IMapper mapper)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
    }

    public async Task<OrderDto> Handle(AssignDeliveryManCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.AssignDeliveryManAsync(
            request.Id, 
            request.Assignment.DeliveryManId);

        return _mapper.Map<OrderDto>(order);
    }
}

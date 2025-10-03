using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class UnassignDeliveryManHandler : IRequestHandler<UnassignDeliveryManCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;

    public UnassignDeliveryManHandler(IOrderRepository orderRepository, IMapper mapper)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
    }

    public async Task<OrderDto> Handle(UnassignDeliveryManCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.UnassignDeliveryManAsync(request.Id);
        return _mapper.Map<OrderDto>(order);
    }
}

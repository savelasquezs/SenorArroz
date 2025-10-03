using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class UpdateOrderHandler : IRequestHandler<UpdateOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;

    public UpdateOrderHandler(IOrderRepository orderRepository, IMapper mapper)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
    }

    public async Task<OrderDto> Handle(UpdateOrderCommand request, CancellationToken cancellationToken)
    {
        var existingOrder = await _orderRepository.GetByIdAsync(request.Id);
        if (existingOrder == null)
            throw new ArgumentException("Order not found");

        // Solo permitir actualizaciones si el pedido no está entregado o cancelado
        if (existingOrder.Status == Domain.Enums.OrderStatus.Delivered || 
            existingOrder.Status == Domain.Enums.OrderStatus.Cancelled)
            throw new InvalidOperationException("Cannot update delivered or cancelled orders");

        _mapper.Map(request.Order, existingOrder);
        
        var updatedOrder = await _orderRepository.UpdateAsync(existingOrder);
        return _mapper.Map<OrderDto>(updatedOrder);
    }
}

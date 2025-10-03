using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class UpdateOrderHandler : IRequestHandler<UpdateOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public UpdateOrderHandler(IOrderRepository orderRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<OrderDto> Handle(UpdateOrderCommand request, CancellationToken cancellationToken)
    {
        var existingOrder = await _orderRepository.GetByIdAsync(request.Id);
        if (existingOrder == null)
            throw new BusinessException("Pedido no encontrado");

        // Validate branch access
        if (_currentUser.Role != "superadmin" && existingOrder.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

        // Validate role permissions
        if (!new[] { "superadmin", "admin", "cashier" }.Contains(_currentUser.Role.ToLower()))
            throw new BusinessException("No tienes permisos para actualizar pedidos");

        // Solo permitir actualizaciones si el pedido no está entregado o cancelado
        if (existingOrder.Status == Domain.Enums.OrderStatus.Delivered || 
            existingOrder.Status == Domain.Enums.OrderStatus.Cancelled)
            throw new BusinessException("No se pueden actualizar pedidos entregados o cancelados");

        _mapper.Map(request.Order, existingOrder);
        
        var updatedOrder = await _orderRepository.UpdateAsync(existingOrder);
        return _mapper.Map<OrderDto>(updatedOrder);
    }
}

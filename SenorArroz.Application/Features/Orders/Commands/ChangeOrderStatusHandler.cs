using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class ChangeOrderStatusHandler : IRequestHandler<ChangeOrderStatusCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public ChangeOrderStatusHandler(IOrderRepository orderRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<OrderDto> Handle(ChangeOrderStatusCommand request, CancellationToken cancellationToken)
    {
        // Get order first to validate access
        var existingOrder = await _orderRepository.GetByIdAsync(request.Id);
        if (existingOrder == null)
            throw new BusinessException("Pedido no encontrado");

        // Validate branch access
        if (_currentUser.Role != "superadmin" && existingOrder.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

        // Validate role permissions for status change
        var hasPermission = request.StatusChange.Status switch
        {
            Domain.Enums.OrderStatus.InPreparation => 
                new[] { "superadmin", "admin", "cashier", "kitchen" }.Contains(_currentUser.Role.ToLower()),
            Domain.Enums.OrderStatus.Ready => 
                new[] { "superadmin", "admin", "kitchen" }.Contains(_currentUser.Role.ToLower()),
            Domain.Enums.OrderStatus.OnTheWay => 
                new[] { "superadmin", "admin", "cashier", "deliveryman" }.Contains(_currentUser.Role.ToLower()),
            Domain.Enums.OrderStatus.Delivered => 
                new[] { "superadmin", "admin", "deliveryman" }.Contains(_currentUser.Role.ToLower()),
            Domain.Enums.OrderStatus.Cancelled => 
                new[] { "superadmin", "admin" }.Contains(_currentUser.Role.ToLower()),
            _ => new[] { "superadmin", "admin" }.Contains(_currentUser.Role.ToLower())
        };

        if (!hasPermission)
            throw new BusinessException("No tienes permisos para cambiar el estado a " + request.StatusChange.Status);

        var order = await _orderRepository.ChangeStatusAsync(
            request.Id, 
            request.StatusChange.Status, 
            request.StatusChange.Reason);

        return _mapper.Map<OrderDto>(order);
    }
}

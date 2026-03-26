using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class UnassignDeliveryManHandler : IRequestHandler<UnassignDeliveryManCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IDeliveryRouteWorkflowService _deliveryRouteWorkflow;

    public UnassignDeliveryManHandler(
        IOrderRepository orderRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        IDeliveryRouteWorkflowService deliveryRouteWorkflow)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _deliveryRouteWorkflow = deliveryRouteWorkflow;
    }

    public async Task<OrderDto> Handle(UnassignDeliveryManCommand request, CancellationToken cancellationToken)
    {
        // Get order first to validate access
        var existingOrder = await _orderRepository.GetByIdAsync(request.Id);
        if (existingOrder == null)
            throw new BusinessException("Pedido no encontrado");

        // Validate branch access
        if (_currentUser.Role != "superadmin" && existingOrder.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

        // Validate role permissions
        if (!new[] { "superadmin", "admin", "cashier" }.Contains(_currentUser.Role.ToLower()))
            throw new BusinessException("No tienes permisos para desasignar domiciliarios");

        await _deliveryRouteWorkflow.OnOrderUnassignedAsync(request.Id, cancellationToken);
        var order = await _orderRepository.UnassignDeliveryManAsync(request.Id);
        return _mapper.Map<OrderDto>(order);
    }
}

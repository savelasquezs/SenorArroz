using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class AssignDeliveryManHandler : IRequestHandler<AssignDeliveryManCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IDeliveryRouteWorkflowService _deliveryRouteWorkflow;
    private readonly IOrderNotificationService _notificationService;

    public AssignDeliveryManHandler(
        IOrderRepository orderRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        IDeliveryRouteWorkflowService deliveryRouteWorkflow,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _deliveryRouteWorkflow = deliveryRouteWorkflow;
        _notificationService = notificationService;
    }

    public async Task<OrderDto> Handle(AssignDeliveryManCommand request, CancellationToken cancellationToken)
    {
        // Get order first to validate access
        var existingOrder = await _orderRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existingOrder == null)
            throw new BusinessException("Pedido no encontrado");

        // Validate branch access
        if (!Roles.IsSuperadmin(_currentUser.Role) && existingOrder.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

        // Validate role permissions
        if (!Roles.IsSuperadminOrAdminOrCashier(_currentUser.Role))
            throw new BusinessException("No tienes permisos para asignar domiciliarios");

        var order = await _orderRepository.AssignDeliveryManAsync(
            request.Id,
            request.Assignment.DeliveryManId,
            cancellationToken);

        // Cambiar estado a OnTheWay si estaba en Ready
        if (order.Status == Domain.Enums.OrderStatus.Ready)
        {
            order = await _orderRepository.ChangeStatusAsync(
                request.Id, 
                Domain.Enums.OrderStatus.OnTheWay, 
                null,
                cancellationToken);
        }

        order = await _orderRepository.GetByIdAsync(request.Id, cancellationToken);
        if (order != null)
            await _deliveryRouteWorkflow.OnOrderAssignedToDeliverymanAsync(order, cancellationToken);

        order = await _orderRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new BusinessException("Pedido no encontrado");
        var orderDto = _mapper.Map<OrderDto>(order);
        await _notificationService.NotifyOrderAssignedToDelivery(orderDto);

        return orderDto;
    }
}

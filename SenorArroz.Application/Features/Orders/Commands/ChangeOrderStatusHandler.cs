using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class ChangeOrderStatusHandler : IRequestHandler<ChangeOrderStatusCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IOrderBusinessRulesService _businessRules;
    private readonly IOrderNotificationService _notificationService;

    public ChangeOrderStatusHandler(
        IOrderRepository orderRepository, 
        IMapper mapper, 
        ICurrentUser currentUser,
        IOrderBusinessRulesService businessRules,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _businessRules = businessRules;
        _notificationService = notificationService;
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

        // Validación especial para domiciliarios
        if (_currentUser.Role.ToLower() == "deliveryman")
        {
            // Verificar que el pedido esté asignado a este domiciliario
            if (!existingOrder.DeliveryManId.HasValue || existingOrder.DeliveryManId.Value != _currentUser.Id)
                throw new BusinessException("Solo puedes cambiar el estado de pedidos asignados a ti");

            // Solo permitir transiciones OnTheWay ↔ Delivered
            var allowedTransitions = new[] 
            { 
                (OrderStatus.OnTheWay, OrderStatus.Delivered),
                (OrderStatus.Delivered, OrderStatus.OnTheWay) 
            };
            
            var transition = (existingOrder.Status, request.StatusChange.Status);
            if (!allowedTransitions.Contains(transition))
                throw new BusinessException("Los domiciliarios solo pueden cambiar entre estados OnTheWay y Delivered");
        }
        else
        {
            // Validar transición de estado para otros roles
            if (!_businessRules.IsStatusTransitionValid(existingOrder, request.StatusChange.Status, _currentUser.Role))
                throw new BusinessException($"No puedes cambiar el estado de {existingOrder.Status} a {request.StatusChange.Status}");
        }

        var order = await _orderRepository.ChangeStatusAsync(
            request.Id, 
            request.StatusChange.Status, 
            request.StatusChange.Reason);

        var orderDto = _mapper.Map<OrderDto>(order);

        // Notificar a domiciliarios si el estado cambia a Ready
        if (request.StatusChange.Status == OrderStatus.Ready)
        {
            await _notificationService.NotifyOrderReadyToDelivery(orderDto);
        }

        return orderDto;
    }
}

using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
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
    private readonly IDeliveryRouteWorkflowService _deliveryRouteWorkflow;
    private readonly IPrintQueueService _printQueue;
    private readonly ILogger<ChangeOrderStatusHandler> _logger;

    public ChangeOrderStatusHandler(
        IOrderRepository orderRepository, 
        IMapper mapper, 
        ICurrentUser currentUser,
        IOrderBusinessRulesService businessRules,
        IOrderNotificationService notificationService,
        IDeliveryRouteWorkflowService deliveryRouteWorkflow,
        IPrintQueueService printQueue,
        ILogger<ChangeOrderStatusHandler> logger)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _businessRules = businessRules;
        _notificationService = notificationService;
        _deliveryRouteWorkflow = deliveryRouteWorkflow;
        _printQueue = printQueue;
        _logger = logger;
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

        // Reserva pasando a preparación: resolver tipo definitivo según si tiene dirección
        if (existingOrder.Type == OrderType.Reservation
            && request.StatusChange.Status == OrderStatus.InPreparation)
        {
            existingOrder.Type = existingOrder.AddressId.HasValue
                ? OrderType.Delivery
                : OrderType.Onsite;
            await _orderRepository.UpdateAsync(existingOrder);
        }

        var routeIdSnapshot = existingOrder.DeliveryRouteId;

        var order = await _orderRepository.ChangeStatusAsync(
            request.Id, 
            request.StatusChange.Status, 
            request.StatusChange.Reason);

        var orderDto = _mapper.Map<OrderDto>(order);

        // Notificar a domiciliarios si el estado cambia a Ready
        if (request.StatusChange.Status == OrderStatus.Ready)
        {
            await _notificationService.NotifyOrderReadyToDelivery(orderDto);

            // Comanda de cocina al pasar a listo solo si quien confirma es cocina (evita impresión si Admin/Superadmin cambia estado).
            if (string.Equals(_currentUser.Role, "kitchen", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await _printQueue.EnqueueAsync(
                        order.BranchId,
                        PrintJobKind.Kitchen,
                        new[] { order.Id },
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "No se encoló comanda de cocina para pedido {OrderId} (sucursal {BranchId}). El estado sí se actualizó.",
                        order.Id,
                        order.BranchId);
                }
            }
        }

        // Notificar a todos los domiciliarios cuando un pedido es asignado (OnTheWay)
        // para que desaparezca de la lista de disponibles en sus pantallas
        if (request.StatusChange.Status == OrderStatus.OnTheWay)
        {
            await _notificationService.NotifyOrderAssignedToDelivery(orderDto);
        }

        if (request.StatusChange.Status == OrderStatus.Cancelled)
            await _deliveryRouteWorkflow.OnOrderCancelledWhileRouteOpenAsync(request.Id, cancellationToken);

        if (request.StatusChange.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            await _deliveryRouteWorkflow.TryFinalizeRouteWhenAllTerminalAsync(
                request.Id, routeIdSnapshot, cancellationToken);

        return orderDto;
    }
}

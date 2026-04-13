using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.Orders.Commands;

public class SelfAssignOrdersHandler : IRequestHandler<SelfAssignOrdersCommand, List<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUserRepository _userRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IPasswordService _passwordService;
    private readonly IOrderNotificationService _notificationService;
    private readonly IDeliveryRouteWorkflowService _deliveryRouteWorkflow;
    private readonly IPrintQueueService _printQueue;
    private readonly ILogger<SelfAssignOrdersHandler> _logger;

    public SelfAssignOrdersHandler(
        IOrderRepository orderRepository, 
        IUserRepository userRepository,
        IMapper mapper, 
        ICurrentUser currentUser,
        IPasswordService passwordService,
        IOrderNotificationService notificationService,
        IDeliveryRouteWorkflowService deliveryRouteWorkflow,
        IPrintQueueService printQueue,
        ILogger<SelfAssignOrdersHandler> logger)
    {
        _orderRepository = orderRepository;
        _userRepository = userRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _passwordService = passwordService;
        _notificationService = notificationService;
        _deliveryRouteWorkflow = deliveryRouteWorkflow;
        _printQueue = printQueue;
        _logger = logger;
    }

    public async Task<List<OrderDto>> Handle(SelfAssignOrdersCommand request, CancellationToken cancellationToken)
    {
        // Validate that user is a deliveryman
        if (!Roles.IsDeliveryman(_currentUser.Role))
            throw new BusinessException("Solo los domiciliarios pueden autoasignarse pedidos");

        // Get current user ID and validate password
        var userId = _currentUser.Id;
        if (!_currentUser.IsAuthenticated)
            throw new BusinessException("Usuario no autenticado");

        // Get user from repository to verify password
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            throw new BusinessException("Usuario no encontrado");

        // Verify password
        if (!_passwordService.VerifyPassword(request.Password, user.PasswordHash))
            throw new BusinessException("Contraseña incorrecta");

        var assignedOrders = new List<OrderDto>();

        if (request.OrderIds.Count > 0)
        {
            var probe = await _orderRepository.GetByIdAsync(request.OrderIds[0], cancellationToken);
            if (probe != null
                && await _deliveryRouteWorkflow.DeliverymanHasPendingOrdersOnActiveRouteAsync(
                    userId,
                    probe.BranchId,
                    cancellationToken,
                    request.OrderIds))
            {
                throw new BusinessException(
                    "Termina o entrega los pedidos de tu ruta actual antes de tomar más. Si necesitas otro pedido urgente, pide a caja que te lo asigne.");
            }
        }

        foreach (var orderId in request.OrderIds)
        {
            // Get order to validate
            var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
            if (order == null)
                throw new BusinessException($"Pedido {orderId} no encontrado");

            if (order.Type != OrderType.Delivery)
                throw new BusinessException(
                    "Solo puedes tomar pedidos de domicilio desde la app. Los pedidos en local los asigna caja o administración.");

            // Validate branch access
            if (order.BranchId != _currentUser.BranchId)
                throw new BusinessException($"No tienes permisos para asignarte pedidos de la sucursal {order.BranchId}");

            // Validate order status - must be Ready
            if (order.Status != Domain.Enums.OrderStatus.Ready)
                throw new BusinessException($"El pedido {orderId} debe estar en estado 'Ready' para ser autoasignado");

            // Validate that order is not already assigned
            if (order.DeliveryManId.HasValue)
                throw new BusinessException($"El pedido {orderId} ya está asignado a otro domiciliario");

            // Ya no existe límite de pedidos activos por domiciliario

            // Assign order to current user
            var assignedOrder = await _orderRepository.AssignDeliveryManAsync(orderId, userId, cancellationToken);
            
            // Cambiar estado a OnTheWay automáticamente después de asignar
            assignedOrder = await _orderRepository.ChangeStatusAsync(orderId, Domain.Enums.OrderStatus.OnTheWay, null, cancellationToken);

            var fullOrder = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
            if (fullOrder != null)
                await _deliveryRouteWorkflow.OnOrderAssignedToDeliverymanAsync(fullOrder, cancellationToken);

            // DTO y notificación después del workflow para incluir deliveryRouteId (y coherencia con BD)
            var afterRoute = await _orderRepository.GetByIdAsync(orderId) ?? assignedOrder;
            var orderDto = _mapper.Map<OrderDto>(afterRoute);
            assignedOrders.Add(orderDto);

            await _notificationService.NotifyOrderAssignedToDelivery(orderDto);

            // Ticket de domicilio solo cuando el domiciliario se autoasigna (no cuando caja/admin asigna).
            if (afterRoute.Type == OrderType.Delivery)
            {
                try
                {
                    await _printQueue.EnqueueAsync(
                        afterRoute.BranchId,
                        PrintJobKind.Delivery,
                        new[] { orderId },
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "No se encoló ticket de domicilio para pedido {OrderId} (sucursal {BranchId}). La asignación se completó.",
                        orderId,
                        afterRoute.BranchId);
                }
            }
        }

        return assignedOrders;
    }
}

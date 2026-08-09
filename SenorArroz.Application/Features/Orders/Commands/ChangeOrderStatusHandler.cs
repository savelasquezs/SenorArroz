using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class ChangeOrderStatusHandler : IRequestHandler<ChangeOrderStatusCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IApplicationDbContext? _context;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IOrderBusinessRulesService _businessRules;
    private readonly IOrderNotificationService _notificationService;
    private readonly IDeliveryRouteWorkflowService _deliveryRouteWorkflow;
    private readonly IPrintQueueService _printQueue;
    private readonly ILoyaltyCycleService _loyaltyCycle;
    private readonly ILogger<ChangeOrderStatusHandler> _logger;
    private readonly IExternalDeliveryStatusSyncService? _externalDeliveryStatusSync;

    public ChangeOrderStatusHandler(
        IOrderRepository orderRepository,
        IApplicationDbContext context,
        IMapper mapper,
        ICurrentUser currentUser,
        IOrderBusinessRulesService businessRules,
        IOrderNotificationService notificationService,
        IDeliveryRouteWorkflowService deliveryRouteWorkflow,
        IPrintQueueService printQueue,
        ILoyaltyCycleService loyaltyCycle,
        ILogger<ChangeOrderStatusHandler> logger,
        IExternalDeliveryStatusSyncService? externalDeliveryStatusSync = null)
    {
        _orderRepository = orderRepository;
        _context = context;
        _mapper = mapper;
        _currentUser = currentUser;
        _businessRules = businessRules;
        _notificationService = notificationService;
        _deliveryRouteWorkflow = deliveryRouteWorkflow;
        _printQueue = printQueue;
        _loyaltyCycle = loyaltyCycle;
        _logger = logger;
        _externalDeliveryStatusSync = externalDeliveryStatusSync;
    }

    public ChangeOrderStatusHandler(
        IOrderRepository orderRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        IOrderBusinessRulesService businessRules,
        IOrderNotificationService notificationService,
        IDeliveryRouteWorkflowService deliveryRouteWorkflow,
        IPrintQueueService printQueue,
        ILoyaltyCycleService loyaltyCycle,
        ILogger<ChangeOrderStatusHandler> logger)
        : this(
            orderRepository,
            context: null!,
            mapper,
            currentUser,
            businessRules,
            notificationService,
            deliveryRouteWorkflow,
            printQueue,
            loyaltyCycle,
            logger)
    {
    }

    public async Task<OrderDto> Handle(ChangeOrderStatusCommand request, CancellationToken cancellationToken)
    {
        if (_context is null)
            return await HandleLegacyAsync(request, cancellationToken);

        var existingOrder = await _orderRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existingOrder == null)
            throw new BusinessException("Pedido no encontrado");

        if (!Roles.IsSuperadmin(_currentUser.Role) && existingOrder.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

        if (Roles.IsDeliveryman(_currentUser.Role))
        {
            if (!existingOrder.DeliveryManId.HasValue || existingOrder.DeliveryManId.Value != _currentUser.Id)
                throw new BusinessException("Solo puedes cambiar el estado de pedidos asignados a ti");

            var allowedTransitions =
                new[]
                {
                    (OrderStatus.OnTheWay, OrderStatus.Delivered),
                    (OrderStatus.Delivered, OrderStatus.OnTheWay),
                };

            var transition = (existingOrder.Status, request.StatusChange.Status);
            if (!allowedTransitions.Contains(transition))
                throw new BusinessException("Los domiciliarios solo pueden cambiar entre estados OnTheWay y Delivered");
        }
        else if (!_businessRules.IsStatusTransitionValid(existingOrder, request.StatusChange.Status, _currentUser.Role))
        {
            throw new BusinessException($"No puedes cambiar el estado de {existingOrder.Status} a {request.StatusChange.Status}");
        }

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            if (existingOrder.Type == OrderType.Reservation
                && request.StatusChange.Status == OrderStatus.Ready)
            {
                var orderForTypeUpdate = await _orderRepository.GetByIdWithDetailsAsync(request.Id, cancellationToken)
                    ?? throw new BusinessException("Pedido no encontrado");

                var detailCount = orderForTypeUpdate.OrderDetails?.Count ?? 0;
                if (detailCount < 1)
                {
                    _logger.LogWarning(
                        "Reserva a preparacion: pedido {OrderId} sin lineas al cargar detalle; se aborta para no vaciar el pedido en BD",
                        request.Id);
                    throw new BusinessException(
                        "No se pudo preparar el pedido: faltan los productos en el sistema. Vuelva a abrir el pedido o pida a administracion verificarlo.");
                }

                var targetType = orderForTypeUpdate.AddressId.HasValue
                    ? OrderType.Delivery
                    : OrderType.Onsite;

                await PromoteReservationDepositsToBankPaymentsAsync(orderForTypeUpdate.Id, cancellationToken);

                await _context.Orders
                    .Where(o => o.Id == request.Id)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(o => o.Type, targetType),
                        cancellationToken);

                existingOrder.Type = targetType;
            }

            var routeIdSnapshot = existingOrder.DeliveryRouteId;
            var previousStatus = existingOrder.Status;

            if (previousStatus == OrderStatus.Delivered
                && request.StatusChange.Status == OrderStatus.Ready)
            {
                await ClearDeliveryAssignmentAsync(request.Id, cancellationToken);
            }

            var order = await _orderRepository.ChangeStatusAsync(
                request.Id,
                request.StatusChange.Status,
                request.StatusChange.Reason,
                cancellationToken);

            if (previousStatus == OrderStatus.Delivered && request.StatusChange.Status != OrderStatus.Delivered)
                await _loyaltyCycle.OnOrderLeftDeliveredAsync(order.Id, cancellationToken);

            if (request.StatusChange.Status == OrderStatus.Delivered
                && previousStatus != OrderStatus.Delivered
                && order.CustomerId.HasValue)
            {
                await _loyaltyCycle.OnOrderDeliveredAsync(order.Id, order.BranchId, order.CustomerId, cancellationToken);
            }

            var orderForResponse = await _orderRepository.GetByIdWithFullDetailsAsync(order.Id, cancellationToken) ?? order;
            var orderDto = _mapper.Map<OrderDto>(orderForResponse);

            if (request.StatusChange.Status == OrderStatus.Ready)
            {
                if (DeliveryReadyNotificationEligibility.ShouldNotifyOwnDeliverymen(order))
                    await _notificationService.NotifyOrderReadyToDelivery(orderDto);
                if (_externalDeliveryStatusSync is not null)
                    await _externalDeliveryStatusSync.SyncReadyForPickupAsync(order.Id, cancellationToken);

                var role = (_currentUser.Role ?? string.Empty).Trim();
                if (Roles.IsKitchen(role))
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
                            "No se encolo comanda de cocina para pedido {OrderId} (sucursal {BranchId}). El estado si se actualizo.",
                            order.Id,
                            order.BranchId);
                    }
                }
            }

            if (request.StatusChange.Status == OrderStatus.OnTheWay)
                await _notificationService.NotifyOrderAssignedToDelivery(orderDto);

            if (request.StatusChange.Status == OrderStatus.Cancelled)
                await _deliveryRouteWorkflow.OnOrderCancelledWhileRouteOpenAsync(request.Id, cancellationToken);

            if (request.StatusChange.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
                await _deliveryRouteWorkflow.TryFinalizeRouteWhenAllTerminalAsync(
                    request.Id,
                    routeIdSnapshot,
                    cancellationToken);

            await tx.CommitAsync(cancellationToken);
            return orderDto;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<OrderDto> HandleLegacyAsync(ChangeOrderStatusCommand request, CancellationToken cancellationToken)
    {
        var existingOrder = await _orderRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existingOrder == null)
            throw new BusinessException("Pedido no encontrado");

        if (!Roles.IsSuperadmin(_currentUser.Role) && existingOrder.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

        if (Roles.IsDeliveryman(_currentUser.Role))
        {
            if (!existingOrder.DeliveryManId.HasValue || existingOrder.DeliveryManId.Value != _currentUser.Id)
                throw new BusinessException("Solo puedes cambiar el estado de pedidos asignados a ti");

            var allowedTransitions =
                new[]
                {
                    (OrderStatus.OnTheWay, OrderStatus.Delivered),
                    (OrderStatus.Delivered, OrderStatus.OnTheWay),
                };

            var transition = (existingOrder.Status, request.StatusChange.Status);
            if (!allowedTransitions.Contains(transition))
                throw new BusinessException("Los domiciliarios solo pueden cambiar entre estados OnTheWay y Delivered");
        }
        else if (!_businessRules.IsStatusTransitionValid(existingOrder, request.StatusChange.Status, _currentUser.Role))
        {
            throw new BusinessException($"No puedes cambiar el estado de {existingOrder.Status} a {request.StatusChange.Status}");
        }

        if (existingOrder.Type == OrderType.Reservation
            && request.StatusChange.Status == OrderStatus.Ready)
        {
            var orderForTypeUpdate = await _orderRepository.GetByIdWithDetailsAsync(request.Id, cancellationToken)
                ?? throw new BusinessException("Pedido no encontrado");

            var detailCount = orderForTypeUpdate.OrderDetails?.Count ?? 0;
            if (detailCount < 1)
                throw new BusinessException(
                    "No se pudo preparar el pedido: faltan los productos en el sistema. Vuelva a abrir el pedido o pida a administracion verificarlo.");

            orderForTypeUpdate.Type = orderForTypeUpdate.AddressId.HasValue
                ? OrderType.Delivery
                : OrderType.Onsite;
            await _orderRepository.UpdateAsync(orderForTypeUpdate, cancellationToken);
            existingOrder.Type = orderForTypeUpdate.Type;
        }

        var routeIdSnapshot = existingOrder.DeliveryRouteId;
        var previousStatus = existingOrder.Status;

        if (previousStatus == OrderStatus.Delivered
            && request.StatusChange.Status == OrderStatus.Ready)
        {
            await ClearDeliveryAssignmentAsync(request.Id, cancellationToken);
        }

        var order = await _orderRepository.ChangeStatusAsync(
            request.Id,
            request.StatusChange.Status,
            request.StatusChange.Reason,
            cancellationToken);

        if (previousStatus == OrderStatus.Delivered && request.StatusChange.Status != OrderStatus.Delivered)
            await _loyaltyCycle.OnOrderLeftDeliveredAsync(order.Id, cancellationToken);

        if (request.StatusChange.Status == OrderStatus.Delivered
            && previousStatus != OrderStatus.Delivered
            && order.CustomerId.HasValue)
        {
            await _loyaltyCycle.OnOrderDeliveredAsync(order.Id, order.BranchId, order.CustomerId, cancellationToken);
        }

        var orderDto = _mapper.Map<OrderDto>(order);

        if (request.StatusChange.Status == OrderStatus.Ready)
        {
            if (DeliveryReadyNotificationEligibility.ShouldNotifyOwnDeliverymen(order))
                await _notificationService.NotifyOrderReadyToDelivery(orderDto);
            if (_externalDeliveryStatusSync is not null)
                await _externalDeliveryStatusSync.SyncReadyForPickupAsync(order.Id, cancellationToken);
            var role = (_currentUser.Role ?? string.Empty).Trim();
            if (Roles.IsKitchen(role))
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
                        "No se encolo comanda de cocina para pedido {OrderId} (sucursal {BranchId}). El estado si se actualizo.",
                        order.Id,
                        order.BranchId);
                }
            }
        }

        if (request.StatusChange.Status == OrderStatus.OnTheWay)
            await _notificationService.NotifyOrderAssignedToDelivery(orderDto);

        if (request.StatusChange.Status == OrderStatus.Cancelled)
            await _deliveryRouteWorkflow.OnOrderCancelledWhileRouteOpenAsync(request.Id, cancellationToken);

        if (request.StatusChange.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            await _deliveryRouteWorkflow.TryFinalizeRouteWhenAllTerminalAsync(
                request.Id,
                routeIdSnapshot,
                cancellationToken);

        return orderDto;
    }

    private async Task ClearDeliveryAssignmentAsync(int orderId, CancellationToken cancellationToken)
    {
        await _deliveryRouteWorkflow.OnOrderUnassignedAsync(orderId, cancellationToken);
        await _orderRepository.UnassignDeliveryManAsync(orderId, cancellationToken);
    }

    private async Task PromoteReservationDepositsToBankPaymentsAsync(int orderId, CancellationToken cancellationToken)
    {
        var deposits = await _context.ReservationDeposits
            .AsNoTracking()
            .Where(d =>
                d.OrderId == orderId
                && !d.IsEffective
                && d.BankId.HasValue
                && !d.AppId.HasValue)
            .OrderBy(d => d.ReceivedAt)
            .ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);

        if (deposits.Count == 0)
            return;

        var depositIds = deposits.Select(d => d.Id).ToList();
        var existingSourceIds = await _context.BankPayments
            .Where(bp => bp.SourceReservationDepositId.HasValue
                && depositIds.Contains(bp.SourceReservationDepositId.Value))
            .Select(bp => bp.SourceReservationDepositId!.Value)
            .ToListAsync(cancellationToken);

        var existingSet = existingSourceIds.ToHashSet();
        var newBankPayments = deposits
            .Where(d => !existingSet.Contains(d.Id))
            .Select(d => new BankPayment
            {
                OrderId = orderId,
                BankId = d.BankId!.Value,
                Amount = d.Amount,
                SourceReservationDepositId = d.Id,
                IsVerified = false,
            })
            .ToList();

        if (newBankPayments.Count == 0)
            return;

        _context.BankPayments.AddRange(newBankPayments);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class CancelOrderHandler : IRequestHandler<CancelOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly IReservationDepositRepository _reservationDepositRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly ILoyaltyCycleService _loyaltyCycle;
    private readonly IDeliveryRouteWorkflowService _deliveryRouteWorkflow;
    private readonly IClock _clock;
    private readonly IOrderNotificationService _notificationService;
    private readonly IExternalDeliveryStatusSyncService? _externalDeliveryStatusSync;

    public CancelOrderHandler(
        IOrderRepository orderRepository,
        IBankPaymentRepository bankPaymentRepository,
        IAppPaymentRepository appPaymentRepository,
        IReservationDepositRepository reservationDepositRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        ILoyaltyCycleService loyaltyCycle,
        IDeliveryRouteWorkflowService deliveryRouteWorkflow,
        IClock clock,
        IOrderNotificationService notificationService,
        IExternalDeliveryStatusSyncService? externalDeliveryStatusSync = null)
    {
        _orderRepository = orderRepository;
        _bankPaymentRepository = bankPaymentRepository;
        _appPaymentRepository = appPaymentRepository;
        _reservationDepositRepository = reservationDepositRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _loyaltyCycle = loyaltyCycle;
        _deliveryRouteWorkflow = deliveryRouteWorkflow;
        _clock = clock;
        _notificationService = notificationService;
        _externalDeliveryStatusSync = externalDeliveryStatusSync;
    }

    public async Task<OrderDto> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Cancellation.Reason))
            throw new BusinessException("La razón de cancelación es obligatoria");
        var cancellationReason = request.Cancellation.Reason.Trim();

        var existingOrder = await _orderRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existingOrder == null)
            throw new BusinessException("Pedido no encontrado");

        if (!Roles.IsSuperadmin(_currentUser.Role) && existingOrder.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

        if (!Roles.IsSuperadminOrAdminOrCashier(_currentUser.Role))
            throw new BusinessException("Solo administradores o cajeros pueden cancelar pedidos");

        if (existingOrder.Status == OrderStatus.Cancelled)
            throw new BusinessException("El pedido ya está cancelado");

        var routeIdSnapshot = existingOrder.DeliveryRouteId;
        var previousStatus = existingOrder.Status;
        var isRappiOrder = existingOrder.ExternalFulfillmentProvider?.Equals(
            "rappi",
            StringComparison.OrdinalIgnoreCase) == true;
        var appPayments = (await _appPaymentRepository.GetByOrderIdAsync(
            request.Id,
            cancellationToken)).ToList();

        if (isRappiOrder && appPayments.Any(x => x.IsSetted))
            throw new BusinessException(
                "El pedido Rappi ya fue liquidado y requiere conciliación antes de cancelarlo");

        if (isRappiOrder)
        {
            if (_externalDeliveryStatusSync is null
                || !await _externalDeliveryStatusSync.SyncCancellationAsync(
                    request.Id,
                    cancellationReason,
                    cancellationToken))
            {
                throw new BusinessException(
                    "No se encontró la orden externa de Rappi para sincronizar la cancelación");
            }
        }

        await CancelAssociatedPaymentsAsync(
            request.Id,
            cancellationReason,
            appPayments,
            isRappiOrder,
            cancellationToken);

        var order = await _orderRepository.CancelOrderAsync(
            request.Id,
            cancellationReason,
            cancellationToken);

        if (KitchenOrderNotificationEligibility.IsVisibleToActiveKitchen(existingOrder, _clock.UtcNow))
        {
            var preview = cancellationReason;
            if (preview.Length > 120)
                preview = preview[..120];
            await _notificationService.NotifyOrderCancelledToKitchen(existingOrder.BranchId, request.Id, preview);
        }

        if (previousStatus == OrderStatus.Delivered)
            await _loyaltyCycle.OnOrderLeftDeliveredAsync(order.Id, cancellationToken);

        await _deliveryRouteWorkflow.OnOrderCancelledWhileRouteOpenAsync(request.Id, cancellationToken);
        await _deliveryRouteWorkflow.TryFinalizeRouteWhenAllTerminalAsync(
            request.Id,
            routeIdSnapshot,
            cancellationToken);

        var orderDto = _mapper.Map<OrderDto>(order);
        if (previousStatus == OrderStatus.OnTheWay)
        {
            await _notificationService.NotifyOrderModifiedToDelivery(
                orderDto,
                "status");
        }

        return orderDto;
    }

    private async Task CancelAssociatedPaymentsAsync(
        int orderId,
        string reason,
        IReadOnlyCollection<AppPayment> appPayments,
        bool reverseAppPayments,
        CancellationToken cancellationToken = default)
    {
        foreach (var appPayment in appPayments)
        {
            if (!reverseAppPayments)
            {
                await _appPaymentRepository.DeleteAsync(appPayment.Id, cancellationToken);
                continue;
            }

            if (appPayment.IsReversed)
                continue;

            appPayment.IsReversed = true;
            appPayment.ReversedAt = _clock.UtcNow;
            appPayment.ReversalReason = $"Cancelado desde Señor Arroz: {reason}";
            await _appPaymentRepository.UpdateAsync(appPayment, cancellationToken);
        }

        var bankPayments = await _bankPaymentRepository.GetByOrderIdAsync(orderId, cancellationToken);
        foreach (var bankPayment in bankPayments)
            await _bankPaymentRepository.DeleteAsync(bankPayment.Id, cancellationToken);

        await _reservationDepositRepository.DeleteByOrderIdAsync(orderId, cancellationToken);
    }
}

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
        IOrderNotificationService notificationService)
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
    }

    public async Task<OrderDto> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Cancellation.Reason))
            throw new BusinessException("La razón de cancelación es obligatoria");

        var existingOrder = await _orderRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existingOrder == null)
            throw new BusinessException("Pedido no encontrado");

        if (!Roles.IsSuperadmin(_currentUser.Role) && existingOrder.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

        if (!Roles.IsAdminOrSuperadmin(_currentUser.Role))
            throw new BusinessException("Solo administradores pueden cancelar pedidos");

        if (existingOrder.Status == OrderStatus.Cancelled)
            throw new BusinessException("El pedido ya está cancelado");

        // Reserva con horario: mismo día calendario en Colombia que creación, prepareAt o entrega (reservedFor).
        if (IsScheduledReservation(existingOrder))
        {
            var now = _clock.UtcNow;
            var canCancelThisColombiaDay =
                ColombiaTimeHelper.IsColombiaTodayFromUtc(existingOrder.CreatedAt, now)
                || (existingOrder.PrepareAt.HasValue
                    && ColombiaTimeHelper.IsColombiaTodayFromUtc(existingOrder.PrepareAt.Value, now))
                || (existingOrder.ReservedFor.HasValue
                    && ColombiaTimeHelper.IsColombiaTodayFromUtc(existingOrder.ReservedFor.Value, now));

            if (!canCancelThisColombiaDay)
                throw new BusinessException(
                    "Las reservas con horario solo se pueden cancelar si hoy (Colombia) coincide con la fecha de registro del pedido, con la de inicio en cocina (prepareAt) o con la de entrega (reservedFor).");
        }

        var routeIdSnapshot = existingOrder.DeliveryRouteId;
        var previousStatus = existingOrder.Status;

        await CancelAssociatedPaymentsAsync(request.Id, cancellationToken);

        var order = await _orderRepository.CancelOrderAsync(
            request.Id,
            request.Cancellation.Reason,
            cancellationToken);

        if (KitchenOrderNotificationEligibility.IsVisibleToActiveKitchen(existingOrder, _clock.UtcNow))
        {
            var preview = request.Cancellation.Reason.Trim();
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

        return _mapper.Map<OrderDto>(order);
    }

    /// <summary>
    /// Reserva con ambas fechas definidas (cocina y entrega); la ventana de cancelación es el día Colombia de la creación,
    /// de <c>PrepareAt</c> o de <c>ReservedFor</c>.
    /// </summary>
    private static bool IsScheduledReservation(Order order) =>
        order.Type == OrderType.Reservation
        && order.PrepareAt.HasValue
        && order.ReservedFor.HasValue;

    private async Task CancelAssociatedPaymentsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var appPayments = await _appPaymentRepository.GetByOrderIdAsync(orderId, cancellationToken);
        foreach (var appPayment in appPayments)
            await _appPaymentRepository.DeleteAsync(appPayment.Id, cancellationToken);

        var bankPayments = await _bankPaymentRepository.GetByOrderIdAsync(orderId, cancellationToken);
        foreach (var bankPayment in bankPayments)
            await _bankPaymentRepository.DeleteAsync(bankPayment.Id, cancellationToken);

        await _reservationDepositRepository.DeleteByOrderIdAsync(orderId, cancellationToken);
    }
}

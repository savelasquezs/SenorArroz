using AutoMapper;
using MediatR;
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
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly ILoyaltyCycleService _loyaltyCycle;
    private readonly IDeliveryRouteWorkflowService _deliveryRouteWorkflow;

    public CancelOrderHandler(
        IOrderRepository orderRepository,
        IBankPaymentRepository bankPaymentRepository,
        IAppPaymentRepository appPaymentRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        ILoyaltyCycleService loyaltyCycle,
        IDeliveryRouteWorkflowService deliveryRouteWorkflow)
    {
        _orderRepository = orderRepository;
        _bankPaymentRepository = bankPaymentRepository;
        _appPaymentRepository = appPaymentRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _loyaltyCycle = loyaltyCycle;
        _deliveryRouteWorkflow = deliveryRouteWorkflow;
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

        // Reserva con horario de preparación y entrega: se mantiene la regla de mismo día (fecha UTC de creación).
        if (IsScheduledReservation(existingOrder))
        {
            var todayUtc = DateTime.UtcNow.Date;
            if (existingOrder.CreatedAt.Date != todayUtc)
                throw new BusinessException(
                    "Las reservas con horario de preparación y entrega solo se pueden cancelar el mismo día en que se registró el pedido.");
        }

        var routeIdSnapshot = existingOrder.DeliveryRouteId;
        var previousStatus = existingOrder.Status;

        await CancelAssociatedPaymentsAsync(request.Id, cancellationToken);

        var order = await _orderRepository.CancelOrderAsync(
            request.Id,
            request.Cancellation.Reason,
            cancellationToken);

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
    /// Reserva con ambas fechas definidas (cocina y entrega); a este caso le aplica la restricción de cancelación por día.
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
    }
}

using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
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

    public CancelOrderHandler(
        IOrderRepository orderRepository, 
        IBankPaymentRepository bankPaymentRepository,
        IAppPaymentRepository appPaymentRepository,
        IMapper mapper, 
        ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _bankPaymentRepository = bankPaymentRepository;
        _appPaymentRepository = appPaymentRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<OrderDto> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        // Validate cancellation reason
        if (string.IsNullOrWhiteSpace(request.Cancellation.Reason))
            throw new BusinessException("La razón de cancelación es obligatoria");

        // Get order first to validate access
        var existingOrder = await _orderRepository.GetByIdAsync(request.Id);
        if (existingOrder == null)
            throw new BusinessException("Pedido no encontrado");

        // Validate branch access
        if (_currentUser.Role != "superadmin" && existingOrder.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

        // Validate role permissions - only admin and superadmin can cancel
        if (!new[] { "superadmin", "admin" }.Contains(_currentUser.Role.ToLower()))
            throw new BusinessException("Solo administradores pueden cancelar pedidos");

        // Validate that order is from the same day
        var today = DateTime.Today;
        var orderDate = existingOrder.CreatedAt.Date;
        if (orderDate != today)
            throw new BusinessException("Solo se pueden cancelar pedidos del mismo día");

        // Validate that order is not already cancelled
        if (existingOrder.Status == Domain.Enums.OrderStatus.Cancelled)
            throw new BusinessException("El pedido ya está cancelado");

        // Cancel all associated payments
        await CancelAssociatedPaymentsAsync(request.Id);

        // Cancel the order
        var order = await _orderRepository.CancelOrderAsync(
            request.Id, 
            request.Cancellation.Reason);

        return _mapper.Map<OrderDto>(order);
    }

    private async Task CancelAssociatedPaymentsAsync(int orderId)
    {
        // Cancel App Payments
        var appPayments = await _appPaymentRepository.GetByOrderIdAsync(orderId);
        foreach (var appPayment in appPayments)
        {
            await _appPaymentRepository.DeleteAsync(appPayment.Id);
        }

        // Cancel Bank Payments (only unverified ones)
        var bankPayments = await _bankPaymentRepository.GetByOrderIdAsync(orderId);
        foreach (var bankPayment in bankPayments)
        {
            await _bankPaymentRepository.DeleteAsync(bankPayment.Id);
        }
    }
}

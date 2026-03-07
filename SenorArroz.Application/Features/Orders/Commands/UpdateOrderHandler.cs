using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class UpdateOrderHandler : IRequestHandler<UpdateOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IAddressRepository _addressRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IOrderBusinessRulesService _businessRules;
    private readonly IOrderNotificationService _notificationService;

    public UpdateOrderHandler(
        IOrderRepository orderRepository, 
        IAddressRepository addressRepository,
        IMapper mapper, 
        ICurrentUser currentUser,
        IOrderBusinessRulesService businessRules,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _addressRepository = addressRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _businessRules = businessRules;
        _notificationService = notificationService;
    }

    public async Task<OrderDto> Handle(UpdateOrderCommand request, CancellationToken cancellationToken)
    {
        var existingOrder = await _orderRepository.GetByIdAsync(request.Id);
        if (existingOrder == null)
            throw new BusinessException("Pedido no encontrado");

        // Validate branch access
        if (_currentUser.Role != "superadmin" && existingOrder.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

        // Validar si puede actualizar el pedido
        if (!_businessRules.CanUpdateOrder(existingOrder, _currentUser.Role))
            throw new BusinessException("No tienes permisos para modificar este pedido en su estado actual");

        // Validar si puede modificar productos
        if (request.Order.OrderDetails != null && !_businessRules.CanUpdateOrderProducts(existingOrder, _currentUser.Role))
            throw new BusinessException("No tienes permisos para modificar los productos de este pedido");

        // Validar prepare_at <= reserved_for cuando ambos tienen valor
        if (request.Order.PrepareAt.HasValue && request.Order.ReservedFor.HasValue
            && request.Order.PrepareAt.Value > request.Order.ReservedFor.Value)
        {
            throw new BusinessException("La hora de preparación no puede ser posterior a la hora de entrega");
        }

        // Cambio automático a Reservation: si se pone reserved_for con valor futuro
        if (request.Order.ReservedFor.HasValue && request.Order.ReservedFor.Value > DateTime.UtcNow)
        {
            existingOrder.Type = OrderType.Reservation;
        }

        // Apply the mapping
        _mapper.Map(request.Order, existingOrder);

        // Recalcular prepare_at si reserved_for cambió y prepare_at no se envió explícitamente
        if (existingOrder.ReservedFor.HasValue && !request.Order.PrepareAt.HasValue)
        {
            existingOrder.PrepareAt = existingOrder.ReservedFor.Value.AddHours(-1);
        }

        // Handle order type changes - clear delivery fields when changing to Onsite
        if (request.Order.Type.HasValue)
        {
            if (request.Order.Type == OrderType.Onsite)
            {
                existingOrder.AddressId = null;
                existingOrder.DeliveryFee = null;
                existingOrder.DeliveryManId = null;
            }
        }

        // Si cambia a Onsite/Delivery (no Reservation), limpiar reserved_for y prepare_at
        if (request.Order.Type.HasValue && request.Order.Type != OrderType.Reservation)
        {
            existingOrder.ReservedFor = null;
            existingOrder.PrepareAt = null;
        }

        // Handle address changes - update delivery fee from address if not provided
        if (request.Order.AddressId.HasValue && !request.Order.DeliveryFee.HasValue)
        {
            var address = await _addressRepository.GetByIdAsync(request.Order.AddressId.Value);
            if (address != null)
            {
                existingOrder.DeliveryFee = address.DeliveryFee;
            }
        }

        var updatedOrder = await _orderRepository.UpdateAsync(existingOrder);
        var result = _mapper.Map<OrderDto>(updatedOrder);

        // Si pedido en taken, prepare_at pasó a estar en el pasado y aún no en cocina → notificar
        if (updatedOrder.Status == OrderStatus.Taken
            && updatedOrder.PrepareAt.HasValue
            && updatedOrder.PrepareAt.Value <= DateTime.UtcNow
            && !updatedOrder.PreparedNotifiedAt.HasValue)
        {
            await _notificationService.NotifyReservationToKitchen(result);
            updatedOrder.PreparedNotifiedAt = DateTime.UtcNow;
            await _orderRepository.UpdateAsync(updatedOrder);
        }

        return result;
    }
}

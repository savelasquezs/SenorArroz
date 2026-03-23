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
        var existingOrder = await _orderRepository.GetByIdWithDetailsAsync(request.Id);
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

        // Validar distancia mínima de 40 min entre preparación y entrega
        if (request.Order.PrepareAt.HasValue && request.Order.ReservedFor.HasValue)
        {
            var diff = request.Order.ReservedFor.Value - request.Order.PrepareAt.Value;
            if (diff.TotalMinutes < 40)
            {
                throw new BusinessException("Debe haber al menos 40 minutos entre la hora de preparación y la hora de entrega");
            }
        }

        // Cambio automático a Reservation: si se pone reserved_for con valor futuro
        if (request.Order.ReservedFor.HasValue && request.Order.ReservedFor.Value > DateTime.UtcNow)
        {
            existingOrder.Type = OrderType.Reservation;
        }

        // Apply scalar field mapping first (details are handled explicitly below)
        _mapper.Map(request.Order, existingOrder);

        if (request.Order.OrderDetails != null)
        {
            if (!request.Order.OrderDetails.Any())
            {
                throw new BusinessException("El pedido debe tener al menos un producto. Si es el último, cancela el pedido.");
            }

            var incomingById = request.Order.OrderDetails
                .Where(d => d.Id > 0)
                .ToDictionary(d => d.Id);

            var detailsToRemove = existingOrder.OrderDetails
                .Where(d => d.Id > 0 && !incomingById.ContainsKey(d.Id))
                .ToList();

            foreach (var detailToRemove in detailsToRemove)
            {
                existingOrder.OrderDetails.Remove(detailToRemove);
            }

            foreach (var incoming in request.Order.OrderDetails)
            {
                var lineSubtotal = incoming.Quantity * incoming.UnitPrice - incoming.Discount;
                if (incoming.Id > 0)
                {
                    var existingDetail = existingOrder.OrderDetails.FirstOrDefault(d => d.Id == incoming.Id);
                    if (existingDetail == null)
                    {
                        continue;
                    }

                    existingDetail.ProductId = incoming.ProductId;
                    existingDetail.Quantity = incoming.Quantity;
                    existingDetail.UnitPrice = incoming.UnitPrice;
                    existingDetail.Discount = incoming.Discount;
                    existingDetail.Notes = incoming.Notes;
                    existingDetail.Subtotal = lineSubtotal;
                }
                else
                {
                    existingOrder.OrderDetails.Add(new Domain.Entities.OrderDetail
                    {
                        ProductId = incoming.ProductId,
                        Quantity = incoming.Quantity,
                        UnitPrice = incoming.UnitPrice,
                        Discount = incoming.Discount,
                        Notes = incoming.Notes,
                        Subtotal = lineSubtotal
                    });
                }
            }

            existingOrder.Subtotal = existingOrder.OrderDetails.Sum(d => d.Quantity * d.UnitPrice);
            existingOrder.DiscountTotal = existingOrder.OrderDetails.Sum(d => d.Discount);
            existingOrder.Total = existingOrder.OrderDetails.Sum(d => (d.Subtotal ?? (d.Quantity * d.UnitPrice - d.Discount)))
                + (existingOrder.DeliveryFee ?? 0);
        }

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

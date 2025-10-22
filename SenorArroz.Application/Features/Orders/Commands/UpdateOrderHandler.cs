using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
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

    public UpdateOrderHandler(
        IOrderRepository orderRepository, 
        IAddressRepository addressRepository,
        IMapper mapper, 
        ICurrentUser currentUser,
        IOrderBusinessRulesService businessRules)
    {
        _orderRepository = orderRepository;
        _addressRepository = addressRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _businessRules = businessRules;
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

        // Apply the mapping
        _mapper.Map(request.Order, existingOrder);

        // Handle order type changes - clear delivery fields when changing to Onsite
        if (request.Order.Type.HasValue)
        {
            if (request.Order.Type == Domain.Enums.OrderType.Onsite)
            {
                // Clear all delivery-related fields for onsite orders
                existingOrder.AddressId = null;
                existingOrder.DeliveryFee = null;
                existingOrder.DeliveryManId = null;
            }
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
        return _mapper.Map<OrderDto>(updatedOrder);
    }
}

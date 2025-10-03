using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetAvailableOrdersForDeliveryHandler : IRequestHandler<GetAvailableOrdersForDeliveryQuery, List<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetAvailableOrdersForDeliveryHandler(IOrderRepository orderRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<List<OrderDto>> Handle(GetAvailableOrdersForDeliveryQuery request, CancellationToken cancellationToken)
    {
        // Validate that user is a deliveryman
        if (_currentUser.Role.ToLower() != "deliveryman")
            throw new BusinessException("Solo los domiciliarios pueden ver pedidos disponibles para entrega");

        // Determine branch filter
        int branchId;
        if (request.BranchId.HasValue && request.BranchId.Value > 0)
        {
            // Validate branch access
            if (_currentUser.Role != "superadmin" && request.BranchId.Value != _currentUser.BranchId)
                throw new BusinessException("No tienes permisos para ver pedidos de esta sucursal");
            branchId = request.BranchId.Value;
        }
        else
        {
            // Use current user's branch
            branchId = _currentUser.BranchId;
        }

        // Get available orders (Ready status, no deliveryman assigned)
        var availableOrders = await _orderRepository.GetAvailableOrdersForDeliveryAsync(branchId);

        return _mapper.Map<List<OrderDto>>(availableOrders);
    }
}

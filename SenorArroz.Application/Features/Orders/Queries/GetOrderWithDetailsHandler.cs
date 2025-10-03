using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetOrderWithDetailsHandler : IRequestHandler<GetOrderWithDetailsQuery, OrderWithDetailsDto?>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetOrderWithDetailsHandler(IOrderRepository orderRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<OrderWithDetailsDto?> Handle(GetOrderWithDetailsQuery request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdWithFullDetailsAsync(request.Id);
        if (order == null)
            return null;

        // Validate branch access
        if (_currentUser.Role != "superadmin" && order.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para ver pedidos de esta sucursal");

        return _mapper.Map<OrderWithDetailsDto>(order);
    }
}

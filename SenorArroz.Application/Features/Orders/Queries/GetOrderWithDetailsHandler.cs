using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetOrderWithDetailsHandler : IRequestHandler<GetOrderWithDetailsQuery, OrderWithDetailsDto?>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;

    public GetOrderWithDetailsHandler(IOrderRepository orderRepository, IMapper mapper)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
    }

    public async Task<OrderWithDetailsDto?> Handle(GetOrderWithDetailsQuery request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdWithFullDetailsAsync(request.Id);
        return order != null ? _mapper.Map<OrderWithDetailsDto>(order) : null;
    }
}

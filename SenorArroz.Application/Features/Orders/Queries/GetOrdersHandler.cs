using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetOrdersHandler : IRequestHandler<GetOrdersQuery, PagedResult<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;

    public GetOrdersHandler(IOrderRepository orderRepository, IMapper mapper)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
    }

    public async Task<PagedResult<OrderDto>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        var result = await _orderRepository.GetAllAsync(
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        return new PagedResult<OrderDto>
        {
            Items = _mapper.Map<List<OrderDto>>(result.Items),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages
        };
    }
}

using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Orders.Queries;

public class SearchOrdersHandler : IRequestHandler<SearchOrdersQuery, PagedResult<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;

    public SearchOrdersHandler(IOrderRepository orderRepository, IMapper mapper)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
    }

    public async Task<PagedResult<OrderDto>> Handle(SearchOrdersQuery request, CancellationToken cancellationToken)
    {
        var result = await _orderRepository.SearchOrdersAsync(
            request.SearchTerm,
            request.BranchId,
            request.CustomerId,
            request.DeliveryManId,
            request.Status,
            request.Type,
            request.FromDate,
            request.ToDate,
            request.MinAmount,
            request.MaxAmount,
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

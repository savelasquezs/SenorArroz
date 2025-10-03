using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetOrdersByStatusHandler : IRequestHandler<GetOrdersByStatusQuery, PagedResult<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;

    public GetOrdersByStatusHandler(IOrderRepository orderRepository, IMapper mapper)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
    }

    public async Task<PagedResult<OrderDto>> Handle(GetOrdersByStatusQuery request, CancellationToken cancellationToken)
    {
        var result = await _orderRepository.GetByStatusAsync(
            request.Status,
            request.BranchId,
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

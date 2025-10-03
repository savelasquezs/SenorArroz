using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetOrdersHandler : IRequestHandler<GetOrdersQuery, PagedResult<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetOrdersHandler(IOrderRepository orderRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<OrderDto>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        // Determine branch filter based on user role
        int? branchFilter = null;
        if (_currentUser.Role != "superadmin")
        {
            branchFilter = _currentUser.BranchId;
        }
        else if (request.BranchId > 0)
        {
            // Superadmin can optionally filter by specific branch
            branchFilter = request.BranchId;
        }

        var result = await _orderRepository.GetAllAsync(
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        // Filter by branch if needed
        var filteredItems = result.Items;
        if (branchFilter.HasValue)
        {
            filteredItems = result.Items.Where(o => o.BranchId == branchFilter.Value).ToList();
        }

        return new PagedResult<OrderDto>
        {
            Items = _mapper.Map<List<OrderDto>>(filteredItems),
            TotalCount = branchFilter.HasValue ? filteredItems.Count() : result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = branchFilter.HasValue ? 
                (int)Math.Ceiling((double)filteredItems.Count() / result.PageSize) : 
                result.TotalPages
        };
    }
}

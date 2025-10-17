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

        // Establecer filtros de fecha por defecto (día actual) si no se especifican
        var fromDate = request.FromDate ?? DateTime.UtcNow.Date; // Inicio del día actual (00:00:00)
        var toDate = request.ToDate ?? DateTime.UtcNow.Date.AddDays(1).AddTicks(-1); // Fin del día actual (23:59:59.999)

        var result = await _orderRepository.GetAllAsync(
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        // Aplicar filtros
        var filteredItems = result.Items.AsQueryable();
        
        // Filtrar por branch si es necesario
        if (branchFilter.HasValue)
        {
            filteredItems = filteredItems.Where(o => o.BranchId == branchFilter.Value);
        }

        // Filtrar por rango de fechas
        filteredItems = filteredItems.Where(o => o.CreatedAt >= fromDate && o.CreatedAt <= toDate);

        var filteredList = filteredItems.ToList();
        var totalFiltered = filteredList.Count;

        return new PagedResult<OrderDto>
        {
            Items = _mapper.Map<List<OrderDto>>(filteredList),
            TotalCount = totalFiltered,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = (int)Math.Ceiling((double)totalFiltered / result.PageSize)
        };
    }
}

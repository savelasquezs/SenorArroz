using AutoMapper;
using Microsoft.EntityFrameworkCore;
using MediatR;
using SenorArroz.Application.Common.Helpers;
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
    private readonly IApplicationDbContext _db;

    public GetOrdersHandler(IOrderRepository orderRepository, IMapper mapper, ICurrentUser currentUser, IApplicationDbContext db)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<PagedResult<OrderDto>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        // Determine branch filter based on user role
        int? branchFilter = null;
        if (!Roles.IsSuperadmin(_currentUser.Role))
        {
            branchFilter = _currentUser.BranchId;
        }
        else if (request.BranchId > 0)
        {
            // Superadmin can optionally filter by specific branch
            branchFilter = request.BranchId;
        }

        // Si el frontend envía fechas, las interpretamos como hora de Colombia y convertimos a UTC
        // Si no envía, usamos el día actual en Colombia
        DateTime fromDateUtc;
        DateTime toDateUtc;

        if (request.FromDate.HasValue)
        {
            // Frontend envió fecha, asumimos que es hora de Colombia
            fromDateUtc = ColombiaTimeHelper.ConvertColombiaToUtc(request.FromDate.Value);
        }
        else
        {
            // Frontend no envió fecha, usamos inicio del día actual en Colombia
            fromDateUtc = ColombiaTimeHelper.GetTodayStartInUtc();
        }

        if (request.ToDate.HasValue)
        {
            // Frontend envió fecha, asumimos que es hora de Colombia
            toDateUtc = ColombiaTimeHelper.ConvertColombiaToUtc(request.ToDate.Value);
        }
        else if (request.FromDate.HasValue)
        {
            // Frontend envió FromDate pero no ToDate: usar el fin del mismo día de FromDate
            var endOfFromDate = request.FromDate.Value.Date.AddDays(1).AddTicks(-1);
            toDateUtc = ColombiaTimeHelper.ConvertColombiaToUtc(endOfFromDate);
        }
        else
        {
            // Frontend no envió ninguna fecha, usamos fin del día actual en Colombia
            toDateUtc = ColombiaTimeHelper.GetTodayEndInUtc();
        }

        // Aplicar filtros directamente en la consulta SQL
        var result = await _orderRepository.GetAllAsync(
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder,
            fromDateUtc,       // Filtro de fecha inicio en UTC
            toDateUtc,         // Filtro de fecha fin en UTC
            branchFilter,      // Filtro de sucursal
            request.ForKitchen,
            cancellationToken);

        var dtos = _mapper.Map<List<OrderDto>>(result.Items);
        if (request.ForKitchen && dtos.Count > 0)
        {
            // Fetch lines only for the already authorized page. Global EF tenant filters
            // and PostgreSQL RLS also apply to OrderDetails and their relationships.
            var ids = dtos.Select(o => o.Id).ToArray();
            var details = await _db.OrderDetails.AsNoTracking()
                .Where(d => ids.Contains(d.OrderId))
                .Include(d => d.Product).ThenInclude(p => p.Category)
                .OrderBy(d => d.OrderId).ThenBy(d => d.Id)
                .ToListAsync(cancellationToken);
            var grouped = details.GroupBy(d => d.OrderId)
                .ToDictionary(g => g.Key, g => _mapper.Map<List<OrderDetailDto>>(g.ToList()));
            foreach (var dto in dtos)
                dto.KitchenDetails = grouped.GetValueOrDefault(dto.Id) ?? [];
        }

        return new PagedResult<OrderDto>
        {
            Items = dtos,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages
        };
    }
}

using AutoMapper;
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
            fromDateUtc,    // Filtro de fecha inicio en UTC
            toDateUtc,      // Filtro de fecha fin en UTC
            branchFilter);  // Filtro de sucursal

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

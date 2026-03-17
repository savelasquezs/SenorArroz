using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Orders.Queries;

public class SearchOrdersHandler : IRequestHandler<SearchOrdersQuery, PagedResult<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IApplicationDbContext _context;

    public SearchOrdersHandler(
        IOrderRepository orderRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        IApplicationDbContext context)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _context = context;
    }

    public async Task<PagedResult<OrderDto>> Handle(SearchOrdersQuery request, CancellationToken cancellationToken)
    {
        // Determine branch filter based on user role
        int? branchFilter = null;
        if (_currentUser.Role != "superadmin")
        {
            branchFilter = _currentUser.BranchId;
        }
        else if (request.BranchId > 0)
        {
            branchFilter = request.BranchId;
        }

        var result = await _orderRepository.SearchOrdersAsync(
            request.SearchTerm,
            branchFilter,
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
            request.SortOrder,
            request.ReservedFromDate,
            request.ReservedToDate);

        var dtos = _mapper.Map<List<OrderDto>>(result.Items);

        // Enriquecer con total abonado para pedidos de tipo reserva
        var reservationIds = dtos
            .Where(d => d.Type == OrderType.Reservation)
            .Select(d => d.Id)
            .ToList();

        if (reservationIds.Count > 0)
        {
            var depositTotals = await _context.ReservationDeposits
                .Where(rd => reservationIds.Contains(rd.OrderId))
                .GroupBy(rd => rd.OrderId)
                .Select(g => new { OrderId = g.Key, Total = g.Sum(rd => rd.Amount) })
                .ToDictionaryAsync(x => x.OrderId, x => x.Total, cancellationToken);

            foreach (var dto in dtos.Where(d => d.Type == OrderType.Reservation))
            {
                dto.TotalDeposited = depositTotals.TryGetValue(dto.Id, out var total) ? total : 0;
            }
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

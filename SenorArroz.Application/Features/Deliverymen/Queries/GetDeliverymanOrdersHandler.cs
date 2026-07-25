using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Deliverymen.Queries;

public class GetDeliverymanOrdersHandler : IRequestHandler<GetDeliverymanOrdersQuery, PagedResult<OrderDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;
    private readonly IMapper _mapper;

    public GetDeliverymanOrdersHandler(
        IUserRepository userRepository,
        IOrderRepository orderRepository,
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IBranchContext branchContext,
        IMapper mapper)
    {
        _userRepository = userRepository;
        _orderRepository = orderRepository;
        _db = db;
        _currentUser = currentUser;
        _branchContext = branchContext;
        _mapper = mapper;
    }

    public async Task<PagedResult<OrderDto>> Handle(GetDeliverymanOrdersQuery request, CancellationToken cancellationToken)
    {
        var deliveryman = await _userRepository.GetByIdAsync(request.DeliverymanId, cancellationToken);
        if (deliveryman == null)
            throw new BusinessException("El domiciliario no existe");
        _branchContext.EnsureAccess(deliveryman.BranchId);
        if (deliveryman.Role != UserRole.Deliveryman)
            throw new BusinessException("El usuario no es un domiciliario");
        if (!deliveryman.Active)
            throw new BusinessException("El domiciliario no está activo");

        var branchId = Roles.IsSuperadmin(_currentUser.Role)
            ? deliveryman.BranchId
            : _currentUser.BranchId;
        if (!Roles.IsSuperadmin(_currentUser.Role) && deliveryman.BranchId != branchId)
            throw new BusinessException("No tienes permisos para ver los datos de este domiciliario");

        var (fromDate, toDate) = ResolveDateRange(request);
        var multiDay = IsMultiDayRequest(request);
        var useSettlementCycle = !multiDay;
        var colDateOnly = DateOnly.FromDateTime(
            request.Date?.Date
            ?? request.FromDate?.Date
            ?? ColombiaTimeHelper.GetNowInColombia().Date);

        DeliverymanDayState? dayState = null;
        if (!multiDay)
        {
            dayState = await _db.DeliverymanDayStates
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.BranchId == branchId
                         && s.DeliverymanId == request.DeliverymanId
                         && s.Date == colDateOnly,
                    cancellationToken);
        }

        var lastLiquidationAtUtc = useSettlementCycle ? dayState?.LastLiquidationAtUtc : null;

        var orders = (await DeliverymanDeliveredOrdersQuery.LoadAllDeliveredInRangeAsync(
                _orderRepository,
                branchId,
                request.DeliverymanId,
                fromDate,
                toDate,
                cancellationToken))
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

        var cycleOrders = DeliverymanSettlementCycleHelper.FilterOrdersForCycle(
            orders, fromDate, toDate, lastLiquidationAtUtc, useSettlementCycle);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var total = cycleOrders.Count;
        var skip = (page - 1) * pageSize;
        var pageItems = cycleOrders
            .Skip(skip)
            .Take(pageSize)
            .Select(o => _mapper.Map<OrderDto>(o))
            .ToList();

        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

        return new PagedResult<OrderDto>
        {
            Items = pageItems,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
        };
    }

    private static (DateTime from, DateTime to) ResolveDateRange(GetDeliverymanOrdersQuery request)
    {
        DateTime fromUtc;
        DateTime toUtc;
        if (request.FromDate.HasValue && request.ToDate.HasValue)
        {
            fromUtc = ColombiaTimeHelper.ConvertColombiaToUtc(request.FromDate.Value);
            var toDate = request.ToDate.Value;
            if (toDate.TimeOfDay == TimeSpan.Zero)
                toDate = toDate.Date.AddDays(1).AddTicks(-1);
            toUtc = ColombiaTimeHelper.ConvertColombiaToUtc(toDate);
        }
        else
        {
            var date = request.Date?.Date ?? ColombiaTimeHelper.GetNowInColombia().Date;
            fromUtc = ColombiaTimeHelper.ConvertColombiaToUtc(date);
            toUtc = ColombiaTimeHelper.ConvertColombiaToUtc(date.AddDays(1).AddTicks(-1));
        }

        return (fromUtc, toUtc);
    }

    private static bool IsMultiDayRequest(GetDeliverymanOrdersQuery request)
    {
        if (request.FromDate.HasValue && request.ToDate.HasValue)
            return request.FromDate.Value.Date != request.ToDate.Value.Date;
        return false;
    }
}

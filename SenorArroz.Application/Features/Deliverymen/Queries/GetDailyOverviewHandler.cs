using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;
using SenorArroz.Application.Features.Deliverymen.DTOs;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Deliverymen.Queries;

public class GetDailyOverviewHandler : IRequestHandler<GetDailyOverviewQuery, DailyOverviewDto>
{
    private const decimal DefaultBaseAmount = 55000m;

    private readonly IUserRepository _userRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IDeliverymanAdvanceRepository _advanceRepository;
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public GetDailyOverviewHandler(
        IUserRepository userRepository,
        IOrderRepository orderRepository,
        IDeliverymanAdvanceRepository advanceRepository,
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IMapper mapper)
    {
        _userRepository = userRepository;
        _orderRepository = orderRepository;
        _advanceRepository = advanceRepository;
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<DailyOverviewDto> Handle(GetDailyOverviewQuery request, CancellationToken cancellationToken)
    {
        var (fromDate, toDate) = ResolveDateRange(request);
        var branchId = ResolveBranchId(request);
        var isSingleCalendarDay = !(request.FromDate.HasValue && request.ToDate.HasValue);

        List<DeliverymanDayState> dayStatesForBranchDate = [];
        if (isSingleCalendarDay && branchId.HasValue)
        {
            var colDate = request.Date?.Date ?? ColombiaTimeHelper.GetNowInColombia().Date;
            var colDateOnly = DateOnly.FromDateTime(colDate);
            dayStatesForBranchDate = await _db.DeliverymanDayStates
                .AsNoTracking()
                .Where(s => s.BranchId == branchId.Value && s.Date == colDateOnly)
                .ToListAsync(cancellationToken);
        }

        var stateByBranchAndDm = dayStatesForBranchDate.ToDictionary(s => (s.BranchId, s.DeliverymanId));

        // 1. Obtener domiciliarios activos de la sucursal
        var allUsers = await _userRepository.GetAllAsync(branchId, cancellationToken);
        var deliverymen = allUsers
            .Where(u => u.Role == UserRole.Deliveryman && u.Active)
            .OrderBy(u => u.Name)
            .ToList();

        // 2. Pedidos entregados del día: delivery + onsite con domiciliario asignado
        var ordersDelivery = await _orderRepository.SearchOrdersAsync(
            searchTerm: null,
            branchId: branchId,
            customerId: null,
            deliveryManId: null,
            status: OrderStatus.Delivered,
            type: OrderType.Delivery,
            fromDate: fromDate,
            toDate: toDate,
            minAmount: null,
            maxAmount: null,
            page: 1,
            pageSize: 500,
            sortBy: "CreatedAt",
            sortOrder: "desc");
        var ordersOnsite = await _orderRepository.SearchOrdersAsync(
            searchTerm: null,
            branchId: branchId,
            customerId: null,
            deliveryManId: null,
            status: OrderStatus.Delivered,
            type: OrderType.Onsite,
            fromDate: fromDate,
            toDate: toDate,
            minAmount: null,
            maxAmount: null,
            page: 1,
            pageSize: 500,
            sortBy: "CreatedAt",
            sortOrder: "desc");

        var ordersByDeliveryman = DeliverymanSettlementCycleHelper.UnionOrdersById(ordersDelivery.Items, ordersOnsite.Items)
            .Where(o => o.DeliveryManId.HasValue)
            .GroupBy(o => o.DeliveryManId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var onTheWayDelivery = await _orderRepository.SearchOrdersAsync(
            searchTerm: null,
            branchId: branchId,
            customerId: null,
            deliveryManId: null,
            status: OrderStatus.OnTheWay,
            type: OrderType.Delivery,
            fromDate: null,
            toDate: null,
            minAmount: null,
            maxAmount: null,
            page: 1,
            pageSize: 2000,
            sortBy: "CreatedAt",
            sortOrder: "desc");
        var onTheWayOnsite = await _orderRepository.SearchOrdersAsync(
            searchTerm: null,
            branchId: branchId,
            customerId: null,
            deliveryManId: null,
            status: OrderStatus.OnTheWay,
            type: OrderType.Onsite,
            fromDate: null,
            toDate: null,
            minAmount: null,
            maxAmount: null,
            page: 1,
            pageSize: 2000,
            sortBy: "CreatedAt",
            sortOrder: "desc");
        var onTheWayCountByDeliveryman = DeliverymanSettlementCycleHelper.UnionOrdersById(onTheWayDelivery.Items, onTheWayOnsite.Items)
            .Where(o => o.DeliveryManId.HasValue)
            .GroupBy(o => o.DeliveryManId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        // 3. Obtener abonos del período
        var advancesResult = await _advanceRepository.GetPagedAsync(
            deliverymanId: null,
            branchId: branchId,
            fromDate: fromDate,
            toDate: toDate,
            page: 1,
            pageSize: 3000,
            sortBy: "createdAt",
            sortOrder: "desc");

        // 4. Mapear advances a DTOs para la tabla
        var advanceDtos = advancesResult.Items
            .Select(a => _mapper.Map<DeliverymanAdvanceDto>(a))
            .ToList();

        // 5. Construir stats por domiciliario (todos los activos)
        var deliverymenStats = new List<DeliverymanDayStatsDto>();
        foreach (var dm in deliverymen)
        {
            DateTime? lastLiq = null;
            if (isSingleCalendarDay
                && stateByBranchAndDm.TryGetValue((dm.BranchId, dm.Id), out var dmState))
                lastLiq = dmState.LastLiquidationAtUtc;

            var rawOrders = ordersByDeliveryman.GetValueOrDefault(dm.Id, new List<Order>());
            var orders = DeliverymanSettlementCycleHelper.FilterOrdersForCycle(
                rawOrders, fromDate, toDate, lastLiq, isSingleCalendarDay);

            var totalAdvances = advancesResult.Items
                .Where(a => a.DeliverymanId == dm.Id)
                .Where(a => DeliverymanSettlementCycleHelper.IsAdvanceInSettlementCycle(
                    a.CreatedAt, fromDate, toDate, lastLiq, isSingleCalendarDay))
                .Sum(a => a.Amount);

            var baseAmount = DefaultBaseAmount;

            var totalCash = DeliverymanSettlementCycleHelper.SumCashFromOrders(orders);
            var totalDeliveryFee = orders.Sum(o => o.DeliveryFee ?? 0);
            var avgTime = CalculateAverageDeliveryTimeMinutes(orders);
            var cashToDeliver = totalCash + baseAmount - totalAdvances;
            var currentBalance = cashToDeliver;

            deliverymenStats.Add(new DeliverymanDayStatsDto
            {
                DeliverymanId = dm.Id,
                DeliverymanName = dm.Name,
                OrdersCount = orders.Count,
                TotalCollected = totalCash,
                TotalAdvances = totalAdvances,
                TotalDeliveryFee = totalDeliveryFee,
                CashToDeliver = cashToDeliver,
                BaseAmount = baseAmount,
                CurrentBalance = currentBalance,
                AverageDeliveryTimeMinutes = avgTime,
                DayBlocked = false,
                LiquidationMode = DeliverymanDayLiquidationMode.None,
                OrdersOnTheWayCount = onTheWayCountByDeliveryman.GetValueOrDefault(dm.Id, 0)
            });
        }

        if (isSingleCalendarDay)
        {
            var dmById = deliverymen.ToDictionary(d => d.Id);
            foreach (var st in deliverymenStats)
            {
                if (!dmById.TryGetValue(st.DeliverymanId, out var dmUser))
                    continue;
                if (stateByBranchAndDm.TryGetValue((dmUser.BranchId, st.DeliverymanId), out var ds))
                {
                    st.DayBlocked = ds.Blocked;
                    st.LiquidationMode = ds.LiquidationMode;
                }
            }
        }

        return new DailyOverviewDto
        {
            Deliverymen = deliverymenStats,
            Advances = advanceDtos
        };
    }

    private static (DateTime from, DateTime to) ResolveDateRange(GetDailyOverviewQuery request)
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
            // Fecha del día en Colombia (no UTC): abonos y pedidos del "día" que ve el usuario
            var date = request.Date?.Date ?? ColombiaTimeHelper.GetNowInColombia().Date;
            fromUtc = ColombiaTimeHelper.ConvertColombiaToUtc(date);
            toUtc = ColombiaTimeHelper.ConvertColombiaToUtc(date.AddDays(1).AddTicks(-1));
        }
        return (fromUtc, toUtc);
    }

    private int? ResolveBranchId(GetDailyOverviewQuery request)
    {
        if (!Roles.IsSuperadmin(_currentUser.Role))
            return _currentUser.BranchId;
        return request.BranchId;
    }

    private static int CalculateAverageDeliveryTimeMinutes(List<Order> orders)
    {
        if (orders.Count == 0) return 0;
        var totalMs = 0.0;
        var count = 0;
        foreach (var order in orders)
        {
            var statusTimes = order.GetStatusTimes();
            if (statusTimes.TryGetValue("ready", out var readyTime) &&
                statusTimes.TryGetValue("delivered", out var deliveredTime))
            {
                totalMs += (deliveredTime - readyTime).TotalMilliseconds;
                count++;
            }
        }
        if (count == 0) return 0;
        return (int)Math.Round(totalMs / count / 1000 / 60);
    }
}

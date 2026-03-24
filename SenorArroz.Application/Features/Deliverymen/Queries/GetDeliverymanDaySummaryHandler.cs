using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Deliverymen.DTOs;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Deliverymen.Queries;

public class GetDeliverymanDaySummaryHandler : IRequestHandler<GetDeliverymanDaySummaryQuery, DeliverymanDaySummaryDto>
{
    private const decimal DefaultBaseAmount = 55000m;

    private readonly IUserRepository _userRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IDeliverymanAdvanceRepository _advanceRepository;
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public GetDeliverymanDaySummaryHandler(
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

    public async Task<DeliverymanDaySummaryDto> Handle(GetDeliverymanDaySummaryQuery request, CancellationToken cancellationToken)
    {
        var (fromDate, toDate) = ResolveDateRange(request);

        // 1. Validar que el domiciliario existe y es de la sucursal
        var deliveryman = await _userRepository.GetByIdAsync(request.DeliverymanId, cancellationToken);
        if (deliveryman == null)
            throw new BusinessException("El domiciliario no existe");
        if (deliveryman.Role != UserRole.Deliveryman)
            throw new BusinessException("El usuario no es un domiciliario");
        if (!deliveryman.Active)
            throw new BusinessException("El domiciliario no está activo");

        var branchId = _currentUser.Role == "superadmin"
            ? deliveryman.BranchId
            : _currentUser.BranchId;
        if (_currentUser.Role != "superadmin" && deliveryman.BranchId != branchId)
            throw new BusinessException("No tienes permisos para ver los datos de este domiciliario");

        var multiDay = IsMultiDaySummaryRequest(request);
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

        // 2. Obtener pedidos del domiciliario en el rango
        var ordersResult = await _orderRepository.SearchOrdersAsync(
            searchTerm: null,
            branchId: branchId,
            customerId: null,
            deliveryManId: request.DeliverymanId,
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

        var orders = ordersResult.Items.ToList();
        var cycleOrders = DeliverymanSettlementCycleHelper.FilterOrdersForCycle(
            orders, fromDate, toDate, lastLiquidationAtUtc, useSettlementCycle);

        var onTheWayResult = await _orderRepository.SearchOrdersAsync(
            searchTerm: null,
            branchId: branchId,
            customerId: null,
            deliveryManId: request.DeliverymanId,
            status: OrderStatus.OnTheWay,
            type: OrderType.Delivery,
            fromDate: null,
            toDate: null,
            minAmount: null,
            maxAmount: null,
            page: 1,
            pageSize: 1,
            sortBy: "CreatedAt",
            sortOrder: "desc");

        // 3. Total de abonos del ciclo (o del rango si es multi-día)
        var totalAdvances = await _advanceRepository.GetTotalAdvancesForSettlementCycleAsync(
            request.DeliverymanId,
            fromDate,
            toDate,
            lastLiquidationAtUtc,
            useSettlementCycle);

        var baseAmount = request.BaseAmount is > 0 ? request.BaseAmount.Value : DefaultBaseAmount;

        // 4. Calcular stats
        var totalCash = DeliverymanSettlementCycleHelper.SumCashFromOrders(cycleOrders);
        var totalDeliveryFee = cycleOrders.Sum(o => o.DeliveryFee ?? 0);
        var avgTime = CalculateAverageDeliveryTimeMinutes(cycleOrders);
        var cashToDeliver = totalCash + baseAmount - totalAdvances;
        var currentBalance = cashToDeliver;

        var stats = new DeliverymanDayStatsDto
        {
            DeliverymanId = deliveryman.Id,
            DeliverymanName = deliveryman.Name,
            OrdersCount = cycleOrders.Count,
            TotalCollected = totalCash,
            TotalAdvances = totalAdvances,
            TotalDeliveryFee = totalDeliveryFee,
            CashToDeliver = cashToDeliver,
            BaseAmount = baseAmount,
            CurrentBalance = currentBalance,
            AverageDeliveryTimeMinutes = avgTime,
            DayBlocked = dayState?.Blocked ?? false,
            LiquidationMode = dayState?.LiquidationMode ?? DeliverymanDayLiquidationMode.None,
            OrdersOnTheWayCount = onTheWayResult.TotalCount
        };

        // 5. Mapear pedidos a DTOs (solo ciclo)
        var orderDtos = cycleOrders.Select(o => _mapper.Map<OrderDto>(o)).ToList();

        // 6. Resumen del día completo (sin filtro de ciclo) — referencia, no sustituye el cuadre del ciclo
        var fullDayOrdersList = DeliverymanSettlementCycleHelper.FilterOrdersForCycle(
            orders, fromDate, toDate, null, useSettlementCycle: false);
        var fullDayAdvances = await _advanceRepository.GetTotalAdvancesForSettlementCycleAsync(
            request.DeliverymanId,
            fromDate,
            toDate,
            lastLiquidationAtUtc: null,
            useSettlementCycle: false);

        var fullDayCash = DeliverymanSettlementCycleHelper.SumCashFromOrders(fullDayOrdersList);
        var fullDayDeliveryFee = fullDayOrdersList.Sum(o => o.DeliveryFee ?? 0);
        var fullDayAvgTime = CalculateAverageDeliveryTimeMinutes(fullDayOrdersList);
        var fullDayCashToDeliver = fullDayCash + baseAmount - fullDayAdvances;
        var fullDayStats = new DeliverymanDayStatsDto
        {
            DeliverymanId = deliveryman.Id,
            DeliverymanName = deliveryman.Name,
            OrdersCount = fullDayOrdersList.Count,
            TotalCollected = fullDayCash,
            TotalAdvances = fullDayAdvances,
            TotalDeliveryFee = fullDayDeliveryFee,
            CashToDeliver = fullDayCashToDeliver,
            BaseAmount = baseAmount,
            CurrentBalance = fullDayCashToDeliver,
            AverageDeliveryTimeMinutes = fullDayAvgTime,
            DayBlocked = dayState?.Blocked ?? false,
            LiquidationMode = dayState?.LiquidationMode ?? DeliverymanDayLiquidationMode.None,
            OrdersOnTheWayCount = onTheWayResult.TotalCount
        };

        var fullDayOrderDtos = fullDayOrdersList.Select(o => _mapper.Map<OrderDto>(o)).ToList();

        return new DeliverymanDaySummaryDto
        {
            Stats = stats,
            Orders = orderDtos,
            FullDayStats = fullDayStats,
            FullDayOrders = fullDayOrderDtos
        };
    }

    private static (DateTime from, DateTime to) ResolveDateRange(GetDeliverymanDaySummaryQuery request)
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

    private static bool IsMultiDaySummaryRequest(GetDeliverymanDaySummaryQuery request)
    {
        if (request.FromDate.HasValue && request.ToDate.HasValue)
            return request.FromDate.Value.Date != request.ToDate.Value.Date;
        return false;
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

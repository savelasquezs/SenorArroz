using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Application.Features.Dashboard.Services;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardDeliveryHandler : IRequestHandler<GetDashboardDeliveryQuery, DashboardDeliveryResponseDto>
{
    private const int MaxRangeDays = 400;

    private readonly IOrderRepository _orderRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IApplicationDbContext _db;

    public GetDashboardDeliveryHandler(
        IOrderRepository orderRepository,
        IUserRepository userRepository,
        ICurrentUser currentUser,
        IApplicationDbContext db)
    {
        _orderRepository = orderRepository;
        _userRepository = userRepository;
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<DashboardDeliveryResponseDto> Handle(
        GetDashboardDeliveryQuery request,
        CancellationToken cancellationToken)
    {
        var (from, to) = ColombiaTimeHelper.NormalizeDashboardRangeUtc(request.FromUtc, request.ToUtc, MaxRangeDays);

        var branchFilter = ResolveBranchFilter(request.BranchId);

        var deliveryManId = request.DeliveryManId;
        if (deliveryManId.HasValue)
            await AssertDeliveryManInScopeAsync(deliveryManId.Value, branchFilter, cancellationToken);

        var orders = await _orderRepository.GetDeliveredDeliveryOrdersForDashboardAsync(
            branchFilter,
            from,
            to,
            deliveryManId,
            cancellationToken);

        var salesTicks = await _orderRepository.GetDeliveredOrdersSalesTicksForDashboardAsync(
            branchFilter,
            from,
            to,
            deliveryManId,
            cancellationToken);

        if (request.ExcludeWednesdays)
        {
            orders = orders.Where(o => DeliveryPerformanceAnalytics.StatusTime(o.StatusTimes, "delivered") is { } delivered
                && DeliveryPerformanceAnalytics.IncludesDay(delivered, true)).ToList();
            salesTicks = salesTicks.Where(t => DeliveryPerformanceAnalytics.IncludesDay(t.UpdatedAt, true)).ToList();
        }
        var agg = DeliveryDashboardAggregator.Build(orders, salesTicks, from, to, request.ExcludeWednesdays);

        var routeBase = _db.DeliveryRoutes.AsNoTracking()
            .Where(r => r.Status == DeliveryRouteStatus.Completed
                        && r.CompletedAtUtc != null
                        && r.CompletedAtUtc >= from
                        && r.CompletedAtUtc <= to);
        if (branchFilter.HasValue)
            routeBase = routeBase.Where(r => r.BranchId == branchFilter.Value);
        if (deliveryManId.HasValue)
            routeBase = routeBase.Where(r => r.DeliverymanId == deliveryManId.Value);

        var routeRowsRaw = await routeBase
            .Select(r => new
            {
                r.DeliverymanId,
                CompletedAtUtc = r.CompletedAtUtc!.Value,
                r.ActualDurationSeconds,
                r.MetaDurationSeconds,
                r.MetSla,
                r.PlannedDistanceMeters,
                r.ReturnToBranchMeters,
            })
            .ToListAsync(cancellationToken);

        var routeRows = routeRowsRaw
            .Where(r => DeliveryPerformanceAnalytics.IncludesDay(r.CompletedAtUtc, request.ExcludeWednesdays))
            .Select(r => new DeliveryRouteDashboardAggregator.RouteMetricRow(
                r.DeliverymanId,
                r.CompletedAtUtc,
                r.ActualDurationSeconds,
                r.MetaDurationSeconds,
                r.MetSla,
                r.PlannedDistanceMeters,
                r.ReturnToBranchMeters))
            .ToList();

        var buckets = DashboardDeliveryTimeBuckets.Create(from, to, request.ExcludeWednesdays);
        var routeAgg = DeliveryRouteDashboardAggregator.Build(routeRows, buckets);

        var recentRoutes = await routeBase
            .OrderByDescending(r => r.CompletedAtUtc)
            .Select(r => new DashboardDeliveryRouteHistoryItemDto
            {
                Id = r.Id,
                DeliverymanId = r.DeliverymanId,
                DeliverymanName = r.Deliveryman.Name ?? "",
                CompletedAtUtc = r.CompletedAtUtc,
                ActualDurationSeconds = r.ActualDurationSeconds,
                MetaDurationSeconds = r.MetaDurationSeconds,
                MetSla = r.MetSla,
                VarianceSeconds = r.ActualDurationSeconds != null && r.MetaDurationSeconds != null
                    ? r.ActualDurationSeconds - r.MetaDurationSeconds
                    : null,
                TotalDistanceMeters = (r.PlannedDistanceMeters ?? 0) + (r.ReturnToBranchMeters ?? 0),
            })
            .ToListAsync(cancellationToken);

        var routeMetricsDto = new DashboardDeliveryRouteMetricsDto
        {
            Performance = await DeliveryPerformanceAnalytics.BuildAsync(_db, branchFilter, deliveryManId, from, to,
                cancellationToken, Roles.IsDeliveryman(_currentUser.Role), request.ExcludeWednesdays),
            CompletedRoutesCount = routeAgg.CompletedRoutesCount,
            RoutesWithSlaDataCount = routeAgg.RoutesWithSlaDataCount,
            PeriodOnTimePercent = routeAgg.PeriodOnTimePercent,
            PeriodDelayedPercent = routeAgg.PeriodDelayedPercent,
            AvgActualRouteMinutes = routeAgg.AvgActualRouteMinutes,
            AvgMetaRouteMinutes = routeAgg.AvgMetaRouteMinutes,
            AvgDelayMinutesWhenDelayed = routeAgg.AvgDelayMinutesWhenDelayed,
            TotalDistanceKm = routeAgg.TotalDistanceKm,
            EvolutionRoutesCompleted = routeAgg.EvolutionRoutesCompleted,
            EvolutionOnTimePercent = routeAgg.EvolutionOnTimePercent,
            EvolutionDelayedPercent = routeAgg.EvolutionDelayedPercent,
            EvolutionAvgDelayMinutes = routeAgg.EvolutionAvgDelayMinutes,
            EvolutionAvgActualRouteMinutes = routeAgg.EvolutionAvgActualRouteMinutes,
            RecentRoutes = recentRoutes.Where(r => r.CompletedAtUtc.HasValue
                && DeliveryPerformanceAnalytics.IncludesDay(r.CompletedAtUtc.Value, request.ExcludeWednesdays)).Take(80).ToList(),
        };

        return new DashboardDeliveryResponseDto
        {
            AvgPrepMinutes = agg.AvgPrepMinutes,
            AvgDeliveryMinutes = agg.AvgDeliveryMinutes,
            Deliverymen = agg.Deliverymen.Select(d =>
            {
                var rd = routeAgg.PerDriver.TryGetValue(d.Id, out var x) ? x : default;
                return new DeliverymanEfficiencyApiDto
                {
                    Id = d.Id,
                    BranchId = d.BranchId,
                    Name = d.Name,
                    DeliveredCount = d.DeliveredCount,
                    AvgDeliveryMinutes = d.AvgDeliveryMinutes,
                    DeliveryFeeTotal = d.DeliveryFeeTotal,
                    RouteCompletedCount = rd.Count,
                    RouteOnTimePercent = rd.OnTimePercent,
                    AvgRouteActualMinutes = rd.AvgActual,
                };
            }).ToList(),
            EvolutionLabels = agg.EvolutionLabels,
            EvolutionDeliveries = agg.EvolutionDeliveries,
            EvolutionFees = agg.EvolutionFees,
            EvolutionSalesTotals = agg.EvolutionSalesTotals,
            PeriodFeeToSalesPercent = agg.PeriodFeeToSalesPercent,
            RouteMetrics = routeMetricsDto,
        };
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
            return requestedBranchId;
        if (Roles.IsDeliveryman(_currentUser.Role))
            return requestedBranchId;
        if (_currentUser.BranchId <= 0)
            throw new UnauthorizedAccessException("Usuario sin sucursal asignada.");
        return _currentUser.BranchId;
    }

    private async Task AssertDeliveryManInScopeAsync(
        int deliveryManId,
        int? branchFilter,
        CancellationToken cancellationToken)
    {
        if (Roles.IsDeliveryman(_currentUser.Role))
        {
            if (deliveryManId != _currentUser.Id)
                throw new UnauthorizedAccessException("No puedes consultar métricas de otro domiciliario.");
            return;
        }

        var user = await _userRepository.GetByIdAsync(deliveryManId, cancellationToken);
        if (user is null
            || (!user.CanDeliver && !await _db.Orders.AsNoTracking()
                .AnyAsync(order => order.DeliveryManId == user.Id, cancellationToken)))
            throw new UnauthorizedAccessException("Domiciliario no encontrado.");

        var scopeBranch = Roles.IsAdmin(_currentUser.Role) ? _currentUser.BranchId : branchFilter;
        var belongsToHistory = scopeBranch.HasValue && (
            await _db.Orders.AsNoTracking().AnyAsync(o => o.DeliveryManId == deliveryManId && o.BranchId == scopeBranch, cancellationToken)
            || await _db.DeliveryRoutes.AsNoTracking().AnyAsync(r => r.DeliverymanId == deliveryManId && r.BranchId == scopeBranch, cancellationToken));

        if (Roles.IsAdmin(_currentUser.Role))
        {
            if (user.BranchId != _currentUser.BranchId && !belongsToHistory)
                throw new UnauthorizedAccessException("Domiciliario no pertenece a tu sucursal.");
            return;
        }

        if (Roles.IsSuperadmin(_currentUser.Role))
        {
            if (branchFilter.HasValue && user.BranchId != branchFilter.Value && !belongsToHistory)
                throw new UnauthorizedAccessException("Domiciliario no pertenece a la sucursal seleccionada.");
        }
    }
}

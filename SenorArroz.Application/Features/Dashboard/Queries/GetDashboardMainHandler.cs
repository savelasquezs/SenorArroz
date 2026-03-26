using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Application.Features.Dashboard.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Models;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardMainHandler : IRequestHandler<GetDashboardMainQuery, DashboardMainResponseDto>
{
    private const int MaxActivityLimit = 50;
    private const int MaxKpiRangeDays = 400;

    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;

    public GetDashboardMainHandler(IOrderRepository orderRepository, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<DashboardMainResponseDto> Handle(GetDashboardMainQuery request, CancellationToken cancellationToken)
    {
        var branchFilter = ResolveBranchFilter(request.BranchId);
        var activityLimit = Math.Clamp(request.ActivityLimit <= 0 ? 20 : request.ActivityLimit, 1, MaxActivityLimit);

        var now = DateTime.UtcNow;

        PrincipalKpiSnapshot kpiDisplay;
        PrincipalKpiSnapshot kpiPrevPeriod;
        PrincipalKpiSnapshot kpiYearCompareCurrent;
        PrincipalKpiSnapshot kpiYearComparePrev;

        DateTime displayWindowStart;
        DateTime displayWindowEnd;

        if (request.KpiFromUtc.HasValue && request.KpiToUtc.HasValue)
        {
            var kFrom = request.KpiFromUtc.Value;
            var kTo = request.KpiToUtc.Value;
            if (kTo < kFrom)
                (kFrom, kTo) = (kTo, kFrom);

            if ((kTo.Date - kFrom.Date).TotalDays + 1 > MaxKpiRangeDays)
                kTo = kFrom.Date.AddDays(MaxKpiRangeDays - 1).AddDays(1).AddTicks(-1);

            displayWindowStart = kFrom;
            displayWindowEnd = kTo;

            var duration = kTo - kFrom;
            var prevTo = kFrom.AddTicks(-1);
            var prevFrom = prevTo - duration;

            kpiDisplay = await _orderRepository.GetPrincipalKpiSnapshotAsync(branchFilter, kFrom, kTo, cancellationToken);
            kpiPrevPeriod = await _orderRepository.GetPrincipalKpiSnapshotAsync(
                branchFilter,
                prevFrom,
                prevTo,
                cancellationToken);

            kpiYearCompareCurrent = kpiDisplay;
            kpiYearComparePrev = await _orderRepository.GetPrincipalKpiSnapshotAsync(
                branchFilter,
                kFrom.AddYears(-1),
                kTo.AddYears(-1),
                cancellationToken);
        }
        else
        {
            var weekEnd = now;
            var weekStart = now.AddDays(-7);
            displayWindowStart = weekStart;
            displayWindowEnd = weekEnd;

            var prevWeekEnd = weekStart;
            var prevWeekStart = weekStart.AddDays(-7);

            var yearEnd = now;
            var yearStart = now.AddDays(-365);
            var prevYearEnd = yearStart;
            var prevYearStart = yearStart.AddDays(-365);

            kpiDisplay = await _orderRepository.GetPrincipalKpiSnapshotAsync(branchFilter, weekStart, weekEnd, cancellationToken);
            kpiPrevPeriod = await _orderRepository.GetPrincipalKpiSnapshotAsync(
                branchFilter,
                prevWeekStart,
                prevWeekEnd,
                cancellationToken);

            kpiYearCompareCurrent = await _orderRepository.GetPrincipalKpiSnapshotAsync(
                branchFilter,
                yearStart,
                yearEnd,
                cancellationToken);
            kpiYearComparePrev = await _orderRepository.GetPrincipalKpiSnapshotAsync(
                branchFilter,
                prevYearStart,
                prevYearEnd,
                cancellationToken);
        }

        var cancelCurRate = kpiDisplay.CancellationRatePercent;
        var cancelPrevRate = kpiPrevPeriod.CancellationRatePercent;

        var pipeline = await _orderRepository.GetPrincipalPipelineCountsAsync(branchFilter, cancellationToken);
        var recentOrders = await _orderRepository.GetRecentOrdersForDashboardAsync(branchFilter, activityLimit, cancellationToken);

        var deliveredDeliveryOrders = await _orderRepository.GetDeliveredDeliveryOrdersForDashboardAsync(
            branchFilter,
            displayWindowStart,
            displayWindowEnd,
            deliveryManId: null,
            cancellationToken);
        var timeAggregates = DeliveryDashboardAggregator.Build(
            deliveredDeliveryOrders,
            Array.Empty<(DateTime, int)>(),
            displayWindowStart,
            displayWindowEnd);

        return new DashboardMainResponseDto
        {
            AvgPrepMinutes = timeAggregates.AvgPrepMinutes,
            AvgDeliveryMinutes = timeAggregates.AvgDeliveryMinutes,
            Kpis = new DashboardKpiDto
            {
                TotalSales = (int)Math.Min(kpiDisplay.TotalSalesCop, int.MaxValue),
                TotalSalesWeekChangePercent = PercentChangeDecimal(kpiDisplay.TotalSalesCop, kpiPrevPeriod.TotalSalesCop),
                TotalSalesYearChangePercent = PercentChangeDecimal(
                    kpiYearCompareCurrent.TotalSalesCop,
                    kpiYearComparePrev.TotalSalesCop),

                OrdersCount = kpiDisplay.CompletedOrderCount,
                OrdersWeekChangePercent = PercentChange(kpiDisplay.CompletedOrderCount, kpiPrevPeriod.CompletedOrderCount),
                OrdersYearChangePercent = PercentChange(
                    kpiYearCompareCurrent.CompletedOrderCount,
                    kpiYearComparePrev.CompletedOrderCount),

                AvgTicket = kpiDisplay.AvgTicketCop,
                AvgTicketWeekChangePercent = PercentChangeDecimal(kpiDisplay.AvgTicketCop, kpiPrevPeriod.AvgTicketCop),
                AvgTicketYearChangePercent = PercentChangeDecimal(
                    kpiYearCompareCurrent.AvgTicketCop,
                    kpiYearComparePrev.AvgTicketCop),

                CancellationRate = Math.Round(cancelCurRate, 2),
                CancellationRateWeekChangePercent = Math.Round(cancelCurRate - cancelPrevRate, 2),
                CancellationRateYearChangePercent = Math.Round(
                    kpiYearCompareCurrent.CancellationRatePercent - kpiYearComparePrev.CancellationRatePercent,
                    2),
            },
            Pipeline = new DashboardPipelineDto
            {
                Taken = pipeline.Taken,
                InPreparation = pipeline.InPreparation,
                Ready = pipeline.Ready,
                OnTheWay = pipeline.OnTheWay,
            },
            RecentActivity = MapActivity(recentOrders),
        };
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (_currentUser.Role == "superadmin")
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }

    private static double PercentChangeDecimal(decimal current, decimal previous)
    {
        if (previous == 0m)
            return current > 0m ? 100d : 0d;
        return Math.Round((double)((current - previous) / previous * 100m), 2);
    }

    private static double PercentChange(int current, int previous)
    {
        if (previous == 0)
            return current > 0 ? 100d : 0d;
        return Math.Round((current - (double)previous) / previous * 100d, 2);
    }

    private static List<DashboardActivityItemDto> MapActivity(List<Order> orders)
    {
        return orders.Select(o => new DashboardActivityItemDto
        {
            Id = o.Id,
            Type = "order",
            Description = BuildActivityDescription(o),
            Timestamp = o.UpdatedAt,
            Branch = o.Branch?.Name ?? string.Empty,
            BranchId = o.BranchId,
        }).ToList();
    }

    private static string BuildActivityDescription(Order o)
    {
        var who = !string.IsNullOrWhiteSpace(o.GuestName)
            ? o.GuestName!.Trim()
            : (o.Customer?.Name?.Trim() ?? "Cliente");
        return $"Pedido #{o.Id} · {SpanishStatus(o.Status)} · {who}";
    }

    private static string SpanishStatus(OrderStatus s) => s switch
    {
        OrderStatus.Taken => "Tomado",
        OrderStatus.InPreparation => "En preparación",
        OrderStatus.Ready => "Listo",
        OrderStatus.OnTheWay => "En camino",
        OrderStatus.Delivered => "Entregado",
        OrderStatus.Cancelled => "Cancelado",
        _ => s.ToString(),
    };
}

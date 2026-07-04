using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardSalesHourlyHandler
    : IRequestHandler<GetDashboardSalesHourlyQuery, DashboardSalesHourlyResponseDto>
{
    private const int MaxRangeDays = 400;

    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;

    public GetDashboardSalesHourlyHandler(IOrderRepository orderRepository, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<DashboardSalesHourlyResponseDto> Handle(
        GetDashboardSalesHourlyQuery request,
        CancellationToken cancellationToken)
    {
        var (from, to) = ColombiaTimeHelper.NormalizeDashboardRangeUtc(request.FromUtc, request.ToUtc, MaxRangeDays);
        var branchFilter = ResolveBranchFilter(request.BranchId);
        var dayOfWeek = NormalizeDayOfWeek(request.DayOfWeek);

        var points = await _orderRepository.GetDashboardSalesHourlyAnalyticsAsync(
            branchFilter,
            from,
            to,
            dayOfWeek,
            cancellationToken);

        var rows = points
            .OrderBy(p => p.Hour)
            .Select(p => new DashboardSalesHourlyPointDto
            {
                Hour = p.Hour,
                Label = FormatHourLabel(p.Hour),
                OrderCount = p.OrderCount,
                TotalSalesCop = p.TotalSalesCop,
                AverageDailySalesCop = Math.Round(p.AverageDailySalesCop, 2),
                MedianDailySalesCop = Math.Round(p.MedianDailySalesCop, 2),
                AverageTicketCop = Math.Round(p.AverageTicketCop, 2),
            })
            .ToList();

        var bestTotal = rows
            .OrderByDescending(p => p.TotalSalesCop)
            .ThenBy(p => p.Hour)
            .FirstOrDefault();
        var bestMedian = rows
            .OrderByDescending(p => p.MedianDailySalesCop)
            .ThenBy(p => p.Hour)
            .FirstOrDefault();

        var dailySales = await _orderRepository.GetDashboardSalesByDayAsync(
            branchFilter,
            from,
            to,
            dayOfWeek,
            cancellationToken);

        var dailyTotals = dailySales
            .GroupBy(p => p.Day.Date)
            .Select(g => (decimal)g.Sum(x => x.SalesCop))
            .OrderBy(v => v)
            .ToList();

        return new DashboardSalesHourlyResponseDto
        {
            Points = rows,
            Summary = new DashboardSalesHourlySummaryDto
            {
                HighestTotalSalesHour = bestTotal == null ? null : ToBestHour(bestTotal),
                HighestMedianSalesHour = bestMedian == null ? null : ToBestHour(bestMedian),
                DayOfWeek = dayOfWeek,
                DayOfWeekLabel = DayOfWeekLabel(dayOfWeek),
                MedianDailySalesCop = Math.Round(PercentileCont(dailyTotals, 0.5m), 2),
                AverageDailySalesCop = dailyTotals.Count == 0 ? 0 : Math.Round(dailyTotals.Average(), 2),
                TotalSalesCop = rows.Sum(p => p.TotalSalesCop),
            },
        };
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }

    private static int? NormalizeDayOfWeek(int? dayOfWeek)
    {
        if (!dayOfWeek.HasValue || dayOfWeek.Value < 1 || dayOfWeek.Value > 7)
            return null;
        return dayOfWeek.Value;
    }

    private static DashboardSalesBestHourDto ToBestHour(DashboardSalesHourlyPointDto p) => new()
    {
        Hour = p.Hour,
        Label = p.Label,
        TotalSalesCop = p.TotalSalesCop,
        MedianDailySalesCop = p.MedianDailySalesCop,
    };

    private static string FormatHourLabel(int hour)
    {
        var suffix = hour < 12 ? "a. m." : "p. m.";
        var displayHour = hour % 12;
        if (displayHour == 0)
            displayHour = 12;
        return $"{displayHour}:00 {suffix}";
    }

    private static string DayOfWeekLabel(int? dayOfWeek) => dayOfWeek switch
    {
        1 => "Lunes",
        2 => "Martes",
        3 => "Miercoles",
        4 => "Jueves",
        5 => "Viernes",
        6 => "Sabado",
        7 => "Domingo",
        _ => "Todos los dias",
    };

    private static decimal PercentileCont(IReadOnlyList<decimal> sortedValues, decimal percentile)
    {
        if (sortedValues.Count == 0)
            return 0;
        if (sortedValues.Count == 1)
            return sortedValues[0];

        var rank = (sortedValues.Count - 1) * percentile;
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
            return sortedValues[lower];

        var fraction = rank - lower;
        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * fraction;
    }
}

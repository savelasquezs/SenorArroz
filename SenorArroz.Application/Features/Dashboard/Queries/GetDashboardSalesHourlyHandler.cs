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

        var buckets = await _orderRepository.GetDashboardSalesDailyHourBucketsAsync(
            branchFilter,
            from,
            to,
            cancellationToken);

        if (dayOfWeek.HasValue)
            buckets = buckets.Where(b => b.DayOfWeek == dayOfWeek.Value).ToList();

        var totalPeriodSales = buckets.Sum(b => b.TotalSalesCop);

        var rows = buckets
            .GroupBy(b => b.Hour)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var orderedTotals = g.Select(x => (decimal)x.TotalSalesCop).OrderBy(x => x).ToList();
                var totalSales = g.Sum(x => x.TotalSalesCop);
                var orderCount = g.Sum(x => x.OrderCount);

                return new
                {
                    Hour = g.Key,
                    OrderCount = orderCount,
                    TotalSalesCop = totalSales,
                    AverageDailySalesCop = g.Average(x => (decimal)x.TotalSalesCop),
                    MedianDailySalesCop = PercentileCont(orderedTotals, 0.5m),
                    AverageTicketCop = orderCount == 0 ? 0 : (decimal)totalSales / orderCount,
                };
            })
            .Select(p => new DashboardSalesHourlyPointDto
            {
                Hour = p.Hour,
                Label = FormatHourLabel(p.Hour),
                OrderCount = p.OrderCount,
                TotalSalesCop = p.TotalSalesCop,
                AverageDailySalesCop = Math.Round(p.AverageDailySalesCop, 2),
                MedianDailySalesCop = Math.Round(p.MedianDailySalesCop, 2),
                AverageTicketCop = Math.Round(p.AverageTicketCop, 2),
                ParticipationPercent = totalPeriodSales == 0
                    ? 0
                    : Math.Round((decimal)p.TotalSalesCop * 100m / totalPeriodSales, 2),
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

        var dailyHistory = buckets
            .GroupBy(b => b.Day.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var totalSales = g.Sum(x => x.TotalSalesCop);
                var orderCount = g.Sum(x => x.OrderCount);
                var dow = g.First().DayOfWeek;
                return new DashboardSalesDailyPointDto
                {
                    Day = DateTime.SpecifyKind(g.Key, DateTimeKind.Unspecified),
                    Label = FormatDayLabel(g.Key, dow),
                    DayOfWeek = dow,
                    DayOfWeekLabel = DayOfWeekLabel(dow),
                    TotalSalesCop = totalSales,
                    OrderCount = orderCount,
                    AverageTicketCop = orderCount == 0 ? 0 : Math.Round((decimal)totalSales / orderCount, 2),
                };
            })
            .ToList();

        var heatmap = buckets
            .GroupBy(b => new { b.DayOfWeek, b.Hour })
            .OrderBy(g => g.Key.DayOfWeek)
            .ThenBy(g => g.Key.Hour)
            .Select(g => new DashboardSalesHeatmapPointDto
            {
                DayOfWeek = g.Key.DayOfWeek,
                DayOfWeekLabel = DayOfWeekLabel(g.Key.DayOfWeek),
                Hour = g.Key.Hour,
                HourLabel = FormatHourLabel(g.Key.Hour),
                MedianDailySalesCop = Math.Round(
                    PercentileCont(g.Select(x => (decimal)x.TotalSalesCop).OrderBy(x => x).ToList(), 0.5m),
                    2),
            })
            .ToList();

        var dailyTotals = dailyHistory
            .Select(p => (decimal)p.TotalSalesCop)
            .OrderBy(v => v)
            .ToList();

        return new DashboardSalesHourlyResponseDto
        {
            Points = rows,
            DailyHistory = dailyHistory,
            Heatmap = heatmap,
            Summary = new DashboardSalesHourlySummaryDto
            {
                HighestTotalSalesHour = bestTotal == null ? null : ToBestHour(bestTotal),
                HighestMedianSalesHour = bestMedian == null ? null : ToBestHour(bestMedian),
                DayOfWeek = dayOfWeek,
                DayOfWeekLabel = DayOfWeekLabel(dayOfWeek),
                MedianDailySalesCop = Math.Round(PercentileCont(dailyTotals, 0.5m), 2),
                AverageDailySalesCop = dailyTotals.Count == 0 ? 0 : Math.Round(dailyTotals.Average(), 2),
                TotalSalesCop = totalPeriodSales,
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

    private static string FormatDayLabel(DateTime day, int dayOfWeek)
    {
        var shortDay = dayOfWeek switch
        {
            1 => "lun",
            2 => "mar",
            3 => "mie",
            4 => "jue",
            5 => "vie",
            6 => "sab",
            7 => "dom",
            _ => string.Empty,
        };
        var month = day.Month switch
        {
            1 => "ene",
            2 => "feb",
            3 => "mar",
            4 => "abr",
            5 => "may",
            6 => "jun",
            7 => "jul",
            8 => "ago",
            9 => "sep",
            10 => "oct",
            11 => "nov",
            12 => "dic",
            _ => string.Empty,
        };
        return $"{shortDay} {day.Day} {month}";
    }

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

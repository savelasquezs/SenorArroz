using System.Globalization;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Domain.Models;

namespace SenorArroz.Application.Features.Dashboard.Services;

/// <summary>
/// Construye bloques alineados al front (<c>TimeEvolutionPanel</c>): mismas reglas de buckets (máx. días/meses/años).
/// </summary>
public static class SalesDashboardChartBuilder
{
    private static readonly CultureInfo EsCo = CultureInfo.GetCultureInfo("es-CO");
    private const int MaxDayBuckets = 62;
    private const int MaxMonthBuckets = 36;
    private const int MaxYearBuckets = 20;

    public static DashboardSalesEvolutionResponseDto BuildEvolution(
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyList<(int Id, string Name)> branchesInOrder,
        IReadOnlyList<SalesDayPoint> salesByDay,
        IReadOnlyList<OrdersDayPoint> ordersByDay,
        IReadOnlyList<SalesMonthPoint> salesByMonth,
        IReadOnlyList<OrdersMonthPoint> ordersByMonth,
        IReadOnlyList<SalesYearPoint> salesByYear,
        IReadOnlyList<OrdersYearPoint> ordersByYear,
        IReadOnlyList<SalesHourPoint> salesByHour,
        IReadOnlyList<OrdersHourPoint> ordersByHour)
    {
        return new DashboardSalesEvolutionResponseDto
        {
            SalesByDay = BuildSalesByDay(fromUtc, toUtc, branchesInOrder, salesByDay),
            OrdersByDay = BuildOrdersByDay(fromUtc, toUtc, ordersByDay),
            SalesByMonth = BuildSalesByMonth(fromUtc, toUtc, branchesInOrder, salesByMonth),
            OrdersByMonth = BuildOrdersByMonth(fromUtc, toUtc, ordersByMonth),
            SalesByYear = BuildSalesByYear(fromUtc, toUtc, branchesInOrder, salesByYear),
            OrdersByYear = BuildOrdersByYear(fromUtc, toUtc, ordersByYear),
            SalesByHour = BuildSalesByHour(branchesInOrder, salesByHour),
            OrdersByHour = BuildOrdersByHour(ordersByHour),
        };
    }

    private static SalesTimeSeriesBlockDto BuildSalesByDay(
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyList<(int Id, string Name)> branches,
        IReadOnlyList<SalesDayPoint> points)
    {
        var (days, labels) = EnumerateDays(fromUtc, toUtc);
        var map = points.ToLookup(p => (p.BranchId, p.Day.Date));

        var datasets = branches.Select(b => new SalesSeriesDatasetDto
        {
            Label = b.Name,
            Data = days.Select(d => map[(b.Id, d)].Sum(x => x.SalesCop)).ToList(),
        }).ToList();

        return new SalesTimeSeriesBlockDto { Labels = labels, Datasets = datasets };
    }

    private static OrdersTimelineBlockDto BuildOrdersByDay(
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyList<OrdersDayPoint> points)
    {
        var (days, labels) = EnumerateDays(fromUtc, toUtc);
        var map = points.ToDictionary(p => p.Day.Date, p => p.OrderCount);

        return new OrdersTimelineBlockDto
        {
            Labels = labels,
            Counts = days.Select(d => map.TryGetValue(d, out var c) ? c : 0).ToList(),
        };
    }

    private static (List<DateTime> Days, List<string> Labels) EnumerateDays(DateTime fromUtc, DateTime toUtc)
    {
        return ColombiaTimeHelper.EnumerateColombiaDashboardDays(fromUtc, toUtc, MaxDayBuckets, EsCo);
    }

    private static SalesTimeSeriesBlockDto BuildSalesByMonth(
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyList<(int Id, string Name)> branches,
        IReadOnlyList<SalesMonthPoint> points)
    {
        var (keys, labels) = EnumerateMonths(fromUtc, toUtc);
        var map = points.ToLookup(p => (p.BranchId, p.Year, p.Month));

        var datasets = branches.Select(b => new SalesSeriesDatasetDto
        {
            Label = b.Name,
            Data = keys.Select(k => map[(b.Id, k.Year, k.Month)].Sum(x => x.SalesCop)).ToList(),
        }).ToList();

        return new SalesTimeSeriesBlockDto { Labels = labels, Datasets = datasets };
    }

    private static OrdersTimelineBlockDto BuildOrdersByMonth(
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyList<OrdersMonthPoint> points)
    {
        var (keys, labels) = EnumerateMonths(fromUtc, toUtc);
        var map = points.ToDictionary(p => (p.Year, p.Month), p => p.OrderCount);

        return new OrdersTimelineBlockDto
        {
            Labels = labels,
            Counts = keys.Select(k => map.TryGetValue((k.Year, k.Month), out var c) ? c : 0).ToList(),
        };
    }

    private static (List<(int Year, int Month)> Keys, List<string> Labels) EnumerateMonths(
        DateTime fromUtc,
        DateTime toUtc)
    {
        return ColombiaTimeHelper.EnumerateColombiaDashboardMonths(fromUtc, toUtc, MaxMonthBuckets, EsCo);
    }

    private static SalesTimeSeriesBlockDto BuildSalesByYear(
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyList<(int Id, string Name)> branches,
        IReadOnlyList<SalesYearPoint> points)
    {
        var (years, labels) = EnumerateYears(fromUtc, toUtc);
        var map = points.ToLookup(p => (p.BranchId, p.Year));

        var datasets = branches.Select(b => new SalesSeriesDatasetDto
        {
            Label = b.Name,
            Data = years.Select(y => map[(b.Id, y)].Sum(x => x.SalesCop)).ToList(),
        }).ToList();

        return new SalesTimeSeriesBlockDto { Labels = labels, Datasets = datasets };
    }

    private static OrdersTimelineBlockDto BuildOrdersByYear(
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyList<OrdersYearPoint> points)
    {
        var (years, labels) = EnumerateYears(fromUtc, toUtc);
        var map = points.ToDictionary(p => p.Year, p => p.OrderCount);

        return new OrdersTimelineBlockDto
        {
            Labels = labels,
            Counts = years.Select(y => map.TryGetValue(y, out var c) ? c : 0).ToList(),
        };
    }

    private static (List<int> Years, List<string> Labels) EnumerateYears(DateTime fromUtc, DateTime toUtc)
    {
        return ColombiaTimeHelper.EnumerateColombiaDashboardYears(fromUtc, toUtc, MaxYearBuckets);
    }

    private static SalesTimeSeriesBlockDto BuildSalesByHour(
        IReadOnlyList<(int Id, string Name)> branches,
        IReadOnlyList<SalesHourPoint> points)
    {
        var labels = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToList();
        var map = points.ToLookup(p => (p.BranchId, p.Hour));

        var datasets = branches.Select(b => new SalesSeriesDatasetDto
        {
            Label = b.Name,
            Data = Enumerable.Range(0, 24)
                .Select(h => map[(b.Id, h)].Sum(x => x.SalesCop))
                .ToList(),
        }).ToList();

        return new SalesTimeSeriesBlockDto { Labels = labels, Datasets = datasets };
    }

    private static OrdersTimelineBlockDto BuildOrdersByHour(IReadOnlyList<OrdersHourPoint> points)
    {
        var labels = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToList();
        var map = points.ToDictionary(p => p.Hour, p => p.OrderCount);

        return new OrdersTimelineBlockDto
        {
            Labels = labels,
            Counts = Enumerable.Range(0, 24).Select(h => map.TryGetValue(h, out var c) ? c : 0).ToList(),
        };
    }
}

using System.Globalization;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Models;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardPrincipalSalesVsExpensesHandler
    : IRequestHandler<GetDashboardPrincipalSalesVsExpensesQuery, DashboardPrincipalSalesVsExpensesResponseDto>
{
    private const int MaxRangeDays = 400;
    private const int MaxDayBuckets = 62;
    private const int MaxMonthBuckets = 36;
    private const int MaxYearBuckets = 20;
    private const int MaxCategoriesShown = 8;

    private static readonly CultureInfo EsCo = CultureInfo.GetCultureInfo("es-CO");

    private readonly IOrderRepository _orderRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IExpenseDashboardRepository _expenseDashboardRepository;
    private readonly ICurrentUser _currentUser;

    public GetDashboardPrincipalSalesVsExpensesHandler(
        IOrderRepository orderRepository,
        IBranchRepository branchRepository,
        IExpenseDashboardRepository expenseDashboardRepository,
        ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _branchRepository = branchRepository;
        _expenseDashboardRepository = expenseDashboardRepository;
        _currentUser = currentUser;
    }

    public async Task<DashboardPrincipalSalesVsExpensesResponseDto> Handle(
        GetDashboardPrincipalSalesVsExpensesQuery request,
        CancellationToken cancellationToken)
    {
        var (from, to) = NormalizeRange(request.FromUtc, request.ToUtc);
        var branchFilter = ResolveBranchFilter(request.BranchId);
        var gran = NormalizeGranularity(request.Granularity);

        var allBranches = (await _branchRepository.GetAllAsync()).OrderBy(b => b.Name).ToList();
        var branchesInOrder = (branchFilter.HasValue
                ? allBranches.Where(b => b.Id == branchFilter.Value)
                : allBranches)
            .Select(b => (b.Id, b.Name))
            .ToList();

        var expenseRows = await _expenseDashboardRepository.GetTimeSeriesByCategoryAsync(
            branchFilter,
            from,
            to,
            gran,
            cancellationToken);

        return gran switch
        {
            "month" => await BuildMonthlyAsync(branchesInOrder, branchFilter, from, to, expenseRows, cancellationToken),
            "year" => await BuildYearlyAsync(branchesInOrder, branchFilter, from, to, expenseRows, cancellationToken),
            _ => await BuildDailyAsync(branchesInOrder, branchFilter, from, to, expenseRows, cancellationToken),
        };
    }

    private async Task<DashboardPrincipalSalesVsExpensesResponseDto> BuildDailyAsync(
        IReadOnlyList<(int Id, string Name)> branchesInOrder,
        int? branchFilter,
        DateTime from,
        DateTime to,
        List<ExpenseCategoryTimeBucketRow> expenseRows,
        CancellationToken cancellationToken)
    {
        var salesPoints = await _orderRepository.GetDashboardSalesByDayAsync(branchFilter, from, to, cancellationToken);
        var (days, labels) = EnumerateDays(from, to);
        var salesCop = SumSalesByDay(branchesInOrder, salesPoints, days);
        var buckets = days.Select(d => DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToList();
        var expenseCategories = BuildExpenseCategorySeries(expenseRows, buckets, "day");
        return new DashboardPrincipalSalesVsExpensesResponseDto
        {
            Granularity = "day",
            Labels = labels,
            SalesCop = salesCop,
            ExpenseCategories = expenseCategories,
        };
    }

    private async Task<DashboardPrincipalSalesVsExpensesResponseDto> BuildMonthlyAsync(
        IReadOnlyList<(int Id, string Name)> branchesInOrder,
        int? branchFilter,
        DateTime from,
        DateTime to,
        List<ExpenseCategoryTimeBucketRow> expenseRows,
        CancellationToken cancellationToken)
    {
        var salesPoints = await _orderRepository.GetDashboardSalesByMonthAsync(branchFilter, from, to, cancellationToken);
        var (keys, labels) = EnumerateMonths(from, to);
        var salesCop = SumSalesByMonth(branchesInOrder, salesPoints, keys);
        var buckets = keys
            .Select(k => new DateTime(k.Year, k.Month, 1, 0, 0, 0, DateTimeKind.Utc))
            .ToList();
        var expenseCategories = BuildExpenseCategorySeries(expenseRows, buckets, "month");
        return new DashboardPrincipalSalesVsExpensesResponseDto
        {
            Granularity = "month",
            Labels = labels,
            SalesCop = salesCop,
            ExpenseCategories = expenseCategories,
        };
    }

    private async Task<DashboardPrincipalSalesVsExpensesResponseDto> BuildYearlyAsync(
        IReadOnlyList<(int Id, string Name)> branchesInOrder,
        int? branchFilter,
        DateTime from,
        DateTime to,
        List<ExpenseCategoryTimeBucketRow> expenseRows,
        CancellationToken cancellationToken)
    {
        var salesPoints = await _orderRepository.GetDashboardSalesByYearAsync(branchFilter, from, to, cancellationToken);
        var (years, labels) = EnumerateYears(from, to);
        var salesCop = SumSalesByYear(branchesInOrder, salesPoints, years);
        var buckets = years.Select(y => new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToList();
        var expenseCategories = BuildExpenseCategorySeries(expenseRows, buckets, "year");
        return new DashboardPrincipalSalesVsExpensesResponseDto
        {
            Granularity = "year",
            Labels = labels,
            SalesCop = salesCop,
            ExpenseCategories = expenseCategories,
        };
    }

    private static List<PrincipalExpenseCategorySeriesDto> BuildExpenseCategorySeries(
        List<ExpenseCategoryTimeBucketRow> rows,
        List<DateTime> bucketStarts,
        string gran)
    {
        if (rows.Count == 0 || bucketStarts.Count == 0)
            return new List<PrincipalExpenseCategorySeriesDto>();

        var bucketSet = bucketStarts.ToHashSet();
        var byBucketCat = new Dictionary<(DateTime Bucket, int CatId), long>();

        foreach (var r in rows)
        {
            var b = NormalizeExpenseBucket(r.BucketStart, gran);
            if (!bucketSet.Contains(b))
                continue;
            var k = (b, r.CategoryId);
            byBucketCat[k] = byBucketCat.GetValueOrDefault(k) + r.TotalCop;
        }

        var categoryTotals = rows
            .GroupBy(r => r.CategoryId)
            .Select(g => new { Id = g.Key, Name = g.Max(x => x.CategoryName), Total = g.Sum(x => x.TotalCop) })
            .OrderByDescending(x => x.Total)
            .ToList();

        var top = categoryTotals.Take(MaxCategoriesShown).ToList();
        var topIds = top.Select(x => x.Id).ToHashSet();
        var hasOtros = categoryTotals.Count > MaxCategoriesShown;

        var series = new List<PrincipalExpenseCategorySeriesDto>();
        foreach (var t in top)
        {
            series.Add(new PrincipalExpenseCategorySeriesDto
            {
                CategoryId = t.Id,
                Name = string.IsNullOrWhiteSpace(t.Name) ? $"Categoría {t.Id}" : t.Name,
                AmountsCop = bucketStarts.Select(b => byBucketCat.GetValueOrDefault((b, t.Id), 0L)).ToList(),
            });
        }

        if (hasOtros)
        {
            series.Add(new PrincipalExpenseCategorySeriesDto
            {
                CategoryId = 0,
                Name = "Otros",
                AmountsCop = bucketStarts.Select(b =>
                {
                    long s = 0;
                    foreach (var kv in byBucketCat)
                    {
                        if (kv.Key.Bucket == b && !topIds.Contains(kv.Key.CatId))
                            s += kv.Value;
                    }

                    return s;
                }).ToList(),
            });
        }

        return series;
    }

    private static DateTime NormalizeExpenseBucket(DateTime raw, string gran) =>
        gran switch
        {
            "year" => new DateTime(raw.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "month" => new DateTime(raw.Year, raw.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => DateTime.SpecifyKind(raw.Date, DateTimeKind.Utc),
        };

    private static List<long> SumSalesByDay(
        IReadOnlyList<(int Id, string Name)> branches,
        IReadOnlyList<SalesDayPoint> points,
        List<DateTime> days)
    {
        var map = points.ToLookup(p => (p.BranchId, p.Day.Date));
        return days.Select(d => branches.Sum(b => map[(b.Id, d)].Sum(x => (long)x.SalesCop))).ToList();
    }

    private static List<long> SumSalesByMonth(
        IReadOnlyList<(int Id, string Name)> branches,
        IReadOnlyList<SalesMonthPoint> points,
        List<(int Year, int Month)> keys)
    {
        var map = points.ToLookup(p => (p.BranchId, p.Year, p.Month));
        return keys.Select(k => branches.Sum(b => map[(b.Id, k.Year, k.Month)].Sum(x => (long)x.SalesCop)))
            .ToList();
    }

    private static List<long> SumSalesByYear(
        IReadOnlyList<(int Id, string Name)> branches,
        IReadOnlyList<SalesYearPoint> points,
        List<int> years)
    {
        var map = points.ToLookup(p => (p.BranchId, p.Year));
        return years.Select(y => branches.Sum(b => map[(b.Id, y)].Sum(x => (long)x.SalesCop))).ToList();
    }

    private static (List<DateTime> Days, List<string> Labels) EnumerateDays(DateTime fromUtc, DateTime toUtc)
    {
        var from = fromUtc.Date;
        var to = toUtc.Date;
        if (to < from)
            (from, to) = (to, from);

        var days = new List<DateTime>();
        for (var d = from; d <= to && days.Count < MaxDayBuckets; d = d.AddDays(1))
            days.Add(d);

        var labels = days.Select(d => d.ToString("ddd d MMM", EsCo)).ToList();
        return (days, labels);
    }

    private static (List<(int Year, int Month)> Keys, List<string> Labels) EnumerateMonths(
        DateTime fromUtc,
        DateTime toUtc)
    {
        var s = new DateTime(fromUtc.Year, fromUtc.Month, 1, 0, 0, 0, fromUtc.Kind);
        var e = new DateTime(toUtc.Year, toUtc.Month, 1, 0, 0, 0, toUtc.Kind);
        if (e < s)
            (s, e) = (e, s);

        var keys = new List<(int Year, int Month)>();
        for (var cur = s; cur <= e && keys.Count < MaxMonthBuckets; cur = cur.AddMonths(1))
            keys.Add((cur.Year, cur.Month));

        var labels = keys
            .Select(k => new DateTime(k.Year, k.Month, 1).ToString("MMM yyyy", EsCo))
            .ToList();

        return (keys, labels);
    }

    private static (List<int> Years, List<string> Labels) EnumerateYears(DateTime fromUtc, DateTime toUtc)
    {
        var y0 = fromUtc.Year;
        var y1 = toUtc.Year;
        if (y1 < y0)
            (y0, y1) = (y1, y0);

        var years = new List<int>();
        for (var y = y0; y <= y1 && years.Count < MaxYearBuckets; y++)
            years.Add(y);

        var labels = years.Select(y => y.ToString()).ToList();
        return (years, labels);
    }

    private static (DateTime From, DateTime To) NormalizeRange(DateTime fromUtc, DateTime toUtc)
    {
        var from = fromUtc;
        var to = toUtc;
        if (to < from)
            (from, to) = (to, from);

        if ((to.Date - from.Date).TotalDays + 1 > MaxRangeDays)
            to = from.Date.AddDays(MaxRangeDays - 1).AddDays(1).AddTicks(-1);

        return (from, to);
    }

    private static string NormalizeGranularity(string? g)
    {
        var x = (g ?? "day").Trim().ToLowerInvariant();
        if (x == "hour")
            return "day";
        return x is "month" or "year" ? x : "day";
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (_currentUser.Role == "superadmin")
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }
}

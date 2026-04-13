using System.Globalization;
using MediatR;
using SenorArroz.Application.Common.Helpers;
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
        var (from, to) = ColombiaTimeHelper.NormalizeDashboardRangeUtc(request.FromUtc, request.ToUtc, MaxRangeDays);
        var branchFilter = ResolveBranchFilter(request.BranchId);
        var gran = NormalizeGranularity(request.Granularity);

        var allBranches = (await _branchRepository.GetAllAsync(cancellationToken)).OrderBy(b => b.Name).ToList();
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
        var (days, labels) = ColombiaTimeHelper.EnumerateColombiaDashboardDays(from, to, MaxDayBuckets, EsCo);
        var salesCop = SumSalesByDay(branchesInOrder, salesPoints, days);
        var buckets = days;
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
        var (keys, labels) = ColombiaTimeHelper.EnumerateColombiaDashboardMonths(from, to, MaxMonthBuckets, EsCo);
        var salesCop = SumSalesByMonth(branchesInOrder, salesPoints, keys);
        var buckets = keys
            .Select(k => new DateTime(k.Year, k.Month, 1, 0, 0, 0, DateTimeKind.Unspecified))
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
        var (years, labels) = ColombiaTimeHelper.EnumerateColombiaDashboardYears(from, to, MaxYearBuckets);
        var salesCop = SumSalesByYear(branchesInOrder, salesPoints, years);
        var buckets = years.Select(y => new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)).ToList();
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
            "year" => new DateTime(raw.Year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            "month" => new DateTime(raw.Year, raw.Month, 1, 0, 0, 0, DateTimeKind.Unspecified),
            _ => DateTime.SpecifyKind(raw.Date, DateTimeKind.Unspecified),
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

    private static string NormalizeGranularity(string? g)
    {
        var x = (g ?? "day").Trim().ToLowerInvariant();
        if (x is "hour" or "fortnight")
            return "day";
        return x is "month" or "year" ? x : "day";
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }
}

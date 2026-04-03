using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Models;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Repositories;

public class ExpenseDashboardRepository : IExpenseDashboardRepository
{
    private readonly ApplicationDbContext _context;

    public ExpenseDashboardRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    private IQueryable<ExpenseDetail> BaseDetailsInRange(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var q = _context.ExpenseDetails
            .AsNoTracking()
            .Where(ed =>
                ed.Header.CreatedAt >= fromUtc
                && ed.Header.CreatedAt <= toUtc);

        if (branchId.HasValue)
            q = q.Where(ed => ed.Header.BranchId == branchId.Value);

        return q;
    }

    public async Task<ExpenseDashboardPeriodTotals> GetPeriodTotalsAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var q = BaseDetailsInRange(branchId, fromUtc, toUtc);

        var totalCop = await q.SumAsync(ed => (long)(ed.Total ?? ed.Quantity * ed.Amount), cancellationToken);
        var lineCount = await q.CountAsync(cancellationToken);
        var headerCount = await q.Select(ed => ed.HeaderId).Distinct().CountAsync(cancellationToken);

        return new ExpenseDashboardPeriodTotals
        {
            TotalCop = totalCop,
            LineCount = lineCount,
            HeaderCount = headerCount,
        };
    }

    public async Task<List<ExpenseCategoryAggregateRow>> GetTotalsByCategoryAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var q = BaseDetailsInRange(branchId, fromUtc, toUtc);

        // Agrupar solo por CategoryId: evita clave anónima con Name que EF no traduce bien con Sum.
        // El nombre es único por id; Max/Min devuelve el mismo valor en el grupo.
        return await q
            .GroupBy(ed => ed.Expense.CategoryId)
            .Select(g => new ExpenseCategoryAggregateRow
            {
                CategoryId = g.Key,
                CategoryName = g.Max(ed => ed.Expense.Category.Name) ?? string.Empty,
                TotalCop = g.Sum(ed => (long)(ed.Total ?? ed.Quantity * ed.Amount)),
            })
            .OrderByDescending(x => x.TotalCop)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ExpenseTimeBucketRow>> GetTimeSeriesAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int? categoryId,
        int? expenseId,
        bool monthlyBuckets,
        CancellationToken cancellationToken = default)
    {
        var q = BaseDetailsInRange(branchId, fromUtc, toUtc);

        if (expenseId.HasValue)
            q = q.Where(ed => ed.ExpenseId == expenseId.Value);
        else if (categoryId.HasValue)
            q = q.Where(ed => ed.Expense.CategoryId == categoryId.Value);

        var rows = await q
            .Select(ed => new
            {
                ed.Header.CreatedAt,
                LineCop = (long)(ed.Total ?? ed.Quantity * ed.Amount),
            })
            .ToListAsync(cancellationToken);

        if (monthlyBuckets)
        {
            return rows
                .GroupBy(r => ColombiaTimeHelper.ExpenseColombiaYearMonth(r.CreatedAt))
                .Select(g => new ExpenseTimeBucketRow
                {
                    BucketStart = new DateTime(g.Key.Year, g.Key.Month, 1, 0, 0, 0, DateTimeKind.Unspecified),
                    TotalCop = g.Sum(x => x.LineCop),
                })
                .OrderBy(x => x.BucketStart)
                .ToList();
        }

        return rows
            .GroupBy(r => ColombiaTimeHelper.ExpenseColombiaCalendarDate(r.CreatedAt))
            .Select(g => new ExpenseTimeBucketRow
            {
                BucketStart = g.Key,
                TotalCop = g.Sum(x => x.LineCop),
            })
            .OrderBy(x => x.BucketStart)
            .ToList();
    }

    public async Task<List<ExpenseCategoryTimeBucketRow>> GetTimeSeriesByCategoryAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        string granularity,
        CancellationToken cancellationToken = default)
    {
        var q = BaseDetailsInRange(branchId, fromUtc, toUtc);
        var g = granularity?.ToLowerInvariant() ?? "day";

        var rows = await q
            .Select(ed => new
            {
                ed.Header.CreatedAt,
                ed.Expense.CategoryId,
                CategoryName = ed.Expense.Category.Name ?? string.Empty,
                LineCop = (long)(ed.Total ?? ed.Quantity * ed.Amount),
            })
            .ToListAsync(cancellationToken);

        if (g == "year")
        {
            return rows
                .GroupBy(r => new { Year = ColombiaTimeHelper.ExpenseColombiaYear(r.CreatedAt), r.CategoryId })
                .Select(x => new ExpenseCategoryTimeBucketRow
                {
                    BucketStart = new DateTime(x.Key.Year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                    CategoryId = x.Key.CategoryId,
                    CategoryName = x.Max(r => r.CategoryName) ?? string.Empty,
                    TotalCop = x.Sum(r => r.LineCop),
                })
                .OrderBy(r => r.BucketStart)
                .ThenBy(r => r.CategoryName)
                .ToList();
        }

        if (g == "month")
        {
            return rows
                .GroupBy(r =>
                {
                    var ym = ColombiaTimeHelper.ExpenseColombiaYearMonth(r.CreatedAt);
                    return new { ym.Year, ym.Month, r.CategoryId };
                })
                .Select(x => new ExpenseCategoryTimeBucketRow
                {
                    BucketStart = new DateTime(x.Key.Year, x.Key.Month, 1, 0, 0, 0, DateTimeKind.Unspecified),
                    CategoryId = x.Key.CategoryId,
                    CategoryName = x.Max(r => r.CategoryName) ?? string.Empty,
                    TotalCop = x.Sum(r => r.LineCop),
                })
                .OrderBy(r => r.BucketStart)
                .ThenBy(r => r.CategoryName)
                .ToList();
        }

        return rows
            .GroupBy(r => new { Day = ColombiaTimeHelper.ExpenseColombiaCalendarDate(r.CreatedAt), r.CategoryId })
            .Select(x => new ExpenseCategoryTimeBucketRow
            {
                BucketStart = x.Key.Day,
                CategoryId = x.Key.CategoryId,
                CategoryName = x.Max(r => r.CategoryName) ?? string.Empty,
                TotalCop = x.Sum(r => r.LineCop),
            })
            .OrderBy(r => r.BucketStart)
            .ThenBy(r => r.CategoryName)
            .ToList();
    }

    public async Task<Dictionary<int, long>> GetTotalsByExpenseCatalogIdsInRangeAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyCollection<int> expenseCatalogIds,
        CancellationToken cancellationToken = default)
    {
        if (expenseCatalogIds == null || expenseCatalogIds.Count == 0)
            return new Dictionary<int, long>();

        var q = BaseDetailsInRange(branchId, fromUtc, toUtc)
            .Where(ed => expenseCatalogIds.Contains(ed.ExpenseId));

        return await q
            .GroupBy(ed => ed.ExpenseId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Sum(ed => (long)(ed.Total ?? ed.Quantity * ed.Amount)),
                cancellationToken);
    }
}

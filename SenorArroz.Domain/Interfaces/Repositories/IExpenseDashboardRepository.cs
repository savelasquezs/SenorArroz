using SenorArroz.Domain.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IExpenseDashboardRepository
{
    Task<ExpenseDashboardPeriodTotals> GetPeriodTotalsAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<List<ExpenseCategoryAggregateRow>> GetTotalsByCategoryAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Serie temporal. Sin <paramref name="categoryId"/> ni <paramref name="expenseId"/> = total.
    /// </summary>
    Task<List<ExpenseTimeBucketRow>> GetTimeSeriesAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int? categoryId,
        int? expenseId,
        bool monthlyBuckets,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Serie por categoría alineada a buckets <c>day</c>, <c>month</c> o <c>year</c> (CreatedAt del encabezado).
    /// </summary>
    Task<List<ExpenseCategoryTimeBucketRow>> GetTimeSeriesByCategoryAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        string granularity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Total COP por ítem de catálogo de gasto (<c>Expense.Id</c>) en el rango, filtrado a los ids indicados.
    /// </summary>
    Task<Dictionary<int, long>> GetTotalsByExpenseCatalogIdsInRangeAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyCollection<int> expenseCatalogIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Top N ítems de catálogo por suma de importes de línea en el rango (agrupa varias compras del mismo ítem).
    /// Misma base temporal y sucursal que el resto del dashboard de gastos.
    /// </summary>
    Task<List<ExpenseCatalogAggregateRow>> GetTopExpenseCatalogAggregatesAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int categoryId,
        int? expenseId,
        int take,
        CancellationToken cancellationToken = default);
}

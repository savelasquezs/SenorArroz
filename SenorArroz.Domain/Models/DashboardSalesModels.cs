namespace SenorArroz.Domain.Models;

/// <summary>Fila agregada por sucursal para comparación en dashboard ventas.</summary>
public class BranchSalesComparisonAggregate
{
    public int BranchId { get; set; }
    public int SalesTotal { get; set; }
    public int OrdersTotal { get; set; }
    public int SalesDelivery { get; set; }
    public int SalesOnsite { get; set; }
    public int OrdersDelivery { get; set; }
    public int OrdersOnsite { get; set; }
}

public sealed record SalesDayPoint(int BranchId, DateTime Day, int SalesCop);

public sealed record OrdersDayPoint(DateTime Day, int OrderCount);

public sealed record SalesMonthPoint(int BranchId, int Year, int Month, int SalesCop);

public sealed record OrdersMonthPoint(int Year, int Month, int OrderCount);

public sealed record SalesYearPoint(int BranchId, int Year, int SalesCop);

public sealed record OrdersYearPoint(int Year, int OrderCount);

public sealed record SalesHourPoint(int BranchId, int Hour, int SalesCop);

public sealed record OrdersHourPoint(int Hour, int OrderCount);

/// <summary>Ventas agregadas por hora del dia, calculadas desde buckets diarios por hora.</summary>
public sealed record SalesHourlyAnalyticsPoint(
    int Hour,
    int OrderCount,
    long TotalSalesCop,
    decimal AverageDailySalesCop,
    decimal MedianDailySalesCop,
    decimal AverageTicketCop);

/// <summary>Agregado por producto (líneas de pedido, pedidos no cancelados).</summary>
public class SalesProductAggregateRow
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public long RevenueCop { get; set; }
}

/// <summary>
/// Ventas por producto con categoría de menú (costeo dashboard).
/// </summary>
public class SalesProductCategoryAggregateRow
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public long RevenueCop { get; set; }
}

/// <summary>Agregado por categoría de producto (mismas líneas de pedido).</summary>
public class SalesCategoryAggregateRow
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public long RevenueCop { get; set; }
}

/// <summary>
/// Gramos vendidos por categoría: suma de (cantidad × peso unitario) solo si el producto tiene <c>weight_grams</c>.
/// </summary>
public class SalesCategoryWeightRow
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public long TotalWeightGrams { get; set; }
}

/// <summary>Gramos vendidos por producto: suma de (cantidad × peso) solo si el producto tiene peso.</summary>
public class SalesProductWeightRow
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public long TotalWeightGrams { get; set; }
}

/// <summary>Punto de la serie temporal de peso (g) para una categoría en un bucket día/mes/año.</summary>
public sealed record SalesCategoryWeightEvolutionPoint(DateTime BucketStartUtc, long TotalWeightGrams);

/// <summary>Serie temporal de peso para una categoría (varias series cuando no se filtra por categoría).</summary>
public sealed record SalesCategoryWeightEvolutionSeries(
    int CategoryId,
    string CategoryName,
    IReadOnlyList<SalesCategoryWeightEvolutionPoint> Points);

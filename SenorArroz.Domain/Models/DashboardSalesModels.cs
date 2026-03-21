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

/// <summary>Agregado por producto (líneas de pedido, pedidos no cancelados).</summary>
public class SalesProductAggregateRow
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public long RevenueCop { get; set; }
}

namespace SenorArroz.Domain.Models;

/// <summary>Totales de gastos en un rango (líneas de <c>ExpenseDetail</c> por fecha del comprobante).</summary>
public class ExpenseDashboardPeriodTotals
{
    public long TotalCop { get; set; }
    public int HeaderCount { get; set; }
    public int LineCount { get; set; }
}

public class ExpenseCategoryAggregateRow
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public long TotalCop { get; set; }
}

public class ExpenseTimeBucketRow
{
    /// <summary>Inicio del bucket (día 00:00 o primer día del mes, según granularidad).</summary>
    public DateTime BucketStart { get; set; }
    public long TotalCop { get; set; }
}

/// <summary>Gasto agregado por bucket temporal y categoría (para gráficos apilados).</summary>
public class ExpenseCategoryTimeBucketRow
{
    public DateTime BucketStart { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public long TotalCop { get; set; }
}

/// <summary>
/// Línea de detalle de gasto rankeada por importe (dashboard: mayores líneas por categoría).
/// </summary>
public class ExpenseTopDetailLineRow
{
    public int DetailId { get; set; }
    public int HeaderId { get; set; }
    public DateTime HeaderCreatedAtUtc { get; set; }
    public long LineCop { get; set; }
    public string ExpenseName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
}

namespace SenorArroz.Application.Features.Dashboard.DTOs;

public class DashboardExpenseSummaryResponseDto
{
    public long TotalCop { get; set; }
    public int HeaderCount { get; set; }
    public int LineCount { get; set; }
    public double AvgDailyCop { get; set; }
    public double AvgTicketCop { get; set; }
    public long PreviousPeriodTotalCop { get; set; }
    public int PreviousPeriodHeaderCount { get; set; }
    /// <summary>Variación % del total respecto al periodo anterior contiguo (misma duración).</summary>
    public double TotalChangeFromPreviousPercent { get; set; }
}

public class DashboardExpenseByCategoryResponseDto
{
    public List<ExpenseCategorySliceDto> Slices { get; set; } = new();
}

public class ExpenseCategorySliceDto
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long TotalCop { get; set; }
    public double Percent { get; set; }
}

public class DashboardExpenseTimeSeriesResponseDto
{
    public List<string> Labels { get; set; } = new();
    public List<long> AmountsCop { get; set; } = new();
    /// <summary><c>day</c> o <c>month</c>.</summary>
    public string Granularity { get; set; } = "day";
    /// <summary>Texto para leyenda (Total, categoría o gasto).</summary>
    public string SeriesLabel { get; set; } = string.Empty;
}

/// <summary>Top de ítems de catálogo por suma de importes de línea en el rango.</summary>
public class DashboardExpenseTopLinesResponseDto
{
    public List<ExpenseCatalogAggregateItemDto> Items { get; set; } = new();
    /// <summary>Límite efectivo usado (1–500).</summary>
    public int LimitApplied { get; set; }
}

/// <summary>Suma de líneas en el rango, agrupada por ítem de catálogo de gasto.</summary>
public class ExpenseCatalogAggregateItemDto
{
    public int ExpenseId { get; set; }
    public string ExpenseName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public long TotalCop { get; set; }
    public int LineCount { get; set; }
}

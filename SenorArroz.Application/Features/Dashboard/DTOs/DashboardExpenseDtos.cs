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

/// <summary>Mayores líneas de detalle de gasto en el rango (por categoría / ítem de catálogo).</summary>
public class DashboardExpenseTopLinesResponseDto
{
    public List<ExpenseTopLineItemDto> Items { get; set; } = new();
    /// <summary>Límite efectivo usado (1–500).</summary>
    public int LimitApplied { get; set; }
}

public class ExpenseTopLineItemDto
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

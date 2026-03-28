namespace SenorArroz.Application.Features.Dashboard.DTOs;

/// <summary>
/// Ventas vs gastos (gastos por categoría, misma escala temporal que la evolución de ventas).
/// </summary>
public class DashboardPrincipalSalesVsExpensesResponseDto
{
    /// <summary><c>day</c>, <c>month</c> o <c>year</c>.</summary>
    public string Granularity { get; set; } = "day";

    public List<string> Labels { get; set; } = new();
    public List<long> SalesCop { get; set; } = new();
    public List<PrincipalExpenseCategorySeriesDto> ExpenseCategories { get; set; } = new();
}

public class PrincipalExpenseCategorySeriesDto
{
    /// <summary>0 = agregado “Otros” (varias categorías).</summary>
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<long> AmountsCop { get; set; } = new();
}

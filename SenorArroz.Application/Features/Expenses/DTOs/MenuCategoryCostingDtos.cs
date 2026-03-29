namespace SenorArroz.Application.Features.Expenses.DTOs;

public class MenuCategoryExpenseBreakdownDto
{
    public string ExpenseName { get; set; } = string.Empty;
    public long AllocatedCop { get; set; }
}

public class MenuProductCostingRowDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public long RevenueCop { get; set; }
    public long GramsSold { get; set; }
    public decimal? AvgPricePerGramCop { get; set; }
    public decimal? AllocatedCostPerGramCop { get; set; }
    public long AllocatedCostCop { get; set; }
    public decimal? MarginPercent { get; set; }
}

public class MenuCategoryCostingBlockDto
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public long TotalAllocatedCostCop { get; set; }
    public long TotalWeightGramsSold { get; set; }
    public decimal? BlendedCostPerGramCop { get; set; }
    public long TotalRevenueCop { get; set; }
    public List<MenuCategoryExpenseBreakdownDto> ExpenseBreakdown { get; set; } = new();
    public List<MenuProductCostingRowDto> Products { get; set; } = new();
}

public class MenuCategoryCostingDashboardResponseDto
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int? BranchId { get; set; }
    public List<MenuCategoryCostingBlockDto> Categories { get; set; } = new();
}

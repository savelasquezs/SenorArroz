using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Expenses.DTOs;

public class ExpenseMenuAttributionLineDto
{
    public int ExpenseId { get; set; }
    public string ExpenseName { get; set; } = string.Empty;
    public long TotalExpenseInPeriodCop { get; set; }
    public ExpenseMenuTargetType TargetType { get; set; }
    public int TargetId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public long AllocatedCop { get; set; }
    public long TotalWeightGramsSold { get; set; }
    public decimal? CostPerGramCop { get; set; }
}

public class ExpenseMenuAttributionResponseDto
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int? BranchId { get; set; }
    public List<ExpenseMenuAttributionLineDto> Lines { get; set; } = new();
}

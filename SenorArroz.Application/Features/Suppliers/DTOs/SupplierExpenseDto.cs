namespace SenorArroz.Application.Features.Suppliers.DTOs;

public class SupplierExpenseDto
{
    public int ExpenseId { get; set; }
    public string ExpenseName { get; set; } = string.Empty;
    public string ExpenseUnit { get; set; } = string.Empty;
    public int UsageCount { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public decimal? LastUnitPrice { get; set; }
}



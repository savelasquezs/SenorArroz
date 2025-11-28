namespace SenorArroz.Application.Features.ExpenseHeaders.DTOs;

public class ExpenseDetailDto
{
    public int Id { get; set; }
    public int HeaderId { get; set; }
    public int ExpenseId { get; set; }
    public string ExpenseName { get; set; } = string.Empty;
    public string ExpenseCategoryName { get; set; } = string.Empty;
    public string ExpenseUnit { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int Amount { get; set; }
    public int? Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}


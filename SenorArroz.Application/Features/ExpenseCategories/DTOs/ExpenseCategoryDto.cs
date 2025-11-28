// SenorArroz.Application/Features/ExpenseCategories/DTOs/ExpenseCategoryDto.cs
namespace SenorArroz.Application.Features.ExpenseCategories.DTOs;

public class ExpenseCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Statistics
    public int TotalExpenses { get; set; }
}


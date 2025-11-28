// SenorArroz.Application/Features/Expenses/DTOs/ExpenseDto.cs
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Expenses.DTOs;

public class ExpenseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public ExpenseUnit Unit { get; set; }
    public string UnitDisplay { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}


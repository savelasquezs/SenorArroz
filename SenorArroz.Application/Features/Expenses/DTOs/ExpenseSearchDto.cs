// SenorArroz.Application/Features/Expenses/DTOs/ExpenseSearchDto.cs
namespace SenorArroz.Application.Features.Expenses.DTOs;

public class ExpenseSearchDto
{
    public int? CategoryId { get; set; }
    public string? Name { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "name";
    public string SortOrder { get; set; } = "asc";
}



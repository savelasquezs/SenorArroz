// SenorArroz.Application/Features/ExpenseCategories/DTOs/ExpenseCategorySearchDto.cs
namespace SenorArroz.Application.Features.ExpenseCategories.DTOs;

public class ExpenseCategorySearchDto
{
    public string? Name { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "name";
    public string SortOrder { get; set; } = "asc";
}


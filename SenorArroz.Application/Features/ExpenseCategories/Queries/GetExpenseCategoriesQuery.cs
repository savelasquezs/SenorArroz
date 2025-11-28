// SenorArroz.Application/Features/ExpenseCategories/Queries/GetExpenseCategoriesQuery.cs
using MediatR;
using SenorArroz.Application.Features.ExpenseCategories.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.ExpenseCategories.Queries;

public class GetExpenseCategoriesQuery : IRequest<PagedResult<ExpenseCategoryDto>>
{
    public string? Name { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "name";
    public string SortOrder { get; set; } = "asc";
}


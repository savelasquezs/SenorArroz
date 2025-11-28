// SenorArroz.Application/Features/Expenses/Queries/GetExpensesQuery.cs
using MediatR;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Expenses.Queries;

public class GetExpensesQuery : IRequest<PagedResult<ExpenseDto>>
{
    public int? CategoryId { get; set; }
    public string? Name { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "name";
    public string SortOrder { get; set; } = "asc";
}



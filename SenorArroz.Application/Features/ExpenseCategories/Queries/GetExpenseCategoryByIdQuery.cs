// SenorArroz.Application/Features/ExpenseCategories/Queries/GetExpenseCategoryByIdQuery.cs
using MediatR;
using SenorArroz.Application.Features.ExpenseCategories.DTOs;

namespace SenorArroz.Application.Features.ExpenseCategories.Queries;

public class GetExpenseCategoryByIdQuery : IRequest<ExpenseCategoryDto?>
{
    public int Id { get; set; }
}



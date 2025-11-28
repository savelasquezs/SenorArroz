// SenorArroz.Application/Features/ExpenseCategories/Queries/GetAllExpenseCategoriesQuery.cs
using MediatR;
using SenorArroz.Application.Features.ExpenseCategories.DTOs;

namespace SenorArroz.Application.Features.ExpenseCategories.Queries;

public class GetAllExpenseCategoriesQuery : IRequest<IEnumerable<ExpenseCategoryDto>>
{
}


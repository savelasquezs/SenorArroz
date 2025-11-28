// SenorArroz.Application/Features/Expenses/Queries/GetAllExpensesQuery.cs
using MediatR;
using SenorArroz.Application.Features.Expenses.DTOs;

namespace SenorArroz.Application.Features.Expenses.Queries;

public class GetAllExpensesQuery : IRequest<IEnumerable<ExpenseDto>>
{
}



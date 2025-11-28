// SenorArroz.Application/Features/Expenses/Queries/GetExpenseByIdQuery.cs
using MediatR;
using SenorArroz.Application.Features.Expenses.DTOs;

namespace SenorArroz.Application.Features.Expenses.Queries;

public class GetExpenseByIdQuery : IRequest<ExpenseDto?>
{
    public int Id { get; set; }
}


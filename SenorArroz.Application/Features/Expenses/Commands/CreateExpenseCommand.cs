// SenorArroz.Application/Features/Expenses/Commands/CreateExpenseCommand.cs
using MediatR;
using SenorArroz.Application.Features.Expenses.DTOs;

namespace SenorArroz.Application.Features.Expenses.Commands;

public class CreateExpenseCommand : IRequest<ExpenseDto>
{
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public Domain.Enums.ExpenseUnit Unit { get; set; }
}


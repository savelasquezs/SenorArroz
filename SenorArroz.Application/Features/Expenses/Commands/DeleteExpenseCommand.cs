// SenorArroz.Application/Features/Expenses/Commands/DeleteExpenseCommand.cs
using MediatR;

namespace SenorArroz.Application.Features.Expenses.Commands;

public class DeleteExpenseCommand : IRequest<bool>
{
    public int Id { get; set; }
}



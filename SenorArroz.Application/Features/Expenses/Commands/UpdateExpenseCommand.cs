// SenorArroz.Application/Features/Expenses/Commands/UpdateExpenseCommand.cs
using MediatR;
using SenorArroz.Application.Features.Expenses.DTOs;

namespace SenorArroz.Application.Features.Expenses.Commands;

public class UpdateExpenseCommand : IRequest<ExpenseDto>
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public Domain.Enums.ExpenseUnit Unit { get; set; }
}



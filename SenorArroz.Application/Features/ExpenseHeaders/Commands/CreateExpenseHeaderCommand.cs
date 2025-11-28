using MediatR;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;

namespace SenorArroz.Application.Features.ExpenseHeaders.Commands;

public class CreateExpenseHeaderCommand : IRequest<ExpenseHeaderDto>
{
    public CreateExpenseHeaderDto ExpenseHeader { get; set; } = null!;
}


using MediatR;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;

namespace SenorArroz.Application.Features.ExpenseHeaders.Commands;

public class UpdateExpenseHeaderCommand : IRequest<ExpenseHeaderDto>
{
    public int Id { get; set; }
    public UpdateExpenseHeaderDto ExpenseHeader { get; set; } = null!;
}


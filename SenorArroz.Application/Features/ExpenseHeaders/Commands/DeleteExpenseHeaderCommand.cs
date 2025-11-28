using MediatR;

namespace SenorArroz.Application.Features.ExpenseHeaders.Commands;

public class DeleteExpenseHeaderCommand : IRequest<bool>
{
    public int Id { get; set; }
}



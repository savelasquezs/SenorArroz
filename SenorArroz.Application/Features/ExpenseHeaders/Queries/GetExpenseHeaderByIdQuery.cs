using MediatR;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;

namespace SenorArroz.Application.Features.ExpenseHeaders.Queries;

public class GetExpenseHeaderByIdQuery : IRequest<ExpenseHeaderDto?>
{
    public int Id { get; set; }
}

